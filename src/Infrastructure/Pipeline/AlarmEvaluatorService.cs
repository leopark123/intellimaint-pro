using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using IntelliMaint.Infrastructure.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.Pipeline;

public sealed class AlarmEvaluatorService : BackgroundService
{
    private readonly ChannelReader<TelemetryPoint> _reader;
    private readonly IAlarmRuleRepository _ruleRepo;
    private readonly IAlarmRepository _alarmRepo;
    private readonly IDbExecutor _db;
    private readonly ILogger<AlarmEvaluatorService> _logger;

    private readonly object _rulesLock = new();
    private List<AlarmRule> _rules = new();

    private DateTimeOffset _lastRefreshUtc = DateTimeOffset.MinValue;
    private const int RuleRefreshIntervalMs = 30_000;

    // 持续时间计时：Key = ruleId
    private readonly ConcurrentDictionary<string, long> _conditionStartUtcMs = new();

    // v56.1: 告警防抖缓存 - 记录最后创建告警的时间，避免高频查询数据库
    private readonly ConcurrentDictionary<string, long> _lastAlarmCreatedUtcMs = new();
    private const int AlarmDebounceMs = 60_000;  // 同一规则 60 秒内不重复查询

    public AlarmEvaluatorService(
        ChannelReader<TelemetryPoint> reader,
        IAlarmRuleRepository ruleRepo,
        IAlarmRepository alarmRepo,
        IDbExecutor db,
        ILogger<AlarmEvaluatorService> logger)
    {
        _reader = reader;
        _ruleRepo = ruleRepo;
        _alarmRepo = alarmRepo;
        _db = db;
        _logger = logger;
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
                if ((now - _lastRefreshUtc).TotalMilliseconds >= RuleRefreshIntervalMs)
                {
                    await RefreshRulesAsync(stoppingToken);
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

    private async Task RefreshRulesAsync(CancellationToken ct)
    {
        try
        {
            var enabled = await _ruleRepo.ListEnabledAsync(ct);
            lock (_rulesLock)
            {
                _rules = new List<AlarmRule>(enabled);
            }

            _lastRefreshUtc = DateTimeOffset.UtcNow;
            _logger.LogDebug("Alarm rules refreshed: {Count}", enabled.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh alarm rules");
        }
    }

    private async Task EvaluatePointAsync(TelemetryPoint point, CancellationToken ct)
    {
        List<AlarmRule> rules;
        lock (_rulesLock)
        {
            rules = _rules;
        }

        if (rules.Count == 0)
            return;

        // 匹配：tagId 必须一致；deviceId 若规则指定则必须一致
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];

            if (!string.Equals(rule.TagId, point.TagId, StringComparison.Ordinal))
                continue;

            if (!string.IsNullOrWhiteSpace(rule.DeviceId) &&
                !string.Equals(rule.DeviceId, point.DeviceId, StringComparison.Ordinal))
                continue;

            await EvaluateRuleAsync(rule, point, ct);
        }
    }

    private async Task EvaluateRuleAsync(AlarmRule rule, TelemetryPoint point, CancellationToken ct)
    {
        if (!TryGetNumericValue(point, out var value))
        {
            // 非数值点位不参与阈值规则
            return;
        }

        var met = EvaluateCondition(rule.ConditionType, value, rule.Threshold);

        if (!met)
        {
            _conditionStartUtcMs.TryRemove(rule.RuleId, out _);
            return;
        }

        // 条件满足：处理 duration
        var nowUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (rule.DurationMs > 0)
        {
            if (!_conditionStartUtcMs.TryGetValue(rule.RuleId, out var startMs))
            {
                _conditionStartUtcMs[rule.RuleId] = nowUtcMs;
                return;
            }

            if (nowUtcMs - startMs < rule.DurationMs)
                return;
        }

        // v56.1: 防抖检查 - 如果最近创建过告警，跳过数据库查询
        var nowUtcMs2 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (_lastAlarmCreatedUtcMs.TryGetValue(rule.RuleId, out var lastCreatedMs) &&
            nowUtcMs2 - lastCreatedMs < AlarmDebounceMs)
        {
            return;  // 防抖期内，跳过
        }

        // 去重：同一 rule 在存在未关闭告警时不再创建
        var hasOpen = await HasUnclosedAlarmForRuleAsync(rule.RuleId, ct);
        if (hasOpen)
        {
            // 标记防抖，避免后续频繁查询数据库
            _lastAlarmCreatedUtcMs[rule.RuleId] = nowUtcMs2;
            return;
        }

        await CreateAlarmAsync(rule, point, value, ct);

        // 记录创建时间，用于防抖
        _lastAlarmCreatedUtcMs[rule.RuleId] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 创建成功后，清理 duration 计时，避免连续点重复触发（去重会挡住，但清理更干净）
        _conditionStartUtcMs.TryRemove(rule.RuleId, out _);
    }

    private async Task<bool> HasUnclosedAlarmForRuleAsync(string ruleId, CancellationToken ct)
    {
        // alarm.status: 0=Open, 1=Acknowledged, 2=Closed
        // 未关闭：status <> 2
        const string sql = @"
SELECT COUNT(*) 
FROM alarm
WHERE code = @Code AND status <> 2;";

        var code = $"RULE:{ruleId}";
        var count = await _db.ExecuteScalarAsync<long>(sql, new { Code = code }, ct);
        return count > 0;
    }

    private async Task CreateAlarmAsync(AlarmRule rule, TelemetryPoint point, double value, CancellationToken ct)
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create alarm for rule {RuleId}", rule.RuleId);
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
}
