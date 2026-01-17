using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IntelliMaint.Application.Services;

/// <summary>
/// v65: 健康评分计算器实现（优化版）
/// - 使用 Sigmoid 函数平滑 Z-Score 转换
/// - 诊断消息按严重度排序
/// - 增强边缘情况处理
/// </summary>
public sealed class HealthScoreCalculator : IHealthScoreCalculator
{
    private readonly ITagImportanceMatcher _importanceMatcher;
    private readonly HealthAssessmentOptions _options;
    private readonly ILogger<HealthScoreCalculator> _logger;

    // v65: Z-Score 转换参数（Sigmoid 函数）
    private const double ZScoreMidpoint = 3.0;  // 50% 扣分点对应的 Z-Score
    private const double ZScoreSteepness = 1.2; // 曲线陡峭度

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

        // 获取所有标签的重要性
        var tagIds = features.TagFeatures.Keys.ToList();
        var importances = _importanceMatcher.GetImportances(tagIds);

        // 1. 计算偏差评分（加权）
        int deviationScore = CalculateDeviationScore(features, baseline, importances, problemTags);

        // 2. 计算趋势评分（加权）
        int trendScore = CalculateTrendScore(features, importances, problemTags);

        // 3. 计算稳定性评分（加权）
        int stabilityScore = CalculateStabilityScore(features, baseline, importances, problemTags);

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

        // v65: 生成诊断消息（按标签重要性排序，优先显示最严重的问题）
        string? diagnosticMessage = null;
        if (problemTags.Count > 0)
        {
            var sortedProblems = problemTags
                .OrderByDescending(p => (int)p.Importance)
                .ThenByDescending(p => p.ZScore)
                .Take(3)
                .Select(p => p.Description);
            diagnosticMessage = string.Join("; ", sortedProblems);
        }

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
    /// v65: 计算偏差评分（使用 Sigmoid 函数平滑扣分）
    /// </summary>
    private int CalculateDeviationScore(
        DeviceFeatures features,
        DeviceBaseline? baseline,
        IReadOnlyDictionary<string, TagImportance> importances,
        List<ProblemTagInfo> problemTags)
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

            // 计算 Z-Score（带边缘情况保护）
            double zScore = 0;
            if (tagBaseline.NormalStdDev > 1e-9 && !double.IsNaN(tagFeatures.Mean) && !double.IsInfinity(tagFeatures.Mean))
            {
                zScore = Math.Abs(tagFeatures.Mean - tagBaseline.NormalMean) / tagBaseline.NormalStdDev;
                // v65: 防止极端值
                zScore = Math.Min(zScore, 10.0);
            }

            // v65: Z-Score 转换为评分（使用 Sigmoid 函数实现平滑扣分）
            // Sigmoid: score = 100 * (1 - sigmoid((z - midpoint) * steepness))
            // 效果: z=0 → ~98分, z=3 → ~50分, z=6 → ~5分
            double sigmoidValue = 1.0 / (1.0 + Math.Exp(-(zScore - ZScoreMidpoint) * ZScoreSteepness));
            double score = 100 * (1 - sigmoidValue * 0.95); // 最低保留 5% 基础分
            score = Math.Clamp(score, 5, 100);

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
            }
        }

        return totalWeight > 0 ? (int)Math.Round(weightedSum / totalWeight) : 80;
    }

    /// <summary>
    /// v65: 计算趋势评分（使用平方根函数平滑扣分）
    /// </summary>
    private int CalculateTrendScore(
        DeviceFeatures features,
        IReadOnlyDictionary<string, TagImportance> importances,
        List<ProblemTagInfo> problemTags)
    {
        double totalWeight = 0;
        double weightedSum = 0;

        foreach (var (tagId, tagFeatures) in features.TagFeatures)
        {
            double score = 100;

            // v65: 边缘情况保护
            if (double.IsNaN(tagFeatures.TrendSlope) || double.IsInfinity(tagFeatures.TrendSlope))
            {
                var imp = importances.GetValueOrDefault(tagId, _options.DefaultTagImportance);
                weightedSum += 80 * (int)imp; // 数据异常时给予较低分数
                totalWeight += (int)imp;
                continue;
            }

            // v65: 使用平滑的趋势扣分（避免小趋势过度扣分）
            double normalizedSlope = Math.Abs(tagFeatures.Mean) > 1e-9
                ? Math.Abs(tagFeatures.TrendSlope) / Math.Abs(tagFeatures.Mean) * 100
                : Math.Min(Math.Abs(tagFeatures.TrendSlope) * 10, 20); // Mean 接近 0 时的备用计算

            // 使用平方根函数平滑扣分：小趋势扣分少，大趋势扣分多
            double penalty = Math.Sqrt(normalizedSlope) * 8;
            score = Math.Clamp(100 - penalty, 20, 100);

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
                    Description = $"呈{direction}趋势 ({normalizedSlope:F1}%)",
                    ZScore = normalizedSlope / trendThreshold // v65: 用于严重度排序
                });
            }
        }

        return totalWeight > 0 ? (int)Math.Round(weightedSum / totalWeight) : 100;
    }

    /// <summary>
    /// v65: 计算稳定性评分（使用对数函数平滑扣分）
    /// </summary>
    private int CalculateStabilityScore(
        DeviceFeatures features,
        DeviceBaseline? baseline,
        IReadOnlyDictionary<string, TagImportance> importances,
        List<ProblemTagInfo> problemTags)
    {
        double totalWeight = 0;
        double weightedSum = 0;

        foreach (var (tagId, tagFeatures) in features.TagFeatures)
        {
            var imp = importances.GetValueOrDefault(tagId, _options.DefaultTagImportance);
            var weight = (int)imp;

            // v65: 边缘情况保护
            if (double.IsNaN(tagFeatures.CoefficientOfVariation) ||
                double.IsInfinity(tagFeatures.CoefficientOfVariation) ||
                tagFeatures.CoefficientOfVariation < 0)
            {
                weightedSum += 80 * weight; // 数据异常时给予较低分数
                totalWeight += weight;
                continue;
            }

            double score = 100;
            double cvThreshold = 0.2;

            if (baseline?.TagBaselines.TryGetValue(tagId, out var tagBaseline) == true &&
                tagBaseline.NormalCV > 0)
            {
                cvThreshold = tagBaseline.NormalCV * 1.5;
                cvThreshold = Math.Clamp(cvThreshold, 0.05, 0.5); // v65: 限制阈值范围
            }

            if (tagFeatures.CoefficientOfVariation > cvThreshold)
            {
                double excess = tagFeatures.CoefficientOfVariation / cvThreshold;
                // v65: 使用对数函数平滑扣分，避免过度惩罚
                double penalty = Math.Log(excess + 1) * 40;
                score = Math.Clamp(100 - penalty, 20, 100);

                problemTags.Add(new ProblemTagInfo
                {
                    TagId = tagId,
                    Importance = imp,
                    ProblemType = "Stability",
                    Description = $"波动异常 (CV={tagFeatures.CoefficientOfVariation:F2})",
                    ZScore = excess // 用于排序
                });
            }

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
