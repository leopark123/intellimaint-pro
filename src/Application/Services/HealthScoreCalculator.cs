using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IntelliMaint.Application.Services;

/// <summary>
/// v61: 健康评分计算器实现（增强版）
/// 支持标签重要性加权和告警严重度加权
/// </summary>
public sealed class HealthScoreCalculator : IHealthScoreCalculator
{
    private readonly ITagImportanceMatcher _importanceMatcher;
    private readonly HealthAssessmentOptions _options;
    private readonly ILogger<HealthScoreCalculator> _logger;

    public HealthScoreCalculator(
        ITagImportanceMatcher importanceMatcher,
        IOptions<HealthAssessmentOptions> options,
        ILogger<HealthScoreCalculator> logger)
    {
        _importanceMatcher = importanceMatcher;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public HealthScore Calculate(DeviceFeatures features, DeviceBaseline? baseline)
    {
        var problemTags = new List<ProblemTagInfo>();
        var diagnosticMessages = new List<string>();

        // 获取所有标签的重要性
        var tagIds = features.TagFeatures.Keys.ToList();
        var importances = _importanceMatcher.GetImportances(tagIds);

        // 1. 计算偏差评分（加权）
        int deviationScore = CalculateDeviationScore(features, baseline, importances, problemTags, diagnosticMessages);

        // 2. 计算趋势评分（加权）
        int trendScore = CalculateTrendScore(features, importances, problemTags, diagnosticMessages);

        // 3. 计算稳定性评分（加权）
        int stabilityScore = CalculateStabilityScore(features, baseline, importances, problemTags, diagnosticMessages);

        // 4. 告警评分（默认满分，实际由 HealthAssessmentService 计算）
        int alarmScore = 100;

        // 综合评分（使用配置的权重）
        var weights = _options.Weights;
        double weightedScore =
            deviationScore * weights.Deviation +
            trendScore * weights.Trend +
            stabilityScore * weights.Stability +
            alarmScore * weights.Alarm;

        int healthIndex = (int)Math.Round(weightedScore);
        healthIndex = Math.Clamp(healthIndex, 0, 100);

        // 确定健康等级（使用配置的阈值）
        var thresholds = _options.LevelThresholds;
        var level = healthIndex switch
        {
            _ when healthIndex >= thresholds.HealthyMin => HealthLevel.Healthy,
            _ when healthIndex >= thresholds.AttentionMin => HealthLevel.Attention,
            _ when healthIndex >= thresholds.WarningMin => HealthLevel.Warning,
            _ => HealthLevel.Critical
        };

        // 生成诊断消息
        string? diagnosticMessage = diagnosticMessages.Count > 0
            ? string.Join("; ", diagnosticMessages.Take(3))
            : null;

        _logger.LogDebug(
            "Health score for {DeviceId}: Index={Index}, Level={Level}, " +
            "Deviation={Deviation}, Trend={Trend}, Stability={Stability}, ProblemTags={ProblemCount}",
            features.DeviceId, healthIndex, level,
            deviationScore, trendScore, stabilityScore, problemTags.Count);

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
            ProblemTags = problemTags.Select(p => p.TagId).Distinct().ToList(),
            DiagnosticMessage = diagnosticMessage
        };
    }

    /// <summary>
    /// 计算偏差评分（加权版）
    /// </summary>
    private int CalculateDeviationScore(
        DeviceFeatures features,
        DeviceBaseline? baseline,
        IReadOnlyDictionary<string, TagImportance> importances,
        List<ProblemTagInfo> problemTags,
        List<string> messages)
    {
        if (baseline == null || baseline.TagBaselines.Count == 0)
        {
            return 80; // 无基线时返回默认评分
        }

        double totalWeight = 0;
        double weightedSum = 0;

        foreach (var (tagId, tagFeatures) in features.TagFeatures)
        {
            if (!baseline.TagBaselines.TryGetValue(tagId, out var tagBaseline))
            {
                continue;
            }

            // 计算 Z-Score
            double zScore = 0;
            if (tagBaseline.NormalStdDev > 0)
            {
                zScore = Math.Abs(tagFeatures.Mean - tagBaseline.NormalMean) / tagBaseline.NormalStdDev;
            }

            // Z-Score 转换为评分
            double score = Math.Max(100 - zScore * 10, 20);

            // 获取标签权重
            var importance = importances.GetValueOrDefault(tagId, _options.DefaultTagImportance);
            var weight = (int)importance;

            weightedSum += score * weight;
            totalWeight += weight;

            // 记录问题标签（关键标签阈值更低）
            double zThreshold = importance switch
            {
                TagImportance.Critical => 2.0,
                TagImportance.Major => 2.5,
                TagImportance.Minor => 3.0,
                _ => 3.5
            };

            if (zScore > zThreshold)
            {
                problemTags.Add(new ProblemTagInfo
                {
                    TagId = tagId,
                    Importance = importance,
                    ProblemType = "Deviation",
                    Description = $"偏离基线 {zScore:F1}σ",
                    ZScore = zScore
                });
                messages.Add($"{tagId} 偏离基线 {zScore:F1}σ");
            }
        }

        return totalWeight > 0 ? (int)Math.Round(weightedSum / totalWeight) : 80;
    }

    /// <summary>
    /// 计算趋势评分（加权版）
    /// </summary>
    private int CalculateTrendScore(
        DeviceFeatures features,
        IReadOnlyDictionary<string, TagImportance> importances,
        List<ProblemTagInfo> problemTags,
        List<string> messages)
    {
        double totalWeight = 0;
        double weightedSum = 0;

        foreach (var (tagId, tagFeatures) in features.TagFeatures)
        {
            double score = 100;

            double normalizedSlope = tagFeatures.Mean != 0
                ? Math.Abs(tagFeatures.TrendSlope) / Math.Abs(tagFeatures.Mean) * 100
                : 0;

            score -= normalizedSlope * 5;
            score = Math.Max(score, 20);

            var importance = importances.GetValueOrDefault(tagId, _options.DefaultTagImportance);
            var weight = (int)importance;

            weightedSum += score * weight;
            totalWeight += weight;

            // 关键标签的趋势变化阈值更低
            double trendThreshold = importance switch
            {
                TagImportance.Critical => 0.5,
                TagImportance.Major => 0.8,
                _ => 1.0
            };

            if (tagFeatures.TrendDirection != 0 && normalizedSlope > trendThreshold)
            {
                string direction = tagFeatures.TrendDirection > 0 ? "上升" : "下降";
                problemTags.Add(new ProblemTagInfo
                {
                    TagId = tagId,
                    Importance = importance,
                    ProblemType = "Trend",
                    Description = $"呈{direction}趋势 ({normalizedSlope:F1}%)"
                });
                messages.Add($"{tagId} 呈{direction}趋势");
            }
        }

        return totalWeight > 0 ? (int)Math.Round(weightedSum / totalWeight) : 100;
    }

    /// <summary>
    /// 计算稳定性评分（加权版）
    /// </summary>
    private int CalculateStabilityScore(
        DeviceFeatures features,
        DeviceBaseline? baseline,
        IReadOnlyDictionary<string, TagImportance> importances,
        List<ProblemTagInfo> problemTags,
        List<string> messages)
    {
        double totalWeight = 0;
        double weightedSum = 0;

        foreach (var (tagId, tagFeatures) in features.TagFeatures)
        {
            double score = 100;
            double cvThreshold = 0.2;

            if (baseline?.TagBaselines.TryGetValue(tagId, out var tagBaseline) == true)
            {
                cvThreshold = tagBaseline.NormalCV * 1.5;
                cvThreshold = Math.Max(cvThreshold, 0.1);
            }

            if (tagFeatures.CoefficientOfVariation > cvThreshold)
            {
                double excess = tagFeatures.CoefficientOfVariation / cvThreshold;
                score -= (excess - 1) * 30;
                score = Math.Max(score, 20);

                var importance = importances.GetValueOrDefault(tagId, _options.DefaultTagImportance);

                problemTags.Add(new ProblemTagInfo
                {
                    TagId = tagId,
                    Importance = importance,
                    ProblemType = "Stability",
                    Description = $"波动异常 (CV={tagFeatures.CoefficientOfVariation:F2})"
                });
                messages.Add($"{tagId} 波动异常 (CV={tagFeatures.CoefficientOfVariation:F2})");
            }

            var imp = importances.GetValueOrDefault(tagId, _options.DefaultTagImportance);
            var weight = (int)imp;

            weightedSum += score * weight;
            totalWeight += weight;
        }

        return totalWeight > 0 ? (int)Math.Round(weightedSum / totalWeight) : 100;
    }
}

/// <summary>
/// v61: 告警评分计算器
/// 支持严重度加权和持续时间加权
/// </summary>
public static class AlarmScoreCalculator
{
    /// <summary>
    /// 根据告警列表计算告警评分
    /// </summary>
    public static int CalculateAlarmScore(
        IEnumerable<AlarmRecord> openAlarms,
        AlarmScoreConfig config)
    {
        var alarmList = openAlarms.ToList();
        if (alarmList.Count == 0)
            return 100;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int totalPenalty = 0;

        foreach (var alarm in alarmList)
        {
            // 基础扣分（按严重度）
            int basePenalty = alarm.Severity switch
            {
                >= 4 => config.CriticalPenalty,  // Critical (4-5)
                3 => config.ErrorPenalty,         // Error
                2 => config.WarningPenalty,       // Warning
                _ => config.InfoPenalty           // Info
            };

            // 持续时间加权
            double multiplier = 1.0;
            if (config.ConsiderDuration)
            {
                long durationMs = now - alarm.Ts;
                double durationHours = durationMs / 3600000.0;
                multiplier = 1 + Math.Min(durationHours * config.DurationFactorPerHour, config.MaxDurationMultiplier - 1);
            }

            totalPenalty += (int)(basePenalty * multiplier);
        }

        // 限制最低分数
        return Math.Max(100 - totalPenalty, config.MinScore);
    }

    /// <summary>
    /// 简化版：仅按告警数量计算（兼容旧逻辑）
    /// </summary>
    public static int CalculateByCount(int openAlarmCount)
    {
        return openAlarmCount switch
        {
            0 => 100,
            1 => 80,
            2 => 60,
            3 => 40,
            _ => 20
        };
    }
}
