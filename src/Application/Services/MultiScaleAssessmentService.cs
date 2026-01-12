using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IntelliMaint.Application.Services;

/// <summary>
/// v62: 多尺度健康评估服务
/// 支持短期（突发异常）、中期（渐变趋势）、长期（整体状态）三个时间窗口
/// </summary>
public sealed class MultiScaleAssessmentService : IMultiScaleAssessmentService
{
    private readonly IFeatureExtractor _featureExtractor;
    private readonly IHealthScoreCalculator _scoreCalculator;
    private readonly IHealthBaselineRepository _baselineRepo;
    private readonly HealthAssessmentOptions _options;
    private readonly ILogger<MultiScaleAssessmentService> _logger;

    public MultiScaleAssessmentService(
        IFeatureExtractor featureExtractor,
        IHealthScoreCalculator scoreCalculator,
        IHealthBaselineRepository baselineRepo,
        IOptions<HealthAssessmentOptions> options,
        ILogger<MultiScaleAssessmentService> logger)
    {
        _featureExtractor = featureExtractor;
        _scoreCalculator = scoreCalculator;
        _baselineRepo = baselineRepo;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// 执行多尺度健康评估
    /// </summary>
    public async Task<MultiScaleScore?> AssessAsync(
        string deviceId,
        CancellationToken ct = default)
    {
        var config = _options.MultiScale;

        if (!config.Enabled)
        {
            _logger.LogDebug("Multi-scale assessment is disabled");
            return null;
        }

        // 获取基线
        var baseline = await _baselineRepo.GetAsync(deviceId, ct);

        // 并行评估三个时间尺度
        var shortTermTask = EvaluateScaleAsync(deviceId, config.ShortTermMinutes, baseline, ct);
        var mediumTermTask = EvaluateScaleAsync(deviceId, config.MediumTermMinutes, baseline, ct);
        var longTermTask = EvaluateScaleAsync(deviceId, config.LongTermMinutes, baseline, ct);

        await Task.WhenAll(shortTermTask, mediumTermTask, longTermTask);

        var shortTermScore = shortTermTask.Result;
        var mediumTermScore = mediumTermTask.Result;
        var longTermScore = longTermTask.Result;

        // 如果所有尺度都无数据，返回 null
        if (shortTermScore == null && mediumTermScore == null && longTermScore == null)
        {
            _logger.LogDebug("No data available for multi-scale assessment of device {DeviceId}", deviceId);
            return null;
        }

        // 使用默认值填充缺失的尺度（100分表示健康）
        int shortScore = shortTermScore?.Index ?? 100;
        int mediumScore = mediumTermScore?.Index ?? 100;
        int longScore = longTermScore?.Index ?? 100;

        // 计算综合评分
        int compositeScore = (int)Math.Round(
            shortScore * config.ShortTermWeight +
            mediumScore * config.MediumTermWeight +
            longScore * config.LongTermWeight);

        compositeScore = Math.Clamp(compositeScore, 0, 100);

        // 分析趋势方向
        var (trendDirection, trendDescription) = AnalyzeTrend(shortScore, mediumScore, longScore);

        var result = new MultiScaleScore
        {
            ShortTermScore = shortScore,
            MediumTermScore = mediumScore,
            LongTermScore = longScore,
            CompositeScore = compositeScore,
            TrendDirection = trendDirection,
            TrendDescription = trendDescription
        };

        _logger.LogDebug(
            "Multi-scale assessment for {DeviceId}: Short={Short}, Medium={Medium}, Long={Long}, Composite={Composite}, Trend={Trend}",
            deviceId, shortScore, mediumScore, longScore, compositeScore, trendDescription);

        return result;
    }

    /// <summary>
    /// 评估单个时间尺度
    /// </summary>
    private async Task<HealthScore?> EvaluateScaleAsync(
        string deviceId,
        int windowMinutes,
        DeviceBaseline? baseline,
        CancellationToken ct)
    {
        try
        {
            var features = await _featureExtractor.ExtractAsync(deviceId, windowMinutes, ct);
            if (features == null)
            {
                return null;
            }

            return _scoreCalculator.Calculate(features, baseline);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to evaluate {Window}min scale for device {DeviceId}",
                windowMinutes, deviceId);
            return null;
        }
    }

    /// <summary>
    /// 分析趋势方向
    /// 基于短期与长期分数的差异判断趋势
    /// </summary>
    private (int direction, string description) AnalyzeTrend(int shortScore, int mediumScore, int longScore)
    {
        // 计算短期相对于长期的变化
        int shortTermDelta = shortScore - longScore;
        int mediumTermDelta = mediumScore - longScore;

        // 阈值：超过5分认为有明显变化
        const int significantThreshold = 5;

        if (shortTermDelta <= -significantThreshold && mediumTermDelta <= -significantThreshold / 2)
        {
            // 短期和中期都在下降
            if (shortTermDelta <= -15)
            {
                return (-2, "急剧恶化");
            }
            return (-1, "持续下降");
        }

        if (shortTermDelta >= significantThreshold && mediumTermDelta >= significantThreshold / 2)
        {
            // 短期和中期都在上升
            if (shortTermDelta >= 15)
            {
                return (2, "快速恢复");
            }
            return (1, "逐步改善");
        }

        if (shortTermDelta <= -significantThreshold && mediumTermDelta >= 0)
        {
            // 短期下降但中期稳定，可能是突发问题
            return (-1, "突发异常");
        }

        if (shortTermDelta >= significantThreshold && mediumTermDelta <= 0)
        {
            // 短期上升但中期稳定，可能是临时恢复
            return (0, "临时恢复");
        }

        // 波动在阈值内，认为稳定
        return (0, "状态稳定");
    }

    /// <summary>
    /// 获取多尺度评估摘要（用于诊断消息）
    /// </summary>
    public string? GetSummary(MultiScaleScore? score)
    {
        if (score == null) return null;

        var config = _options.MultiScale;
        var parts = new List<string>();

        // 添加各时间尺度描述
        parts.Add($"短期({config.ShortTermMinutes}min):{score.ShortTermScore}");
        parts.Add($"中期({config.MediumTermMinutes}min):{score.MediumTermScore}");
        parts.Add($"长期({config.LongTermMinutes / 60}h):{score.LongTermScore}");

        // 添加趋势
        if (!string.IsNullOrEmpty(score.TrendDescription))
        {
            parts.Add($"趋势:{score.TrendDescription}");
        }

        return string.Join(", ", parts);
    }
}

/// <summary>
/// v62: 多尺度评估服务接口
/// </summary>
public interface IMultiScaleAssessmentService
{
    /// <summary>
    /// 执行多尺度健康评估
    /// </summary>
    Task<MultiScaleScore?> AssessAsync(string deviceId, CancellationToken ct = default);

    /// <summary>
    /// 获取多尺度评估摘要
    /// </summary>
    string? GetSummary(MultiScaleScore? score);
}
