using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.Pipeline;

/// <summary>
/// v56: 变化率告警评估服务
/// </summary>
public sealed class RocEvaluatorService : BackgroundService
{
    private readonly ChannelReader<TelemetryPoint> _reader;
    private readonly IAlarmRuleRepository _ruleRepo;
    private readonly IAlarmRepository _alarmRepo;
    private readonly RocSlidingWindow _slidingWindow;
    private readonly ILogger<RocEvaluatorService> _logger;
    private readonly AlarmAggregationService? _aggregationService;

    // 规则刷新间隔（毫秒）
    private const int RuleRefreshIntervalMs = 30_000;

    // 告警防抖（毫秒）
    private const int AlarmDebounceMs = 60_000;

    // 窗口清理间隔（毫秒）
    private const int CleanupIntervalMs = 60_000;

    // 规则缓存
    private readonly object _rulesLock = new();
    private List<AlarmRule> _rocRules = new();
    private DateTimeOffset _lastRefreshUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastCleanupUtc = DateTimeOffset.MinValue;

    // 防抖缓存：Key = ruleId|deviceId, Value = 最后创建告警的时间
    private readonly ConcurrentDictionary<string, long> _lastAlarmCreatedUtcMs = new();

    // P0: 缓存过期时间（防止内存泄漏）
    private const long CacheExpiryMs = 24 * 60 * 60 * 1000;  // 24小时过期

    public RocEvaluatorService(
        ChannelReader<TelemetryPoint> reader,
        IAlarmRuleRepository ruleRepo,
        IAlarmRepository alarmRepo,
        RocSlidingWindow slidingWindow,
        ILogger<RocEvaluatorService> logger,
        AlarmAggregationService? aggregationService = null)
    {
        _reader = reader;
        _ruleRepo = ruleRepo;
        _alarmRepo = alarmRepo;
        _slidingWindow = slidingWindow;
        _logger = logger;
        _aggregationService = aggregationService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RocEvaluatorService starting...");

        // 初始加载规则
        await RefreshRulesAsync(stoppingToken);

        try
        {
            await foreach (var point in _reader.ReadAllAsync(stoppingToken))
            {
                var now = DateTimeOffset.UtcNow;

                // 周期性刷新规则
                if ((now - _lastRefreshUtc).TotalMilliseconds >= RuleRefreshIntervalMs)
                {
                    await RefreshRulesAsync(stoppingToken);
                }

                // 周期性清理过期数据
                if ((now - _lastCleanupUtc).TotalMilliseconds >= CleanupIntervalMs)
                {
                    _slidingWindow.CleanupExpired();
                    CleanupExpiredCache();  // P0: 同时清理防抖缓存
                    _lastCleanupUtc = now;
                }

                // 处理数据点
                await ProcessPointAsync(point, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("RocEvaluatorService stopping...");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RocEvaluatorService crashed");
            throw;
        }
    }

    /// <summary>
    /// 刷新变化率规则缓存
    /// </summary>
    private async Task RefreshRulesAsync(CancellationToken ct)
    {
        try
        {
            var allRules = await _ruleRepo.ListEnabledAsync(ct);

            // 只保留变化率规则
            var rocRules = allRules
                .Where(r => r.ConditionType.StartsWith("roc_", StringComparison.OrdinalIgnoreCase))
                .ToList();

            lock (_rulesLock)
            {
                _rocRules = rocRules;
            }

            _lastRefreshUtc = DateTimeOffset.UtcNow;
            _logger.LogDebug("Refreshed RoC rules: {Count} rules loaded", rocRules.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh RoC rules");
        }
    }

    /// <summary>
    /// 处理单个数据点
    /// </summary>
    private async Task ProcessPointAsync(TelemetryPoint point, CancellationToken ct)
    {
        // 提取数值
        if (!TryGetNumericValue(point, out var value))
            return;

        // 添加到滑动窗口
        _slidingWindow.Add(point.DeviceId, point.TagId, point.Ts, value);

        // 获取匹配的规则
        List<AlarmRule> matchingRules;
        lock (_rulesLock)
        {
            matchingRules = _rocRules
                .Where(r => r.TagId == point.TagId)
                .Where(r => string.IsNullOrEmpty(r.DeviceId) || r.DeviceId == point.DeviceId)
                .ToList();
        }

        // 评估每个匹配的规则
        foreach (var rule in matchingRules)
        {
            try
            {
                await EvaluateRuleAsync(rule, point, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error evaluating RoC rule {RuleId}", rule.RuleId);
            }
        }
    }

    /// <summary>
    /// 评估单个变化率规则
    /// </summary>
    private async Task EvaluateRuleAsync(AlarmRule rule, TelemetryPoint point, CancellationToken ct)
    {
        // 获取窗口内的变化率
        var rocResult = _slidingWindow.GetRateOfChange(point.DeviceId, point.TagId, rule.RocWindowMs);
        if (rocResult == null || rocResult.Count < 2)
            return; // 窗口内数据不足

        // 根据规则类型检查变化率
        bool triggered;
        double changeValue;

        if (rule.ConditionType.Equals("roc_percent", StringComparison.OrdinalIgnoreCase))
        {
            changeValue = rocResult.PercentChange;
            triggered = changeValue >= rule.Threshold;
        }
        else if (rule.ConditionType.Equals("roc_absolute", StringComparison.OrdinalIgnoreCase))
        {
            changeValue = rocResult.AbsoluteChange;
            triggered = changeValue >= rule.Threshold;
        }
        else
        {
            return; // 未知的变化率类型
        }

        if (!triggered)
            return;

        // 防抖检查
        var ruleKey = $"{rule.RuleId}|{point.DeviceId}";
        var nowUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (_lastAlarmCreatedUtcMs.TryGetValue(ruleKey, out var lastCreatedMs))
        {
            if (nowUtcMs - lastCreatedMs < AlarmDebounceMs)
                return; // 防抖期内
        }

        // 检查是否存在未关闭的告警
        var code = $"RULE:{rule.RuleId}";
        var hasUnclosed = await HasUnclosedAlarmForCodeAsync(code, ct);
        if (hasUnclosed)
        {
            _lastAlarmCreatedUtcMs[ruleKey] = nowUtcMs;
            return;
        }

        // 创建告警
        await CreateAlarmAsync(rule, point, rocResult, changeValue, nowUtcMs, ct);
        _lastAlarmCreatedUtcMs[ruleKey] = nowUtcMs;
    }

    /// <summary>
    /// 创建变化率告警
    /// </summary>
    private async Task CreateAlarmAsync(
        AlarmRule rule,
        TelemetryPoint point,
        RocResult rocResult,
        double changeValue,
        long nowUtcMs,
        CancellationToken ct)
    {
        var alarmId = $"auto-{rule.RuleId}-{nowUtcMs}";
        var code = $"RULE:{rule.RuleId}";
        var message = BuildMessage(rule, point, rocResult, changeValue);

        var alarm = new AlarmRecord
        {
            AlarmId = alarmId,
            DeviceId = point.DeviceId,
            TagId = point.TagId,
            Ts = nowUtcMs,
            Severity = rule.Severity,
            Code = code,
            Message = message,
            Status = AlarmStatus.Open,
            CreatedUtc = nowUtcMs,
            UpdatedUtc = nowUtcMs
        };

        await _alarmRepo.CreateAsync(alarm, ct);

        _logger.LogWarning(
            "[RoC] Device={DeviceId}, Tag={TagId}, Rule={RuleId}, Change={Change:F2}{Unit}",
            point.DeviceId, point.TagId, rule.RuleId, changeValue,
            rule.ConditionType.Contains("percent") ? "%" : "");

        // v59: 聚合告警
        if (_aggregationService != null)
        {
            try
            {
                var group = await _aggregationService.AggregateAlarmAsync(alarm, ct);
                _logger.LogDebug("RoC alarm {AlarmId} aggregated to group {GroupId}", alarmId, group.GroupId);
            }
            catch (Exception aggEx)
            {
                _logger.LogWarning(aggEx, "Failed to aggregate RoC alarm {AlarmId}", alarmId);
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
    private static string BuildMessage(AlarmRule rule, TelemetryPoint point, RocResult rocResult, double changeValue)
    {
        var isPercent = rule.ConditionType.Contains("percent", StringComparison.OrdinalIgnoreCase);
        var defaultTemplate = isPercent
            ? "[RoC] {tagId} changed {changePercent}% in {windowMs}ms (from {windowStart} to {windowEnd})"
            : "[RoC] {tagId} changed by {changeAbsolute} in {windowMs}ms (from {windowStart} to {windowEnd})";

        var template = string.IsNullOrWhiteSpace(rule.MessageTemplate)
            ? defaultTemplate
            : rule.MessageTemplate;

        return template
            .Replace("{ruleId}", rule.RuleId, StringComparison.OrdinalIgnoreCase)
            .Replace("{ruleName}", rule.Name, StringComparison.OrdinalIgnoreCase)
            .Replace("{deviceId}", point.DeviceId, StringComparison.OrdinalIgnoreCase)
            .Replace("{tagId}", point.TagId, StringComparison.OrdinalIgnoreCase)
            .Replace("{threshold}", rule.Threshold.ToString("G", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{changePercent}", rocResult.PercentChange.ToString("F2", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{changeAbsolute}", rocResult.AbsoluteChange.ToString("G", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{value}", changeValue.ToString("G", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{windowMs}", rule.RocWindowMs.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{windowStart}", rocResult.First.ToString("G", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{windowEnd}", rocResult.Last.ToString("G", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{min}", rocResult.Min.ToString("G", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{max}", rocResult.Max.ToString("G", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 提取数值
    /// </summary>
    private static bool TryGetNumericValue(TelemetryPoint point, out double value)
    {
        value = 0;

        if (point.BoolValue.HasValue)
        {
            value = point.BoolValue.Value ? 1 : 0;
            return true;
        }

        if (point.Int8Value.HasValue) { value = point.Int8Value.Value; return true; }
        if (point.UInt8Value.HasValue) { value = point.UInt8Value.Value; return true; }
        if (point.Int16Value.HasValue) { value = point.Int16Value.Value; return true; }
        if (point.UInt16Value.HasValue) { value = point.UInt16Value.Value; return true; }
        if (point.Int32Value.HasValue) { value = point.Int32Value.Value; return true; }
        if (point.UInt32Value.HasValue) { value = point.UInt32Value.Value; return true; }
        if (point.Int64Value.HasValue) { value = point.Int64Value.Value; return true; }
        if (point.UInt64Value.HasValue) { value = point.UInt64Value.Value; return true; }
        if (point.Float32Value.HasValue) { value = point.Float32Value.Value; return true; }
        if (point.Float64Value.HasValue) { value = point.Float64Value.Value; return true; }

        if (!string.IsNullOrWhiteSpace(point.StringValue))
        {
            if (double.TryParse(point.StringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return true;
        }

        return false;
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
            _logger.LogDebug("RocEvaluator cleaned up {Count} expired cache entries", expiredKeys.Count);
        }
    }
}
