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
/// v56.1: 实现 IAlarmEvaluator 接口，支持测试和解耦
/// P1: 使用 SystemConstants 消除魔法数字
/// </summary>
public sealed class AlarmEvaluatorService : BackgroundService, IAlarmEvaluator
{
    private readonly ChannelReader<TelemetryPoint> _reader;
    private readonly IAlarmRuleRepository _ruleRepo;
    private readonly IAlarmRepository _alarmRepo;
    private readonly ILogger<AlarmEvaluatorService> _logger;
    private readonly AlarmAggregationService? _aggregationService;

    private readonly object _rulesLock = new();
    private List<AlarmRule> _rules = new();

    private DateTimeOffset _lastRefreshUtc = DateTimeOffset.MinValue;

    // 持续时间计时：Key = ruleId
    private readonly ConcurrentDictionary<string, long> _conditionStartUtcMs = new();

    // v56.1: 告警防抖缓存 - 记录最后创建告警的时间，避免高频查询数据库
    private readonly ConcurrentDictionary<string, long> _lastAlarmCreatedUtcMs = new();
    private const int AlarmDebounceMs = 60_000;  // 同一规则 60 秒内不重复查询

    // P0: 缓存清理配置（防止内存泄漏）
    private const long CacheExpiryMs = 24 * 60 * 60 * 1000;  // 24小时过期
    private DateTimeOffset _lastCleanupUtc = DateTimeOffset.MinValue;
    private const int CleanupIntervalMs = 300_000;  // 5分钟清理一次

    /// <summary>
    /// v56.1: IAlarmEvaluator.RuleCount 实现
    /// </summary>
    public int RuleCount
    {
        get
        {
            lock (_rulesLock)
            {
                return _rules.Count;
            }
        }
    }

    public AlarmEvaluatorService(
        ChannelReader<TelemetryPoint> reader,
        IAlarmRuleRepository ruleRepo,
        IAlarmRepository alarmRepo,
        ILogger<AlarmEvaluatorService> logger,
        AlarmAggregationService? aggregationService = null)
    {
        _reader = reader;
        _ruleRepo = ruleRepo;
        _alarmRepo = alarmRepo;
        _logger = logger;
        _aggregationService = aggregationService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AlarmEvaluatorService starting...");

        await RefreshRulesAsync(stoppingToken);

        try
        {
            await foreach (var point in _reader.ReadAllAsync(stoppingToken))
            {
                var now = DateTimeOffset.UtcNow;
                if ((now - _lastRefreshUtc).TotalMilliseconds >= SystemConstants.Alarm.RuleCacheRefreshMs)
                {
                    await RefreshRulesAsync(stoppingToken);
                }

                // P0: 定期清理过期缓存
                if ((now - _lastCleanupUtc).TotalMilliseconds >= CleanupIntervalMs)
                {
                    CleanupExpiredCache();
                    _lastCleanupUtc = now;
                }

                await EvaluatePointAsync(point, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("AlarmEvaluatorService stopping...");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AlarmEvaluatorService crashed");
            throw;
        }
    }

    /// <summary>
    /// v56.1: IAlarmEvaluator.RefreshRulesAsync 实现
    /// v56/v58: 只保留阈值规则，离线、变化率、波动规则由专门服务处理
    /// </summary>
    public async Task RefreshRulesAsync(CancellationToken ct)
    {
        try
        {
            var enabled = await _ruleRepo.ListEnabledAsync(ct);

            // v56/v58: 过滤规则 - 只保留阈值类型
            // 离线检测由 OfflineDetectorService 处理
            // 变化率告警由 RocEvaluatorService 处理
            // v58: 波动告警由 VolatilityEvaluatorService 处理
            var thresholdRules = enabled
                .Where(r => !r.ConditionType.Equals("offline", StringComparison.OrdinalIgnoreCase))
                .Where(r => !r.ConditionType.StartsWith("roc_", StringComparison.OrdinalIgnoreCase))
                .Where(r => !r.ConditionType.Equals("volatility", StringComparison.OrdinalIgnoreCase))
                .ToList();

            lock (_rulesLock)
            {
                _rules = thresholdRules;
            }

            _lastRefreshUtc = DateTimeOffset.UtcNow;
            _logger.LogDebug("Threshold alarm rules refreshed: {Count} (filtered from {Total})",
                thresholdRules.Count, enabled.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh alarm rules");
        }
    }

    /// <summary>
    /// v56.1: IAlarmEvaluator.EvaluateAsync 实现 - 评估单个数据点
    /// </summary>
    public async Task<AlarmRecord?> EvaluateAsync(TelemetryPoint point, CancellationToken ct)
    {
        List<AlarmRule> rules;
        lock (_rulesLock)
        {
            rules = _rules;
        }

        if (rules.Count == 0)
            return null;

        // 匹配：tagId 必须一致；deviceId 若规则指定则必须一致
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];

            if (!string.Equals(rule.TagId, point.TagId, StringComparison.Ordinal))
                continue;

            if (!string.IsNullOrWhiteSpace(rule.DeviceId) &&
                !string.Equals(rule.DeviceId, point.DeviceId, StringComparison.Ordinal))
                continue;

            var alarm = await EvaluateRuleAsync(rule, point, ct);
            if (alarm != null)
                return alarm;  // 返回第一个触发的告警
        }

        return null;
    }

    private async Task EvaluatePointAsync(TelemetryPoint point, CancellationToken ct)
    {
        // 复用公共方法
        await EvaluateAsync(point, ct);
    }

    private async Task<AlarmRecord?> EvaluateRuleAsync(AlarmRule rule, TelemetryPoint point, CancellationToken ct)
    {
        if (!TryGetNumericValue(point, out var value))
        {
            // 非数值点位不参与阈值规则
            return null;
        }

        var met = EvaluateCondition(rule.ConditionType, value, rule.Threshold);

        if (!met)
        {
            _conditionStartUtcMs.TryRemove(rule.RuleId, out _);
            return null;
        }

        // 条件满足：处理 duration
        var nowUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (rule.DurationMs > 0)
        {
            if (!_conditionStartUtcMs.TryGetValue(rule.RuleId, out var startMs))
            {
                _conditionStartUtcMs[rule.RuleId] = nowUtcMs;
                return null;
            }

            if (nowUtcMs - startMs < rule.DurationMs)
                return null;
        }

        // v56.1: 防抖检查 - 如果最近创建过告警，跳过数据库查询
        var nowUtcMs2 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (_lastAlarmCreatedUtcMs.TryGetValue(rule.RuleId, out var lastCreatedMs) &&
            nowUtcMs2 - lastCreatedMs < AlarmDebounceMs)
        {
            return null;  // 防抖期内，跳过
        }

        // 去重：同一 rule 在存在未关闭告警时不再创建
        var hasOpen = await HasUnclosedAlarmForRuleAsync(rule.RuleId, ct);
        if (hasOpen)
        {
            // 标记防抖，避免后续频繁查询数据库
            _lastAlarmCreatedUtcMs[rule.RuleId] = nowUtcMs2;
            return null;
        }

        var alarm = await CreateAlarmAsync(rule, point, value, ct);

        // 记录创建时间，用于防抖
        _lastAlarmCreatedUtcMs[rule.RuleId] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 创建成功后，清理 duration 计时，避免连续点重复触发（去重会挡住，但清理更干净）
        _conditionStartUtcMs.TryRemove(rule.RuleId, out _);

        return alarm;
    }

    private async Task<bool> HasUnclosedAlarmForRuleAsync(string ruleId, CancellationToken ct)
    {
        var code = $"RULE:{ruleId}";
        return await _alarmRepo.HasUnclosedByCodeAsync(code, ct);
    }

    private async Task<AlarmRecord?> CreateAlarmAsync(AlarmRule rule, TelemetryPoint point, double value, CancellationToken ct)
    {
        var nowUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var alarmId = $"auto-{rule.RuleId}-{nowUtcMs}";
        var code = $"RULE:{rule.RuleId}";

        var msg = BuildMessage(rule, point, value);

        var alarm = new AlarmRecord
        {
            AlarmId = alarmId,
            DeviceId = point.DeviceId,
            TagId = point.TagId,
            Ts = point.Ts,
            Severity = rule.Severity,
            Code = code,
            Message = msg,
            Status = AlarmStatus.Open,
            CreatedUtc = nowUtcMs,
            UpdatedUtc = nowUtcMs
        };

        try
        {
            await _alarmRepo.CreateAsync(alarm, ct);
            _logger.LogWarning("Alarm triggered. RuleId={RuleId}, AlarmId={AlarmId}, Msg={Msg}", rule.RuleId, alarmId, msg);

            // v59: 聚合告警
            if (_aggregationService != null)
            {
                try
                {
                    var group = await _aggregationService.AggregateAlarmAsync(alarm, ct);
                    _logger.LogDebug("Alarm {AlarmId} aggregated to group {GroupId}", alarmId, group.GroupId);
                }
                catch (Exception aggEx)
                {
                    _logger.LogWarning(aggEx, "Failed to aggregate alarm {AlarmId}", alarmId);
                }
            }

            return alarm;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create alarm for rule {RuleId}", rule.RuleId);
            return null;
        }
    }

    private static string BuildMessage(AlarmRule rule, TelemetryPoint point, double value)
    {
        var template = string.IsNullOrWhiteSpace(rule.MessageTemplate)
            ? "[{ruleName}] {tagId} {cond} {threshold}, value={value}"
            : rule.MessageTemplate;

        return template
            .Replace("{ruleId}", rule.RuleId, StringComparison.OrdinalIgnoreCase)
            .Replace("{ruleName}", rule.Name, StringComparison.OrdinalIgnoreCase)
            .Replace("{deviceId}", point.DeviceId, StringComparison.OrdinalIgnoreCase)
            .Replace("{tagId}", point.TagId, StringComparison.OrdinalIgnoreCase)
            .Replace("{cond}", rule.ConditionType, StringComparison.OrdinalIgnoreCase)
            .Replace("{threshold}", rule.Threshold.ToString("G", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{value}", value.ToString("G", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
    }

    private static bool EvaluateCondition(string conditionType, double value, double threshold)
    {
        switch (conditionType.Trim().ToLowerInvariant())
        {
            case "gt": return value > threshold;
            case "gte": return value >= threshold;
            case "lt": return value < threshold;
            case "lte": return value <= threshold;
            case "eq": return Math.Abs(value - threshold) < 1e-9;
            case "ne": return Math.Abs(value - threshold) >= 1e-9;
            default: return false;
        }
    }

    // TelemetryPoint 使用类型化字段，根据 ValueType 获取对应的值
    private static bool TryGetNumericValue(TelemetryPoint point, out double value)
    {
        value = 0;

        switch (point.ValueType)
        {
            case TagValueType.Bool:
                if (point.BoolValue.HasValue)
                {
                    value = point.BoolValue.Value ? 1 : 0;
                    return true;
                }
                return false;

            case TagValueType.Int8:
                if (point.Int8Value.HasValue)
                {
                    value = point.Int8Value.Value;
                    return true;
                }
                return false;

            case TagValueType.UInt8:
                if (point.UInt8Value.HasValue)
                {
                    value = point.UInt8Value.Value;
                    return true;
                }
                return false;

            case TagValueType.Int16:
                if (point.Int16Value.HasValue)
                {
                    value = point.Int16Value.Value;
                    return true;
                }
                return false;

            case TagValueType.UInt16:
                if (point.UInt16Value.HasValue)
                {
                    value = point.UInt16Value.Value;
                    return true;
                }
                return false;

            case TagValueType.Int32:
                if (point.Int32Value.HasValue)
                {
                    value = point.Int32Value.Value;
                    return true;
                }
                return false;

            case TagValueType.UInt32:
                if (point.UInt32Value.HasValue)
                {
                    value = point.UInt32Value.Value;
                    return true;
                }
                return false;

            case TagValueType.Int64:
                if (point.Int64Value.HasValue)
                {
                    value = point.Int64Value.Value;
                    return true;
                }
                return false;

            case TagValueType.UInt64:
                if (point.UInt64Value.HasValue)
                {
                    value = point.UInt64Value.Value;
                    return true;
                }
                return false;

            case TagValueType.Float32:
                if (point.Float32Value.HasValue)
                {
                    value = point.Float32Value.Value;
                    return true;
                }
                return false;

            case TagValueType.Float64:
                if (point.Float64Value.HasValue)
                {
                    value = point.Float64Value.Value;
                    return true;
                }
                return false;

            case TagValueType.String:
                if (!string.IsNullOrWhiteSpace(point.StringValue))
                {
                    return double.TryParse(point.StringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                           || double.TryParse(point.StringValue, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
                }
                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// P0: 清理过期的缓存条目，防止内存泄漏
    /// </summary>
    private void CleanupExpiredCache()
    {
        var nowUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var expiredKeys = new List<string>();

        // 清理防抖缓存
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

        // 清理持续时间计时缓存
        expiredKeys.Clear();
        foreach (var kvp in _conditionStartUtcMs)
        {
            if (nowUtcMs - kvp.Value > CacheExpiryMs)
            {
                expiredKeys.Add(kvp.Key);
            }
        }

        foreach (var key in expiredKeys)
        {
            _conditionStartUtcMs.TryRemove(key, out _);
        }

        if (expiredKeys.Count > 0)
        {
            _logger.LogDebug("Cleaned up {Count} expired cache entries", expiredKeys.Count);
        }
    }
}
