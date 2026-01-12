using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IntelliMaint.Application.Services;

/// <summary>
/// v63: 趋势预测服务
/// 使用线性回归和指数平滑进行趋势预测
/// </summary>
public sealed class TrendPredictionService : ITrendPredictionService
{
    private readonly ITelemetryRepository _telemetryRepo;
    private readonly IAlarmRuleRepository _alarmRuleRepo;
    private readonly IDeviceRepository _deviceRepo;
    private readonly HealthAssessmentOptions _options;
    private readonly ILogger<TrendPredictionService> _logger;

    public TrendPredictionService(
        ITelemetryRepository telemetryRepo,
        IAlarmRuleRepository alarmRuleRepo,
        IDeviceRepository deviceRepo,
        IOptions<HealthAssessmentOptions> options,
        ILogger<TrendPredictionService> logger)
    {
        _telemetryRepo = telemetryRepo;
        _alarmRuleRepo = alarmRuleRepo;
        _deviceRepo = deviceRepo;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// 预测单个设备的趋势
    /// </summary>
    public async Task<DeviceTrendSummary?> PredictDeviceTrendAsync(
        string deviceId,
        CancellationToken ct = default)
    {
        var config = _options.TrendPrediction;
        if (!config.Enabled)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var startTs = now - config.HistoryWindowHours * 3600 * 1000L;

        // 获取历史数据
        var data = await _telemetryRepo.QuerySimpleAsync(
            deviceId, null, startTs, now, 50000, ct);

        if (data.Count < config.MinDataPoints)
        {
            _logger.LogDebug("Not enough data for trend prediction: {Count} < {Min}",
                data.Count, config.MinDataPoints);
            return null;
        }

        // 获取告警规则用于判断阈值
        var alarmRules = await _alarmRuleRepo.ListEnabledAsync(ct);

        // 按标签分组
        var tagGroups = data.GroupBy(p => p.TagId).ToList();
        var predictions = new List<TrendPrediction>();

        foreach (var group in tagGroups)
        {
            var tagData = group.OrderBy(p => p.Ts).ToList();
            if (tagData.Count < 10) continue;

            var prediction = PredictTagTrend(
                deviceId, group.Key, tagData, alarmRules, config);

            if (prediction != null)
            {
                predictions.Add(prediction);
            }
        }

        var maxAlert = predictions.Count > 0
            ? predictions.Max(p => p.AlertLevel)
            : PredictionAlertLevel.None;

        var riskTags = predictions.Where(p => p.AlertLevel > PredictionAlertLevel.None).ToList();

        return new DeviceTrendSummary
        {
            DeviceId = deviceId,
            Timestamp = now,
            TagPredictions = predictions,
            MaxAlertLevel = maxAlert,
            RiskTagCount = riskTags.Count,
            RiskSummary = GenerateRiskSummary(riskTags)
        };
    }

    /// <summary>
    /// 预测所有设备的趋势
    /// </summary>
    public async Task<IReadOnlyList<DeviceTrendSummary>> PredictAllDevicesTrendAsync(
        CancellationToken ct = default)
    {
        var devices = await _deviceRepo.ListAsync(ct);
        var results = new List<DeviceTrendSummary>();

        foreach (var device in devices.Where(d => d.Enabled))
        {
            try
            {
                var summary = await PredictDeviceTrendAsync(device.DeviceId, ct);
                if (summary != null)
                {
                    results.Add(summary);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to predict trend for device {DeviceId}",
                    device.DeviceId);
            }
        }

        return results;
    }

    /// <summary>
    /// 预测单个标签的趋势
    /// </summary>
    private TrendPrediction? PredictTagTrend(
        string deviceId,
        string tagId,
        List<TelemetryPoint> data,
        IReadOnlyList<AlarmRule> alarmRules,
        TrendPredictionConfig config)
    {
        // 提取数值
        var values = ExtractValues(data);
        if (values.Count < 10) return null;

        // 应用指数平滑
        var smoothedValues = ExponentialSmoothing(values, config.SmoothingAlpha);

        // 线性回归
        var (slope, intercept, rSquared) = LinearRegression(smoothedValues);

        // 计算趋势方向
        int direction = 0;
        if (Math.Abs(slope) > config.TrendSignificanceThreshold)
        {
            direction = slope > 0 ? 1 : -1;
        }

        // 当前值和预测值
        double currentValue = smoothedValues[^1];
        double predictedValue = intercept + slope * (smoothedValues.Count + config.PredictionHorizonHours);

        // 计算置信度（基于 R²）
        double confidence = Math.Max(0, Math.Min(1, rSquared));

        // 查找相关告警规则
        var relatedRule = FindRelatedAlarmRule(tagId, alarmRules, currentValue, slope);

        // 计算到达告警阈值的时间
        double? hoursToThreshold = null;
        PredictionAlertLevel alertLevel = PredictionAlertLevel.None;
        string? alertMessage = null;

        if (relatedRule != null && Math.Abs(slope) > 0.0001)
        {
            hoursToThreshold = CalculateHoursToThreshold(
                currentValue, slope, relatedRule, out var thresholdType);

            if (hoursToThreshold.HasValue && hoursToThreshold.Value > 0)
            {
                alertLevel = DetermineAlertLevel(hoursToThreshold.Value, confidence, config);
                alertMessage = GenerateAlertMessage(
                    tagId, currentValue, predictedValue, hoursToThreshold.Value,
                    thresholdType, relatedRule.Severity);
            }
        }

        // 只有置信度足够才返回预测
        if (confidence < config.ConfidenceThreshold && alertLevel == PredictionAlertLevel.None)
        {
            return null;
        }

        return new TrendPrediction
        {
            DeviceId = deviceId,
            TagId = tagId,
            PredictionTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            CurrentValue = currentValue,
            TrendSlope = slope,
            TrendDirection = direction,
            PredictedValue = predictedValue,
            Confidence = confidence,
            HoursToAlarmThreshold = hoursToThreshold,
            RelatedAlarmRuleId = relatedRule != null ? int.TryParse(relatedRule.RuleId, out var rId) ? rId : 0 : null,
            AlertLevel = alertLevel,
            AlertMessage = alertMessage
        };
    }

    /// <summary>
    /// 提取数值列表
    /// </summary>
    private List<double> ExtractValues(List<TelemetryPoint> data)
    {
        var values = new List<double>();
        foreach (var point in data)
        {
            var value = ExtractNumericValue(point);
            if (value.HasValue)
            {
                values.Add(value.Value);
            }
        }
        return values;
    }

    /// <summary>
    /// 指数平滑
    /// </summary>
    private List<double> ExponentialSmoothing(List<double> values, double alpha)
    {
        if (values.Count == 0) return values;

        var result = new List<double>(values.Count) { values[0] };

        for (int i = 1; i < values.Count; i++)
        {
            double smoothed = alpha * values[i] + (1 - alpha) * result[i - 1];
            result.Add(smoothed);
        }

        return result;
    }

    /// <summary>
    /// 线性回归 (返回 slope, intercept, R²)
    /// </summary>
    private (double slope, double intercept, double rSquared) LinearRegression(List<double> values)
    {
        int n = values.Count;
        if (n < 2) return (0, values.FirstOrDefault(), 0);

        double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0, sumY2 = 0;

        for (int i = 0; i < n; i++)
        {
            double x = i;
            double y = values[i];
            sumX += x;
            sumY += y;
            sumXY += x * y;
            sumX2 += x * x;
            sumY2 += y * y;
        }

        double meanX = sumX / n;
        double meanY = sumY / n;

        double denominator = sumX2 - n * meanX * meanX;
        if (Math.Abs(denominator) < 1e-10)
        {
            return (0, meanY, 0);
        }

        double slope = (sumXY - n * meanX * meanY) / denominator;
        double intercept = meanY - slope * meanX;

        // 计算 R²
        double ssTot = sumY2 - n * meanY * meanY;
        double ssRes = 0;
        for (int i = 0; i < n; i++)
        {
            double predicted = intercept + slope * i;
            ssRes += (values[i] - predicted) * (values[i] - predicted);
        }

        double rSquared = ssTot > 0 ? 1 - ssRes / ssTot : 0;
        rSquared = Math.Max(0, Math.Min(1, rSquared));

        return (slope, intercept, rSquared);
    }

    /// <summary>
    /// 查找相关告警规则
    /// </summary>
    private AlarmRule? FindRelatedAlarmRule(
        string tagId,
        IReadOnlyList<AlarmRule> rules,
        double currentValue,
        double slope)
    {
        // 查找匹配的启用规则
        var matchingRules = rules
            .Where(r => r.Enabled && MatchesPattern(tagId, r.TagId))
            .OrderByDescending(r => r.Severity)
            .ToList();

        if (matchingRules.Count == 0) return null;

        // 根据趋势方向选择最相关的规则
        if (slope > 0)
        {
            // 上升趋势，关注上限阈值
            return matchingRules.FirstOrDefault(r =>
                r.ConditionType == "gt" || r.ConditionType == "gte");
        }
        else
        {
            // 下降趋势，关注下限阈值
            return matchingRules.FirstOrDefault(r =>
                r.ConditionType == "lt" || r.ConditionType == "lte");
        }
    }

    /// <summary>
    /// 计算到达阈值的小时数
    /// </summary>
    private double? CalculateHoursToThreshold(
        double currentValue,
        double slope,
        AlarmRule rule,
        out string thresholdType)
    {
        thresholdType = rule.ConditionType;

        if (Math.Abs(slope) < 1e-10)
        {
            return null;
        }

        double threshold = rule.Threshold;
        double hoursToThreshold = (threshold - currentValue) / slope;

        // 只返回正值（未来时间）
        if (hoursToThreshold > 0 && hoursToThreshold < 720) // 最多预测30天
        {
            return hoursToThreshold;
        }

        return null;
    }

    /// <summary>
    /// 确定预警级别
    /// </summary>
    private PredictionAlertLevel DetermineAlertLevel(
        double hoursToThreshold,
        double confidence,
        TrendPredictionConfig config)
    {
        // 置信度低时降级
        double effectiveConfidence = confidence >= config.ConfidenceThreshold ? 1.0 : 0.5;

        if (hoursToThreshold <= 24 * effectiveConfidence)
            return PredictionAlertLevel.Critical;
        if (hoursToThreshold <= 48 * effectiveConfidence)
            return PredictionAlertLevel.High;
        if (hoursToThreshold <= 72 * effectiveConfidence)
            return PredictionAlertLevel.Medium;
        if (hoursToThreshold <= 168 * effectiveConfidence) // 7天
            return PredictionAlertLevel.Low;

        return PredictionAlertLevel.None;
    }

    /// <summary>
    /// 生成预警消息
    /// </summary>
    private string GenerateAlertMessage(
        string tagId,
        double currentValue,
        double predictedValue,
        double hoursToThreshold,
        string thresholdType,
        int severity)
    {
        string timeDesc = hoursToThreshold < 24
            ? $"{hoursToThreshold:F1}小时"
            : $"{hoursToThreshold / 24:F1}天";

        string direction = predictedValue > currentValue ? "上升" : "下降";

        string severityDesc = severity switch
        {
            5 => "紧急",
            4 => "严重",
            3 => "警告",
            2 => "注意",
            _ => "信息"
        };

        return $"{tagId} 预计 {timeDesc} 后{direction}至 {predictedValue:F2}，触发{severityDesc}告警";
    }

    /// <summary>
    /// 生成风险摘要
    /// </summary>
    private string? GenerateRiskSummary(List<TrendPrediction> riskTags)
    {
        if (riskTags.Count == 0) return null;

        var criticalCount = riskTags.Count(t => t.AlertLevel == PredictionAlertLevel.Critical);
        var highCount = riskTags.Count(t => t.AlertLevel == PredictionAlertLevel.High);

        var parts = new List<string>();

        if (criticalCount > 0)
            parts.Add($"{criticalCount}个紧急预警");
        if (highCount > 0)
            parts.Add($"{highCount}个高风险预警");
        if (riskTags.Count - criticalCount - highCount > 0)
            parts.Add($"{riskTags.Count - criticalCount - highCount}个其他预警");

        return string.Join(", ", parts);
    }

    /// <summary>
    /// 模式匹配
    /// </summary>
    private bool MatchesPattern(string tagId, string pattern)
    {
        if (string.IsNullOrEmpty(pattern) || pattern == "*")
            return true;

        if (pattern.Contains('*'))
        {
            var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace("\\*", ".*") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(
                tagId, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        return tagId.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 提取数值
    /// </summary>
    private double? ExtractNumericValue(TelemetryPoint point)
    {
        return point.ValueType switch
        {
            TagValueType.Float32 => point.Float32Value,
            TagValueType.Float64 => point.Float64Value,
            TagValueType.Int8 => point.Int8Value,
            TagValueType.UInt8 => point.UInt8Value,
            TagValueType.Int16 => point.Int16Value,
            TagValueType.UInt16 => point.UInt16Value,
            TagValueType.Int32 => point.Int32Value,
            TagValueType.UInt32 => point.UInt32Value,
            TagValueType.Int64 => point.Int64Value,
            TagValueType.UInt64 => (double?)point.UInt64Value,
            TagValueType.Bool => point.BoolValue.HasValue ? (point.BoolValue.Value ? 1.0 : 0.0) : null,
            _ => null
        };
    }
}

/// <summary>
/// v63: 趋势预测服务接口
/// </summary>
public interface ITrendPredictionService
{
    /// <summary>预测单个设备的趋势</summary>
    Task<DeviceTrendSummary?> PredictDeviceTrendAsync(string deviceId, CancellationToken ct = default);

    /// <summary>预测所有设备的趋势</summary>
    Task<IReadOnlyList<DeviceTrendSummary>> PredictAllDevicesTrendAsync(CancellationToken ct = default);
}
