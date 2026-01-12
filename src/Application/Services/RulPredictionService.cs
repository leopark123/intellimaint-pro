using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IntelliMaint.Application.Services;

/// <summary>
/// v63: RUL (Remaining Useful Life) 剩余寿命预测服务
/// 基于健康指数历史趋势预测设备剩余使用寿命
/// </summary>
public sealed class RulPredictionService : IRulPredictionService
{
    private readonly IDeviceHealthSnapshotRepository _snapshotRepo;
    private readonly IDeviceRepository _deviceRepo;
    private readonly IDegradationDetectionService _degradationService;
    private readonly HealthAssessmentOptions _options;
    private readonly ILogger<RulPredictionService> _logger;

    public RulPredictionService(
        IDeviceHealthSnapshotRepository snapshotRepo,
        IDeviceRepository deviceRepo,
        IDegradationDetectionService degradationService,
        IOptions<HealthAssessmentOptions> options,
        ILogger<RulPredictionService> logger)
    {
        _snapshotRepo = snapshotRepo;
        _deviceRepo = deviceRepo;
        _degradationService = degradationService;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// 预测单个设备的 RUL
    /// </summary>
    public async Task<RulPrediction?> PredictDeviceRulAsync(
        string deviceId,
        CancellationToken ct = default)
    {
        var config = _options.RulPrediction;
        if (!config.Enabled)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var startTs = now - config.HistoryWindowDays * 24L * 3600 * 1000;

        // 获取健康指数历史
        var history = await _snapshotRepo.GetHistoryAsync(deviceId, startTs, now, ct);

        if (history.Count < config.MinDataPoints / 10) // 快照比遥测数据少
        {
            _logger.LogDebug("Not enough health snapshots for RUL prediction: {Count}", history.Count);
            return CreateInsufficientDataResult(deviceId, now);
        }

        // 提取健康指数序列
        var healthIndices = history
            .OrderBy(h => h.Timestamp)
            .Select(h => (Timestamp: h.Timestamp, Index: h.Index))
            .ToList();

        // 计算当前健康指数
        int currentIndex = healthIndices.Last().Index;

        // 如果当前已经低于失效阈值
        if (currentIndex <= config.FailureThreshold)
        {
            return CreateNearFailureResult(deviceId, now, currentIndex);
        }

        // 计算劣化速率（使用线性回归）
        var (slope, intercept, rSquared) = CalculateDegradationRate(healthIndices);

        // 如果没有明显劣化趋势
        if (slope >= -0.001) // 每小时下降不足 0.001 点
        {
            return CreateHealthyResult(deviceId, now, currentIndex, rSquared);
        }

        // 预测到达失效阈值的时间
        double hoursToFailure = (config.FailureThreshold - currentIndex) / slope;

        // 负值表示健康指数在上升，不会失效
        if (hoursToFailure < 0)
        {
            return CreateHealthyResult(deviceId, now, currentIndex, rSquared);
        }

        // 限制最大预测时间
        double maxHours = config.MaxPredictionDays * 24.0;
        bool isBeyondMax = hoursToFailure > maxHours;

        double daysToFailure = hoursToFailure / 24.0;
        double dailyRate = slope * 24; // 转换为每天下降点数

        // 确定风险等级
        var riskLevel = DetermineRiskLevel(daysToFailure);

        // 确定 RUL 状态
        var status = DetermineRulStatus(dailyRate, daysToFailure, rSquared);

        // 计算预测失效时间和建议维护时间
        long? predictedFailureTime = isBeyondMax ? null : now + (long)(hoursToFailure * 3600 * 1000);
        long? recommendedMaintenanceTime = predictedFailureTime.HasValue
            ? predictedFailureTime.Value - 7 * 24 * 3600 * 1000L // 提前7天维护
            : null;

        // 获取影响因素
        var factors = await GetInfluencingFactorsAsync(deviceId, ct);

        // 生成诊断消息
        string diagnosticMessage = GenerateDiagnosticMessage(
            status, daysToFailure, dailyRate, riskLevel, isBeyondMax);

        return new RulPrediction
        {
            DeviceId = deviceId,
            PredictionTimestamp = now,
            CurrentHealthIndex = currentIndex,
            RemainingUsefulLifeHours = isBeyondMax ? null : hoursToFailure,
            RemainingUsefulLifeDays = isBeyondMax ? null : daysToFailure,
            PredictedFailureTime = predictedFailureTime,
            Confidence = rSquared,
            DegradationRate = Math.Abs(dailyRate),
            ModelType = config.ModelType,
            Status = status,
            RiskLevel = riskLevel,
            RecommendedMaintenanceTime = recommendedMaintenanceTime,
            DiagnosticMessage = diagnosticMessage,
            Factors = factors
        };
    }

    /// <summary>
    /// 预测所有设备的 RUL
    /// </summary>
    public async Task<IReadOnlyList<RulPrediction>> PredictAllDevicesRulAsync(
        CancellationToken ct = default)
    {
        var devices = await _deviceRepo.ListAsync(ct);
        var results = new List<RulPrediction>();

        foreach (var device in devices.Where(d => d.Enabled))
        {
            try
            {
                var prediction = await PredictDeviceRulAsync(device.DeviceId, ct);
                if (prediction != null)
                {
                    results.Add(prediction);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to predict RUL for device {DeviceId}",
                    device.DeviceId);
            }
        }

        return results;
    }

    /// <summary>
    /// 计算劣化速率（线性回归）
    /// 返回：(slope, intercept, R²)，slope 为每小时健康指数变化
    /// </summary>
    private (double slope, double intercept, double rSquared) CalculateDegradationRate(
        List<(long Timestamp, int Index)> data)
    {
        int n = data.Count;
        if (n < 2) return (0, data.FirstOrDefault().Index, 0);

        // 转换时间戳为小时（相对于第一个点）
        long baseTime = data[0].Timestamp;
        var points = data.Select(d => (
            X: (d.Timestamp - baseTime) / 3600000.0, // 毫秒转小时
            Y: (double)d.Index
        )).ToList();

        double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0, sumY2 = 0;

        foreach (var (x, y) in points)
        {
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
        foreach (var (x, y) in points)
        {
            double predicted = intercept + slope * x;
            ssRes += (y - predicted) * (y - predicted);
        }

        double rSquared = ssTot > 0 ? 1 - ssRes / ssTot : 0;
        rSquared = Math.Max(0, Math.Min(1, rSquared));

        return (slope, intercept, rSquared);
    }

    /// <summary>
    /// 确定风险等级
    /// </summary>
    private RulRiskLevel DetermineRiskLevel(double daysToFailure)
    {
        return daysToFailure switch
        {
            < 1 => RulRiskLevel.Critical,
            < 7 => RulRiskLevel.High,
            < 30 => RulRiskLevel.Medium,
            _ => RulRiskLevel.Low
        };
    }

    /// <summary>
    /// 确定 RUL 状态
    /// </summary>
    private RulStatus DetermineRulStatus(double dailyRate, double daysToFailure, double confidence)
    {
        // 每天下降超过 2 点认为是加速劣化
        const double acceleratedThreshold = -2.0;
        // 每天下降超过 0.5 点认为是正常劣化
        const double normalThreshold = -0.5;

        if (daysToFailure < 7)
        {
            return RulStatus.NearFailure;
        }

        if (dailyRate < acceleratedThreshold)
        {
            return RulStatus.AcceleratedDegradation;
        }

        if (dailyRate < normalThreshold)
        {
            return RulStatus.NormalDegradation;
        }

        return RulStatus.Healthy;
    }

    /// <summary>
    /// 获取影响因素
    /// </summary>
    private async Task<IReadOnlyList<RulFactor>> GetInfluencingFactorsAsync(
        string deviceId,
        CancellationToken ct)
    {
        try
        {
            var degradations = await _degradationService.DetectDeviceDegradationAsync(deviceId, ct);

            return degradations
                .Where(d => d.IsDegrading)
                .OrderByDescending(d => Math.Abs(d.DegradationRate))
                .Take(5)
                .Select(d => new RulFactor
                {
                    Name = GetFactorName(d.TagId),
                    TagId = d.TagId,
                    Weight = Math.Min(1.0, Math.Abs(d.DegradationRate) / 5.0),
                    CurrentStatus = d.DegradationType.ToString(),
                    Contribution = -Math.Abs(d.DegradationRate) // 劣化因素都是负贡献
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get influencing factors for device {DeviceId}", deviceId);
            return Array.Empty<RulFactor>();
        }
    }

    /// <summary>
    /// 从标签ID提取因素名称
    /// </summary>
    private string GetFactorName(string tagId)
    {
        // 简单的标签名称提取逻辑
        var parts = tagId.Split('_', '.', '-');
        if (parts.Length > 1)
        {
            return parts[^1]; // 取最后一部分
        }
        return tagId;
    }

    /// <summary>
    /// 生成诊断消息
    /// </summary>
    private string GenerateDiagnosticMessage(
        RulStatus status,
        double daysToFailure,
        double dailyRate,
        RulRiskLevel riskLevel,
        bool isBeyondMax)
    {
        if (isBeyondMax)
        {
            return "设备状态良好，预计寿命超过一年";
        }

        return status switch
        {
            RulStatus.Healthy => "设备健康，无明显劣化趋势",
            RulStatus.NormalDegradation => $"设备正常老化，预计 {daysToFailure:F0} 天后需要维护",
            RulStatus.AcceleratedDegradation => $"检测到加速劣化（日均下降 {Math.Abs(dailyRate):F1} 点），建议尽快检查",
            RulStatus.NearFailure => $"设备临近失效，剩余 {daysToFailure:F1} 天，请立即安排维护",
            RulStatus.InsufficientData => "历史数据不足，无法预测",
            _ => "未知状态"
        };
    }

    /// <summary>
    /// 创建数据不足结果
    /// </summary>
    private RulPrediction CreateInsufficientDataResult(string deviceId, long now)
    {
        return new RulPrediction
        {
            DeviceId = deviceId,
            PredictionTimestamp = now,
            CurrentHealthIndex = 0,
            RemainingUsefulLifeHours = null,
            RemainingUsefulLifeDays = null,
            PredictedFailureTime = null,
            Confidence = 0,
            DegradationRate = 0,
            ModelType = _options.RulPrediction.ModelType,
            Status = RulStatus.InsufficientData,
            RiskLevel = RulRiskLevel.Low,
            RecommendedMaintenanceTime = null,
            DiagnosticMessage = "历史数据不足，无法进行 RUL 预测",
            Factors = Array.Empty<RulFactor>()
        };
    }

    /// <summary>
    /// 创建临近失效结果
    /// </summary>
    private RulPrediction CreateNearFailureResult(string deviceId, long now, int currentIndex)
    {
        return new RulPrediction
        {
            DeviceId = deviceId,
            PredictionTimestamp = now,
            CurrentHealthIndex = currentIndex,
            RemainingUsefulLifeHours = 0,
            RemainingUsefulLifeDays = 0,
            PredictedFailureTime = now,
            Confidence = 1.0,
            DegradationRate = 0,
            ModelType = _options.RulPrediction.ModelType,
            Status = RulStatus.NearFailure,
            RiskLevel = RulRiskLevel.Critical,
            RecommendedMaintenanceTime = now,
            DiagnosticMessage = $"设备已达到失效阈值（健康指数 {currentIndex}），请立即维护",
            Factors = Array.Empty<RulFactor>()
        };
    }

    /// <summary>
    /// 创建健康结果
    /// </summary>
    private RulPrediction CreateHealthyResult(string deviceId, long now, int currentIndex, double confidence)
    {
        return new RulPrediction
        {
            DeviceId = deviceId,
            PredictionTimestamp = now,
            CurrentHealthIndex = currentIndex,
            RemainingUsefulLifeHours = null,
            RemainingUsefulLifeDays = null,
            PredictedFailureTime = null,
            Confidence = confidence,
            DegradationRate = 0,
            ModelType = _options.RulPrediction.ModelType,
            Status = RulStatus.Healthy,
            RiskLevel = RulRiskLevel.Low,
            RecommendedMaintenanceTime = null,
            DiagnosticMessage = "设备健康，无明显劣化趋势",
            Factors = Array.Empty<RulFactor>()
        };
    }
}

/// <summary>
/// v63: RUL 预测服务接口
/// </summary>
public interface IRulPredictionService
{
    /// <summary>预测单个设备的 RUL</summary>
    Task<RulPrediction?> PredictDeviceRulAsync(string deviceId, CancellationToken ct = default);

    /// <summary>预测所有设备的 RUL</summary>
    Task<IReadOnlyList<RulPrediction>> PredictAllDevicesRulAsync(CancellationToken ct = default);
}
