using IntelliMaint.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Application.Services;

/// <summary>
/// v45: 健康评分计算器实现
/// 基于设备特征和基线计算健康指数
/// </summary>
public sealed class HealthScoreCalculator : IHealthScoreCalculator
{
    private readonly IAlarmRepository _alarmRepo;
    private readonly ILogger<HealthScoreCalculator> _logger;

    // 权重配置
    private const double DeviationWeight = 0.40;   // 偏差评分权重 40%
    private const double TrendWeight = 0.30;       // 趋势评分权重 30%
    private const double StabilityWeight = 0.20;   // 稳定性评分权重 20%
    private const double AlarmWeight = 0.10;       // 告警评分权重 10%

    public HealthScoreCalculator(
        IAlarmRepository alarmRepo,
        ILogger<HealthScoreCalculator> logger)
    {
        _alarmRepo = alarmRepo;
        _logger = logger;
    }

    /// <inheritdoc />
    public HealthScore Calculate(DeviceFeatures features, DeviceBaseline? baseline)
    {
        var problemTags = new List<string>();
        var diagnosticMessages = new List<string>();

        // 1. 计算偏差评分
        int deviationScore = CalculateDeviationScore(features, baseline, problemTags, diagnosticMessages);

        // 2. 计算趋势评分
        int trendScore = CalculateTrendScore(features, problemTags, diagnosticMessages);

        // 3. 计算稳定性评分
        int stabilityScore = CalculateStabilityScore(features, baseline, problemTags, diagnosticMessages);

        // 4. 计算告警评分（需要异步，这里简化处理）
        int alarmScore = 100; // 默认满分，实际应该查询告警

        // 综合评分
        double weightedScore = 
            deviationScore * DeviationWeight +
            trendScore * TrendWeight +
            stabilityScore * StabilityWeight +
            alarmScore * AlarmWeight;

        int healthIndex = (int)Math.Round(weightedScore);
        healthIndex = Math.Clamp(healthIndex, 0, 100);

        // 确定健康等级
        var level = healthIndex switch
        {
            >= 80 => HealthLevel.Healthy,
            >= 60 => HealthLevel.Attention,
            >= 40 => HealthLevel.Warning,
            _ => HealthLevel.Critical
        };

        // 生成诊断消息
        string? diagnosticMessage = diagnosticMessages.Count > 0 
            ? string.Join("; ", diagnosticMessages.Take(3)) 
            : null;

        _logger.LogDebug(
            "Health score for {DeviceId}: Index={Index}, Level={Level}, " +
            "Deviation={Deviation}, Trend={Trend}, Stability={Stability}, Alarm={Alarm}",
            features.DeviceId, healthIndex, level, 
            deviationScore, trendScore, stabilityScore, alarmScore);

        return new HealthScore
        {
            DeviceId = features.DeviceId,
            Timestamp = features.Timestamp,
            Index = healthIndex,
            Level = level,
            DeviationScore = deviationScore,
            TrendScore = trendScore,
            StabilityScore = stabilityScore,
            AlarmScore = alarmScore,
            HasBaseline = baseline != null,
            ProblemTags = problemTags.Distinct().ToList(),
            DiagnosticMessage = diagnosticMessage
        };
    }

    /// <summary>
    /// 计算偏差评分
    /// 衡量当前值与基线的偏离程度
    /// </summary>
    private int CalculateDeviationScore(
        DeviceFeatures features,
        DeviceBaseline? baseline,
        List<string> problemTags,
        List<string> messages)
    {
        if (baseline == null || baseline.TagBaselines.Count == 0)
        {
            // 无基线时，返回默认评分
            return 80;
        }

        var scores = new List<double>();

        foreach (var (tagId, tagFeatures) in features.TagFeatures)
        {
            if (!baseline.TagBaselines.TryGetValue(tagId, out var tagBaseline))
            {
                continue; // 该标签无基线
            }

            // 计算 Z-Score（当前均值与基线均值的标准差距离）
            double zScore = 0;
            if (tagBaseline.NormalStdDev > 0)
            {
                zScore = Math.Abs(tagFeatures.Mean - tagBaseline.NormalMean) / tagBaseline.NormalStdDev;
            }

            // Z-Score 转换为评分 (0-100)
            // Z=0 → 100分, Z=2 → 80分, Z=4 → 60分, Z=6 → 40分, Z≥8 → 20分
            double score = Math.Max(100 - zScore * 10, 20);
            scores.Add(score);

            // 记录问题标签
            if (zScore > 3)
            {
                problemTags.Add(tagId);
                messages.Add($"{tagId} 偏离基线 {zScore:F1}σ");
            }
        }

        return scores.Count > 0 ? (int)Math.Round(scores.Average()) : 80;
    }

    /// <summary>
    /// 计算趋势评分
    /// 衡量数据变化趋势
    /// </summary>
    private int CalculateTrendScore(
        DeviceFeatures features,
        List<string> problemTags,
        List<string> messages)
    {
        var scores = new List<double>();

        foreach (var (tagId, tagFeatures) in features.TagFeatures)
        {
            double score = 100;

            // 根据趋势方向和斜率评分
            // 上升或下降趋势都可能是问题（取决于具体标签语义）
            // 这里简单处理：斜率越大，扣分越多
            double normalizedSlope = tagFeatures.Mean != 0 
                ? Math.Abs(tagFeatures.TrendSlope) / Math.Abs(tagFeatures.Mean) * 100 
                : 0;

            // 每 1% 的斜率扣 5 分
            score -= normalizedSlope * 5;
            score = Math.Max(score, 20);

            scores.Add(score);

            // 显著趋势记录
            if (tagFeatures.TrendDirection != 0 && normalizedSlope > 1)
            {
                string direction = tagFeatures.TrendDirection > 0 ? "上升" : "下降";
                problemTags.Add(tagId);
                messages.Add($"{tagId} 呈{direction}趋势");
            }
        }

        return scores.Count > 0 ? (int)Math.Round(scores.Average()) : 100;
    }

    /// <summary>
    /// 计算稳定性评分
    /// 衡量数据波动程度
    /// </summary>
    private int CalculateStabilityScore(
        DeviceFeatures features,
        DeviceBaseline? baseline,
        List<string> problemTags,
        List<string> messages)
    {
        var scores = new List<double>();

        foreach (var (tagId, tagFeatures) in features.TagFeatures)
        {
            double score = 100;
            double cvThreshold = 0.2; // 默认 CV 阈值

            // 如果有基线，使用基线的 CV 作为参考
            if (baseline?.TagBaselines.TryGetValue(tagId, out var tagBaseline) == true)
            {
                cvThreshold = tagBaseline.NormalCV * 1.5; // 允许 1.5 倍波动
                cvThreshold = Math.Max(cvThreshold, 0.1); // 最小阈值
            }

            // 当前 CV 超过阈值时扣分
            if (tagFeatures.CoefficientOfVariation > cvThreshold)
            {
                double excess = tagFeatures.CoefficientOfVariation / cvThreshold;
                score -= (excess - 1) * 30; // 每超过 1 倍扣 30 分
                score = Math.Max(score, 20);

                problemTags.Add(tagId);
                messages.Add($"{tagId} 波动异常 (CV={tagFeatures.CoefficientOfVariation:F2})");
            }

            scores.Add(score);
        }

        return scores.Count > 0 ? (int)Math.Round(scores.Average()) : 100;
    }
}

/// <summary>
/// v45: 健康评分计算器（含异步告警查询）
/// </summary>
public sealed class HealthScoreCalculatorAsync
{
    private readonly IHealthScoreCalculator _calculator;
    private readonly IAlarmRepository _alarmRepo;

    public HealthScoreCalculatorAsync(
        IHealthScoreCalculator calculator,
        IAlarmRepository alarmRepo)
    {
        _calculator = calculator;
        _alarmRepo = alarmRepo;
    }

    /// <summary>
    /// 计算健康评分（含告警查询）
    /// </summary>
    public async Task<HealthScore> CalculateAsync(
        DeviceFeatures features, 
        DeviceBaseline? baseline,
        CancellationToken ct)
    {
        // 先计算基础分数
        var score = _calculator.Calculate(features, baseline);

        // 查询未关闭的告警数量
        int openAlarmCount = await _alarmRepo.GetOpenCountAsync(features.DeviceId, ct);

        // 根据告警数量调整分数
        int alarmScore = openAlarmCount switch
        {
            0 => 100,
            1 => 80,
            2 => 60,
            3 => 40,
            _ => 20
        };

        // 重新计算综合分数
        double weightedScore = 
            score.DeviationScore * 0.40 +
            score.TrendScore * 0.30 +
            score.StabilityScore * 0.20 +
            alarmScore * 0.10;

        int healthIndex = (int)Math.Round(weightedScore);
        healthIndex = Math.Clamp(healthIndex, 0, 100);

        var level = healthIndex switch
        {
            >= 80 => HealthLevel.Healthy,
            >= 60 => HealthLevel.Attention,
            >= 40 => HealthLevel.Warning,
            _ => HealthLevel.Critical
        };

        return score with
        {
            Index = healthIndex,
            Level = level,
            AlarmScore = alarmScore
        };
    }
}
