using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IntelliMaint.Application.Services;

/// <summary>
/// v62: 健康评估服务（增强版）
/// 整合特征提取、基线管理、健康评分和关联分析
/// 支持标签重要性加权、告警严重度加权和多标签关联分析
/// </summary>
public sealed class HealthAssessmentService
{
    private readonly IFeatureExtractor _featureExtractor;
    private readonly IHealthScoreCalculator _scoreCalculator;
    private readonly IHealthBaselineRepository _baselineRepo;
    private readonly IAlarmRepository _alarmRepo;
    private readonly ITelemetryRepository _telemetryRepo;
    private readonly ICorrelationAnalyzer _correlationAnalyzer;
    private readonly HealthAssessmentOptions _options;
    private readonly ILogger<HealthAssessmentService> _logger;

    public HealthAssessmentService(
        IFeatureExtractor featureExtractor,
        IHealthScoreCalculator scoreCalculator,
        IHealthBaselineRepository baselineRepo,
        IAlarmRepository alarmRepo,
        ITelemetryRepository telemetryRepo,
        ICorrelationAnalyzer correlationAnalyzer,
        IOptions<HealthAssessmentOptions> options,
        ILogger<HealthAssessmentService> logger)
    {
        _featureExtractor = featureExtractor;
        _scoreCalculator = scoreCalculator;
        _baselineRepo = baselineRepo;
        _alarmRepo = alarmRepo;
        _telemetryRepo = telemetryRepo;
        _correlationAnalyzer = correlationAnalyzer;
        _options = options.Value;
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
        var window = windowMinutes ?? _options.DefaultWindowMinutes;

        // 1. 提取特征
        var features = await _featureExtractor.ExtractAsync(deviceId, window, ct);
        if (features == null)
        {
            _logger.LogDebug("No features extracted for device {DeviceId}", deviceId);
            return null;
        }

        // 2. 获取基线
        var baseline = await _baselineRepo.GetAsync(deviceId, ct);

        // 3. 计算健康评分（已包含标签重要性加权）
        var score = _scoreCalculator.Calculate(features, baseline);

        // 4. 查询告警并调整分数
        int openAlarmCount = await _alarmRepo.GetOpenCountAsync(deviceId, ct);
        score = AdjustScoreWithAlarms(score, openAlarmCount);

        // 5. v62: 执行关联分析并应用扣分
        score = await ApplyCorrelationAnalysisAsync(deviceId, score, window, ct);

        _logger.LogInformation(
            "Health assessment for {DeviceId}: Index={Index}, Level={Level}, Alarms={AlarmCount}",
            deviceId, score.Index, score.Level, openAlarmCount);

        return score;
    }

    /// <summary>
    /// 评估所有设备的健康状态
    /// </summary>
    public async Task<IReadOnlyList<HealthScore>> AssessAllDevicesAsync(
        int? windowMinutes = null,
        CancellationToken ct = default)
    {
        var window = windowMinutes ?? _options.DefaultWindowMinutes;

        // 1. 批量提取特征
        var allFeatures = await _featureExtractor.ExtractAllAsync(window, ct);

        // 2. 获取所有基线
        var baselines = await _baselineRepo.ListAsync(ct);
        var baselineDict = baselines.ToDictionary(b => b.DeviceId);

        // 3. 批量获取所有设备的告警数量（优化N+1查询）
        var deviceIds = allFeatures.Select(f => f.DeviceId).ToList();
        var alarmCountDict = await _alarmRepo.GetOpenCountByDevicesAsync(deviceIds, ct);

        // 4. 计算每个设备的健康评分
        var scores = new List<HealthScore>();

        foreach (var features in allFeatures)
        {
            baselineDict.TryGetValue(features.DeviceId, out var baseline);
            var score = _scoreCalculator.Calculate(features, baseline);

            // 使用批量获取的告警数量
            alarmCountDict.TryGetValue(features.DeviceId, out var openAlarmCount);
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
        int? learningHours = null,
        CancellationToken ct = default)
    {
        var hours = learningHours ?? _options.BaselineLearningHours;

        _logger.LogInformation("Learning baseline for device {DeviceId}, hours={Hours}",
            deviceId, hours);

        // 提取更长时间窗口的特征
        var features = await _featureExtractor.ExtractAsync(deviceId, hours * 60, ct);
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
            LearningHours = hours,
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
    /// v61: 使用配置的权重和阈值
    /// </summary>
    private HealthScore AdjustScoreWithAlarms(HealthScore score, int openAlarmCount)
    {
        // 使用简化的告警评分（按数量）
        // 如需按严重度评分，需要查询完整告警列表
        int alarmScore = AlarmScoreCalculator.CalculateByCount(openAlarmCount);

        // 使用配置的权重重新计算综合分数
        var weights = _options.Weights;
        double weightedScore =
            score.DeviationScore * weights.Deviation +
            score.TrendScore * weights.Trend +
            score.StabilityScore * weights.Stability +
            alarmScore * weights.Alarm;

        int healthIndex = (int)Math.Round(weightedScore);
        healthIndex = Math.Clamp(healthIndex, 0, 100);

        // 使用配置的阈值确定健康等级
        var thresholds = _options.LevelThresholds;
        var level = healthIndex switch
        {
            _ when healthIndex >= thresholds.HealthyMin => HealthLevel.Healthy,
            _ when healthIndex >= thresholds.AttentionMin => HealthLevel.Attention,
            _ when healthIndex >= thresholds.WarningMin => HealthLevel.Warning,
            _ => HealthLevel.Critical
        };

        return score with
        {
            Index = healthIndex,
            Level = level,
            AlarmScore = alarmScore
        };
    }

    /// <summary>
    /// v62: 执行关联分析并应用扣分
    /// </summary>
    private async Task<HealthScore> ApplyCorrelationAnalysisAsync(
        string deviceId,
        HealthScore score,
        int windowMinutes,
        CancellationToken ct)
    {
        try
        {
            // 获取最近的遥测数据
            var endTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var startTs = endTs - windowMinutes * 60 * 1000L;

            var recentData = await _telemetryRepo.QuerySimpleAsync(
                deviceId, null, startTs, endTs, 1000, ct);

            if (recentData.Count < 10)
            {
                return score; // 数据不足，跳过关联分析
            }

            // 执行关联分析
            var anomalies = await _correlationAnalyzer.AnalyzeAsync(deviceId, recentData, ct);

            if (anomalies.Count == 0)
            {
                return score;
            }

            // 计算关联异常扣分
            int totalPenalty = anomalies.Sum(a => a.PenaltyScore);

            // 扣分后不低于最低分
            int newIndex = Math.Max(score.Index - totalPenalty, _options.AlarmScore.MinScore);

            // 重新计算健康等级
            var thresholds = _options.LevelThresholds;
            var newLevel = newIndex switch
            {
                _ when newIndex >= thresholds.HealthyMin => HealthLevel.Healthy,
                _ when newIndex >= thresholds.AttentionMin => HealthLevel.Attention,
                _ when newIndex >= thresholds.WarningMin => HealthLevel.Warning,
                _ => HealthLevel.Critical
            };

            // 添加关联异常诊断信息
            var correlationMessages = anomalies.Select(a => a.RiskDescription ?? a.RuleName);
            var existingMessage = score.DiagnosticMessage;
            var newMessage = string.Join("; ", correlationMessages.Take(2));
            if (!string.IsNullOrEmpty(existingMessage))
            {
                newMessage = existingMessage + "; " + newMessage;
            }

            _logger.LogDebug(
                "Correlation analysis for {DeviceId}: {Count} anomalies detected, penalty={Penalty}",
                deviceId, anomalies.Count, totalPenalty);

            return score with
            {
                Index = newIndex,
                Level = newLevel,
                DiagnosticMessage = newMessage
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Correlation analysis failed for device {DeviceId}", deviceId);
            return score; // 分析失败时返回原始分数
        }
    }
}
