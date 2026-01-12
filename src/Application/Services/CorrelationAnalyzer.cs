using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Application.Services;

/// <summary>
/// v62: 多标签关联分析服务
/// 检测多个标签同时发生异常的情况，用于更准确的故障诊断
/// </summary>
public sealed class CorrelationAnalyzer : ICorrelationAnalyzer
{
    private readonly ITagCorrelationRepository _repository;
    private readonly ILogger<CorrelationAnalyzer> _logger;

    // 规则缓存
    private volatile IReadOnlyList<TagCorrelationRule> _rules = Array.Empty<TagCorrelationRule>();
    private readonly ConcurrentDictionary<string, Regex> _patternCache = new();

    public CorrelationAnalyzer(
        ITagCorrelationRepository repository,
        ILogger<CorrelationAnalyzer> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public int RuleCount => _rules.Count;

    public async Task RefreshRulesAsync(CancellationToken ct)
    {
        _rules = await _repository.ListEnabledAsync(ct);
        _logger.LogInformation("Correlation analyzer refreshed with {Count} rules", _rules.Count);
    }

    public async Task<IReadOnlyList<CorrelationAnomaly>> AnalyzeAsync(
        string deviceId,
        IReadOnlyList<TelemetryPoint> recentData,
        CancellationToken ct)
    {
        if (_rules.Count == 0 || recentData.Count < 2)
            return Array.Empty<CorrelationAnomaly>();

        // 过滤适用于此设备的规则
        var applicableRules = _rules
            .Where(r => MatchPattern(r.DevicePattern, deviceId))
            .ToList();

        if (applicableRules.Count == 0)
            return Array.Empty<CorrelationAnomaly>();

        // 按标签ID分组数据
        var tagData = recentData
            .GroupBy(p => p.TagId)
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.Ts).ToList());

        var anomalies = new List<CorrelationAnomaly>();

        foreach (var rule in applicableRules)
        {
            // 找到匹配规则的标签对
            var tag1Matches = tagData.Keys.Where(t => MatchPattern(rule.Tag1Pattern, t)).ToList();
            var tag2Matches = tagData.Keys.Where(t => MatchPattern(rule.Tag2Pattern, t)).ToList();

            foreach (var tag1Id in tag1Matches)
            {
                foreach (var tag2Id in tag2Matches)
                {
                    if (tag1Id == tag2Id) continue;

                    var tag1Data = tagData[tag1Id];
                    var tag2Data = tagData[tag2Id];

                    // 需要足够的数据点才能计算相关性
                    if (tag1Data.Count < 3 || tag2Data.Count < 3)
                        continue;

                    var anomaly = await AnalyzeTagPairAsync(rule, tag1Id, tag2Id, tag1Data, tag2Data, ct);
                    if (anomaly != null)
                    {
                        anomalies.Add(anomaly);
                    }
                }
            }
        }

        return anomalies;
    }

    private Task<CorrelationAnomaly?> AnalyzeTagPairAsync(
        TagCorrelationRule rule,
        string tag1Id,
        string tag2Id,
        List<TelemetryPoint> tag1Data,
        List<TelemetryPoint> tag2Data,
        CancellationToken ct)
    {
        // 提取数值序列
        var values1 = ExtractNumericValues(tag1Data);
        var values2 = ExtractNumericValues(tag2Data);

        if (values1.Count < 3 || values2.Count < 3)
            return Task.FromResult<CorrelationAnomaly?>(null);

        // 对齐时间序列（简单插值）
        var (aligned1, aligned2) = AlignTimeSeries(tag1Data, tag2Data);
        if (aligned1.Count < 3)
            return Task.FromResult<CorrelationAnomaly?>(null);

        // 根据关联类型分析
        var isAnomaly = rule.CorrelationType switch
        {
            CorrelationType.SameDirection => AnalyzeSameDirection(aligned1, aligned2, rule.Threshold),
            CorrelationType.OppositeDirection => AnalyzeOppositeDirection(aligned1, aligned2, rule.Threshold),
            CorrelationType.ThresholdCombination => AnalyzeThresholdCombination(values1, values2, rule.Threshold),
            _ => false
        };

        if (!isAnomaly)
            return Task.FromResult<CorrelationAnomaly?>(null);

        // 计算相关系数
        var correlation = CalculatePearsonCorrelation(aligned1, aligned2);

        var anomaly = new CorrelationAnomaly
        {
            RuleId = rule.Id,
            RuleName = rule.Name,
            Tag1Id = tag1Id,
            Tag2Id = tag2Id,
            CorrelationValue = correlation,
            RiskDescription = rule.RiskDescription,
            PenaltyScore = rule.PenaltyScore
        };

        _logger.LogDebug("Correlation anomaly detected: {Rule} ({Tag1}-{Tag2}), correlation={Corr:F3}",
            rule.Name, tag1Id, tag2Id, correlation);

        return Task.FromResult<CorrelationAnomaly?>(anomaly);
    }

    /// <summary>
    /// 分析同向变化（如温度升高+电流升高）
    /// </summary>
    private bool AnalyzeSameDirection(List<double> values1, List<double> values2, double threshold)
    {
        if (values1.Count < 3 || values2.Count < 3)
            return false;

        // 计算变化率
        var trend1 = CalculateTrend(values1);
        var trend2 = CalculateTrend(values2);

        // 两个都在上升且超过阈值
        if (trend1 > threshold && trend2 > threshold)
            return true;

        // 两个都在下降且超过阈值（绝对值）
        if (trend1 < -threshold && trend2 < -threshold)
            return true;

        return false;
    }

    /// <summary>
    /// 分析反向变化（如压力升高+流量降低）
    /// </summary>
    private bool AnalyzeOppositeDirection(List<double> values1, List<double> values2, double threshold)
    {
        if (values1.Count < 3 || values2.Count < 3)
            return false;

        var trend1 = CalculateTrend(values1);
        var trend2 = CalculateTrend(values2);

        // 一个上升一个下降，且变化幅度都超过阈值
        if ((trend1 > threshold && trend2 < -threshold) ||
            (trend1 < -threshold && trend2 > threshold))
            return true;

        return false;
    }

    /// <summary>
    /// 分析阈值组合（如温度>80且振动>5）
    /// </summary>
    private bool AnalyzeThresholdCombination(List<double> values1, List<double> values2, double threshold)
    {
        if (values1.Count == 0 || values2.Count == 0)
            return false;

        // 计算最近值的 Z-Score
        var mean1 = values1.Average();
        var mean2 = values2.Average();
        var std1 = CalculateStdDev(values1);
        var std2 = CalculateStdDev(values2);

        if (std1 < 0.001 || std2 < 0.001)
            return false;

        var latest1 = values1[^1];
        var latest2 = values2[^1];

        var zScore1 = Math.Abs((latest1 - mean1) / std1);
        var zScore2 = Math.Abs((latest2 - mean2) / std2);

        // 两个标签都超出阈值（以 Z-Score 衡量）
        return zScore1 > threshold && zScore2 > threshold;
    }

    /// <summary>
    /// 计算趋势（归一化的变化率）
    /// </summary>
    private double CalculateTrend(List<double> values)
    {
        if (values.Count < 2) return 0;

        var mean = values.Average();
        if (Math.Abs(mean) < 0.001) mean = 1;

        // 简单线性回归斜率
        var n = values.Count;
        var sumX = 0.0;
        var sumY = 0.0;
        var sumXY = 0.0;
        var sumX2 = 0.0;

        for (int i = 0; i < n; i++)
        {
            sumX += i;
            sumY += values[i];
            sumXY += i * values[i];
            sumX2 += i * i;
        }

        var slope = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);

        // 归一化斜率（相对于均值的变化率）
        return slope / Math.Abs(mean);
    }

    /// <summary>
    /// 计算 Pearson 相关系数
    /// </summary>
    private double CalculatePearsonCorrelation(List<double> x, List<double> y)
    {
        var n = Math.Min(x.Count, y.Count);
        if (n < 2) return 0;

        var meanX = x.Take(n).Average();
        var meanY = y.Take(n).Average();

        var sumXY = 0.0;
        var sumX2 = 0.0;
        var sumY2 = 0.0;

        for (int i = 0; i < n; i++)
        {
            var dx = x[i] - meanX;
            var dy = y[i] - meanY;
            sumXY += dx * dy;
            sumX2 += dx * dx;
            sumY2 += dy * dy;
        }

        var denominator = Math.Sqrt(sumX2 * sumY2);
        if (denominator < 0.0001) return 0;

        return sumXY / denominator;
    }

    private double CalculateStdDev(List<double> values)
    {
        if (values.Count < 2) return 0;
        var mean = values.Average();
        var sumSq = values.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sumSq / values.Count);
    }

    /// <summary>
    /// 提取数值列表
    /// </summary>
    private List<double> ExtractNumericValues(List<TelemetryPoint> data)
    {
        var values = new List<double>(data.Count);
        foreach (var point in data)
        {
            var value = ExtractNumericValue(point);
            if (value.HasValue)
                values.Add(value.Value);
        }
        return values;
    }

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

    /// <summary>
    /// 对齐两个时间序列（简单策略：使用共同时间点）
    /// </summary>
    private (List<double> aligned1, List<double> aligned2) AlignTimeSeries(
        List<TelemetryPoint> data1,
        List<TelemetryPoint> data2)
    {
        var aligned1 = new List<double>();
        var aligned2 = new List<double>();

        // 创建时间戳到值的映射
        var ts1 = data1.ToDictionary(p => p.Ts, p => ExtractNumericValue(p));
        var ts2 = data2.ToDictionary(p => p.Ts, p => ExtractNumericValue(p));

        // 找共同时间点
        var commonTs = ts1.Keys.Intersect(ts2.Keys).OrderBy(t => t).ToList();

        if (commonTs.Count >= 3)
        {
            foreach (var ts in commonTs)
            {
                var v1 = ts1[ts];
                var v2 = ts2[ts];
                if (v1.HasValue && v2.HasValue)
                {
                    aligned1.Add(v1.Value);
                    aligned2.Add(v2.Value);
                }
            }
        }
        else
        {
            // 如果共同时间点不足，使用简单的索引对齐
            var values1 = ExtractNumericValues(data1);
            var values2 = ExtractNumericValues(data2);
            var minLen = Math.Min(values1.Count, values2.Count);

            for (int i = 0; i < minLen; i++)
            {
                aligned1.Add(values1[i]);
                aligned2.Add(values2[i]);
            }
        }

        return (aligned1, aligned2);
    }

    /// <summary>
    /// 通配符模式匹配
    /// </summary>
    private bool MatchPattern(string? pattern, string value)
    {
        if (string.IsNullOrEmpty(pattern) || pattern == "*")
            return true;

        var regex = _patternCache.GetOrAdd(pattern, p =>
        {
            // 将通配符转换为正则表达式
            var escaped = Regex.Escape(p).Replace("\\*", ".*");
            return new Regex($"^{escaped}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        });

        return regex.IsMatch(value);
    }
}
