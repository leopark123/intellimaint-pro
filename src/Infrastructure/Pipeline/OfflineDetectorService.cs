using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.Pipeline;

/// <summary>
/// v56: 离线检测服务 - 定期检查设备/标签是否超时无数据
/// </summary>
public sealed class OfflineDetectorService : BackgroundService
{
    private readonly LastDataTracker _lastDataTracker;
    private readonly IAlarmRuleRepository _ruleRepo;
    private readonly IAlarmRepository _alarmRepo;
    private readonly ILogger<OfflineDetectorService> _logger;
    private readonly AlarmAggregationService? _aggregationService;

    // 检查间隔（毫秒）
    private const int CheckIntervalMs = 10_000;

    // 规则刷新间隔（毫秒）
    private const int RuleRefreshIntervalMs = 30_000;

    // 告警防抖（毫秒）
    private const int AlarmDebounceMs = 60_000;

    // 规则缓存
    private readonly object _rulesLock = new();
    private List<AlarmRule> _offlineRules = new();
    private DateTimeOffset _lastRefreshUtc = DateTimeOffset.MinValue;

    // 防抖缓存：Key = ruleId, Value = 最后创建告警的时间
    private readonly ConcurrentDictionary<string, long> _lastAlarmCreatedUtcMs = new();

    // P0: 缓存清理配置（防止内存泄漏）
    private const long CacheExpiryMs = 24 * 60 * 60 * 1000;  // 24小时过期
    private DateTimeOffset _lastCleanupUtc = DateTimeOffset.MinValue;
    private const int CleanupIntervalMs = 300_000;  // 5分钟清理一次

    public OfflineDetectorService(
        LastDataTracker lastDataTracker,
        IAlarmRuleRepository ruleRepo,
        IAlarmRepository alarmRepo,
        ILogger<OfflineDetectorService> logger,
        AlarmAggregationService? aggregationService = null)
    {
        _lastDataTracker = lastDataTracker;
        _ruleRepo = ruleRepo;
        _alarmRepo = alarmRepo;
        _logger = logger;
        _aggregationService = aggregationService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OfflineDetectorService starting...");

        // 初始加载规则
        await RefreshRulesAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(CheckIntervalMs, stoppingToken);

                // 周期性刷新规则
                var now = DateTimeOffset.UtcNow;
                if ((now - _lastRefreshUtc).TotalMilliseconds >= RuleRefreshIntervalMs)
                {
                    await RefreshRulesAsync(stoppingToken);
                }

                // P0: 定期清理过期缓存
                if ((now - _lastCleanupUtc).TotalMilliseconds >= CleanupIntervalMs)
                {
                    CleanupExpiredCache();
                    _lastCleanupUtc = now;
                }

                // 检查离线规则
                await CheckOfflineRulesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("OfflineDetectorService stopping...");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OfflineDetectorService check loop");
            }
        }
    }

    /// <summary>
    /// 刷新离线规则缓存
    /// </summary>
    private async Task RefreshRulesAsync(CancellationToken ct)
    {
        try
        {
            var allRules = await _ruleRepo.ListEnabledAsync(ct);

            // 只保留离线检测规则
            var offlineRules = allRules
                .Where(r => r.ConditionType.Equals("offline", StringComparison.OrdinalIgnoreCase))
                .ToList();

            lock (_rulesLock)
            {
                _offlineRules = offlineRules;
            }

            _lastRefreshUtc = DateTimeOffset.UtcNow;
            _logger.LogDebug("Refreshed offline rules: {Count} rules loaded", offlineRules.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh offline rules");
        }
    }

    /// <summary>
    /// 检查所有离线规则
    /// </summary>
    private async Task CheckOfflineRulesAsync(CancellationToken ct)
    {
        List<AlarmRule> rulesToCheck;
        lock (_rulesLock)
        {
            rulesToCheck = new List<AlarmRule>(_offlineRules);
        }

        if (rulesToCheck.Count == 0)
            return;

        var nowUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        foreach (var rule in rulesToCheck)
        {
            try
            {
                await CheckSingleRuleAsync(rule, nowUtcMs, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking offline rule {RuleId}", rule.RuleId);
            }
        }
    }

    /// <summary>
    /// 检查单个离线规则
    /// </summary>
    private async Task CheckSingleRuleAsync(AlarmRule rule, long nowUtcMs, CancellationToken ct)
    {
        // 获取标签最后数据时间
        var lastTs = _lastDataTracker.GetLastTs(rule.DeviceId ?? "", rule.TagId);

        // 如果 DeviceId 为空，需要检查所有匹配 TagId 的标签
        if (string.IsNullOrEmpty(rule.DeviceId))
        {
            // 从内存中查找所有匹配的标签
            var allLastTs = _lastDataTracker.GetAllLastTs();
            var matchingEntries = allLastTs
                .Where(kvp => kvp.Key.EndsWith($"|{rule.TagId}"))
                .ToList();

            foreach (var entry in matchingEntries)
            {
                var parts = entry.Key.Split('|');
                if (parts.Length == 2)
                {
                    var deviceId = parts[0];
                    await CheckTagOfflineAsync(rule, deviceId, entry.Value, nowUtcMs, ct);
                }
            }
        }
        else
        {
            if (lastTs.HasValue)
            {
                await CheckTagOfflineAsync(rule, rule.DeviceId, lastTs.Value, nowUtcMs, ct);
            }
            else
            {
                // 从未收到数据，视为离线
                await CreateOfflineAlarmIfNeededAsync(rule, rule.DeviceId, 0, nowUtcMs, ct);
            }
        }
    }

    /// <summary>
    /// 检查标签是否离线
    /// </summary>
    private async Task CheckTagOfflineAsync(AlarmRule rule, string deviceId, long lastTs, long nowUtcMs, CancellationToken ct)
    {
        // 计算超时阈值（秒转毫秒）
        var timeoutMs = (long)(rule.Threshold * 1000);
        var elapsedMs = nowUtcMs - lastTs;

        if (elapsedMs > timeoutMs)
        {
            await CreateOfflineAlarmIfNeededAsync(rule, deviceId, lastTs, nowUtcMs, ct);
        }
    }

    /// <summary>
    /// 如果需要，创建离线告警
    /// </summary>
    private async Task CreateOfflineAlarmIfNeededAsync(AlarmRule rule, string deviceId, long lastTs, long nowUtcMs, CancellationToken ct)
    {
        var ruleKey = $"{rule.RuleId}|{deviceId}";

        // 防抖检查
        if (_lastAlarmCreatedUtcMs.TryGetValue(ruleKey, out var lastCreatedMs))
        {
            if (nowUtcMs - lastCreatedMs < AlarmDebounceMs)
            {
                return; // 防抖期内，跳过
            }
        }

        // 检查是否存在未关闭的告警
        var code = $"RULE:{rule.RuleId}";
        var hasUnclosed = await HasUnclosedAlarmForCodeAsync(code, ct);
        if (hasUnclosed)
        {
            _lastAlarmCreatedUtcMs[ruleKey] = nowUtcMs;
            return;
        }

        // 创建离线告警
        var alarmId = $"auto-{rule.RuleId}-{nowUtcMs}";
        var message = BuildMessage(rule, deviceId, lastTs, nowUtcMs);

        var alarm = new AlarmRecord
        {
            AlarmId = alarmId,
            DeviceId = deviceId,
            TagId = rule.TagId,
            Ts = nowUtcMs,
            Severity = rule.Severity,
            Code = code,
            Message = message,
            Status = AlarmStatus.Open,
            CreatedUtc = nowUtcMs,
            UpdatedUtc = nowUtcMs
        };

        await _alarmRepo.CreateAsync(alarm, ct);
        _lastAlarmCreatedUtcMs[ruleKey] = nowUtcMs;

        _logger.LogWarning(
            "[Offline] Device={DeviceId}, Tag={TagId}, Rule={RuleId}, LastSeen={LastSeen}",
            deviceId, rule.TagId, rule.RuleId,
            lastTs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(lastTs).ToString("u") : "never");

        // v59: 聚合告警
        if (_aggregationService != null)
        {
            try
            {
                var group = await _aggregationService.AggregateAlarmAsync(alarm, ct);
                _logger.LogDebug("Offline alarm {AlarmId} aggregated to group {GroupId}", alarmId, group.GroupId);
            }
            catch (Exception aggEx)
            {
                _logger.LogWarning(aggEx, "Failed to aggregate offline alarm {AlarmId}", alarmId);
            }
        }
    }

    /// <summary>
    /// 检查是否存在未关闭的告警
    /// </summary>
    private async Task<bool> HasUnclosedAlarmForCodeAsync(string code, CancellationToken ct)
    {
        return await _alarmRepo.HasUnclosedByCodeAsync(code, ct);
    }

    /// <summary>
    /// 构建告警消息
    /// </summary>
    private static string BuildMessage(AlarmRule rule, string deviceId, long lastTs, long nowUtcMs)
    {
        var template = string.IsNullOrWhiteSpace(rule.MessageTemplate)
            ? "[Offline] Device={deviceId}, Tag={tagId}, no data for {timeout}s"
            : rule.MessageTemplate;

        var elapsedSeconds = lastTs > 0 ? (nowUtcMs - lastTs) / 1000.0 : -1;
        var lastSeenStr = lastTs > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(lastTs).ToString("yyyy-MM-dd HH:mm:ss")
            : "never";

        return template
            .Replace("{ruleId}", rule.RuleId, StringComparison.OrdinalIgnoreCase)
            .Replace("{ruleName}", rule.Name, StringComparison.OrdinalIgnoreCase)
            .Replace("{deviceId}", deviceId, StringComparison.OrdinalIgnoreCase)
            .Replace("{tagId}", rule.TagId, StringComparison.OrdinalIgnoreCase)
            .Replace("{timeout}", rule.Threshold.ToString("F0"), StringComparison.OrdinalIgnoreCase)
            .Replace("{elapsed}", elapsedSeconds.ToString("F1"), StringComparison.OrdinalIgnoreCase)
            .Replace("{lastSeen}", lastSeenStr, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// P0: 清理过期的缓存条目，防止内存泄漏
    /// </summary>
    private void CleanupExpiredCache()
    {
        var nowUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var expiredKeys = new List<string>();

        foreach (var kvp in _lastAlarmCreatedUtcMs)
        {
            if (nowUtcMs - kvp.Value > CacheExpiryMs)
            {
                expiredKeys.Add(kvp.Key);
            }
        }

        foreach (var key in expiredKeys)
        {
            _lastAlarmCreatedUtcMs.TryRemove(key, out _);
        }

        if (expiredKeys.Count > 0)
        {
            _logger.LogDebug("OfflineDetector cleaned up {Count} expired cache entries", expiredKeys.Count);
        }
    }
}
