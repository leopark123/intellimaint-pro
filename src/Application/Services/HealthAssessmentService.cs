using IntelliMaint.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Application.Services;

/// <summary>
/// v45: 健康评估服务
/// 整合特征提取、基线管理和健康评分
/// </summary>
public sealed class HealthAssessmentService
{
    private readonly IFeatureExtractor _featureExtractor;
    private readonly IHealthScoreCalculator _scoreCalculator;
    private readonly IHealthBaselineRepository _baselineRepo;
    private readonly IAlarmRepository _alarmRepo;
    private readonly ILogger<HealthAssessmentService> _logger;

    // 默认时间窗口（分钟）
    private const int DefaultWindowMinutes = 30;

    public HealthAssessmentService(
        IFeatureExtractor featureExtractor,
        IHealthScoreCalculator scoreCalculator,
        IHealthBaselineRepository baselineRepo,
        IAlarmRepository alarmRepo,
        ILogger<HealthAssessmentService> logger)
    {
        _featureExtractor = featureExtractor;
        _scoreCalculator = scoreCalculator;
        _baselineRepo = baselineRepo;
        _alarmRepo = alarmRepo;
        _logger = logger;
    }

    /// <summary>
    /// 评估单个设备的健康状态
    /// </summary>
    public async Task<HealthScore?> AssessDeviceAsync(
        string deviceId, 
        int? windowMinutes = null,
        CancellationToken ct = default)
    {
        var window = windowMinutes ?? DefaultWindowMinutes;

        // 1. 提取特征
        var features = await _featureExtractor.ExtractAsync(deviceId, window, ct);
        if (features == null)
        {
            _logger.LogDebug("No features extracted for device {DeviceId}", deviceId);
            return null;
        }

        // 2. 获取基线
        var baseline = await _baselineRepo.GetAsync(deviceId, ct);

        // 3. 计算健康评分
        var score = _scoreCalculator.Calculate(features, baseline);

        // 4. 查询告警并调整分数
        int openAlarmCount = await _alarmRepo.GetOpenCountAsync(deviceId, ct);
        score = AdjustScoreWithAlarms(score, openAlarmCount);

        _logger.LogInformation(
            "Health assessment for {DeviceId}: Index={Index}, Level={Level}",
            deviceId, score.Index, score.Level);

        return score;
    }

    /// <summary>
    /// 评估所有设备的健康状态
    /// </summary>
    public async Task<IReadOnlyList<HealthScore>> AssessAllDevicesAsync(
        int? windowMinutes = null,
        CancellationToken ct = default)
    {
        var window = windowMinutes ?? DefaultWindowMinutes;

        // 1. 批量提取特征
        var allFeatures = await _featureExtractor.ExtractAllAsync(window, ct);

        // 2. 获取所有基线
        var baselines = await _baselineRepo.ListAsync(ct);
        var baselineDict = baselines.ToDictionary(b => b.DeviceId);

        // 3. 计算每个设备的健康评分
        var scores = new List<HealthScore>();

        foreach (var features in allFeatures)
        {
            baselineDict.TryGetValue(features.DeviceId, out var baseline);
            var score = _scoreCalculator.Calculate(features, baseline);

            // 查询告警
            int openAlarmCount = await _alarmRepo.GetOpenCountAsync(features.DeviceId, ct);
            score = AdjustScoreWithAlarms(score, openAlarmCount);

            scores.Add(score);
        }

        _logger.LogInformation("Assessed {Count} devices", scores.Count);

        return scores;
    }

    /// <summary>
    /// 学习设备基线
    /// </summary>
    public async Task<DeviceBaseline?> LearnBaselineAsync(
        string deviceId,
        int learningHours = 24,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Learning baseline for device {DeviceId}, hours={Hours}", 
            deviceId, learningHours);

        // 提取更长时间窗口的特征
        var features = await _featureExtractor.ExtractAsync(deviceId, learningHours * 60, ct);
        if (features == null || features.TagFeatures.Count == 0)
        {
            _logger.LogWarning("Not enough data to learn baseline for device {DeviceId}", deviceId);
            return null;
        }

        // 构建基线
        var tagBaselines = new Dictionary<string, TagBaseline>();

        foreach (var (tagId, tagFeatures) in features.TagFeatures)
        {
            if (tagFeatures.Count < 100) // 至少 100 个样本
            {
                continue;
            }

            tagBaselines[tagId] = new TagBaseline
            {
                TagId = tagId,
                NormalMean = tagFeatures.Mean,
                NormalStdDev = tagFeatures.StdDev,
                NormalMin = tagFeatures.Min,
                NormalMax = tagFeatures.Max,
                NormalCV = tagFeatures.CoefficientOfVariation
            };
        }

        if (tagBaselines.Count == 0)
        {
            _logger.LogWarning("No tags with sufficient data for baseline, device {DeviceId}", deviceId);
            return null;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var baseline = new DeviceBaseline
        {
            DeviceId = deviceId,
            CreatedUtc = now,
            UpdatedUtc = now,
            SampleCount = features.SampleCount,
            LearningHours = learningHours,
            TagBaselines = tagBaselines
        };

        // 保存基线
        await _baselineRepo.SaveAsync(baseline, ct);

        _logger.LogInformation(
            "Baseline learned for device {DeviceId}: {TagCount} tags, {SampleCount} samples",
            deviceId, tagBaselines.Count, features.SampleCount);

        return baseline;
    }

    /// <summary>
    /// 删除设备基线
    /// </summary>
    public async Task DeleteBaselineAsync(string deviceId, CancellationToken ct = default)
    {
        await _baselineRepo.DeleteAsync(deviceId, ct);
        _logger.LogInformation("Baseline deleted for device {DeviceId}", deviceId);
    }

    /// <summary>
    /// 获取设备基线
    /// </summary>
    public Task<DeviceBaseline?> GetBaselineAsync(string deviceId, CancellationToken ct = default)
    {
        return _baselineRepo.GetAsync(deviceId, ct);
    }

    /// <summary>
    /// 根据告警数量调整健康评分
    /// </summary>
    private HealthScore AdjustScoreWithAlarms(HealthScore score, int openAlarmCount)
    {
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
