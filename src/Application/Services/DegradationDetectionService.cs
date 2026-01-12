using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IntelliMaint.Application.Services;

/// <summary>
/// v63: 劣化检测服务
/// 检测设备参数的渐变异常（区别于突发异常）
/// </summary>
public sealed class DegradationDetectionService : IDegradationDetectionService
{
    private readonly ITelemetryRepository _telemetryRepo;
    private readonly IHealthBaselineRepository _baselineRepo;
    private readonly IDeviceRepository _deviceRepo;
    private readonly HealthAssessmentOptions _options;
    private readonly ILogger<DegradationDetectionService> _logger;

    public DegradationDetectionService(
        ITelemetryRepository telemetryRepo,
        IHealthBaselineRepository baselineRepo,
        IDeviceRepository deviceRepo,
        IOptions<HealthAssessmentOptions> options,
        ILogger<DegradationDetectionService> logger)
    {
        _telemetryRepo = telemetryRepo;
        _baselineRepo = baselineRepo;
        _deviceRepo = deviceRepo;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// 检测单个设备的劣化
    /// </summary>
    public async Task<IReadOnlyList<DegradationResult>> DetectDeviceDegradationAsync(
        string deviceId,
        CancellationToken ct = default)
    {
        var config = _options.Degradation;
        if (!config.Enabled)
        {
            return Array.Empty<DegradationResult>();
        }

        var results = new List<DegradationResult>();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var startTs = now - config.DetectionWindowDays * 24 * 3600 * 1000L;

        // 获取历史数据
        var data = await _telemetryRepo.QuerySimpleAsync(
            deviceId, null, startTs, now, 100000, ct);

        if (data.Count < 100)
        {
            _logger.LogDebug("Not enough data for degradation detection: {Count}", data.Count);
            return results;
        }

        // 获取基线（如果有）
        var baseline = await _baselineRepo.GetAsync(deviceId, ct);

        // 按标签分组
        var tagGroups = data.GroupBy(p => p.TagId).ToList();

        foreach (var group in tagGroups)
        {
            var tagData = group.OrderBy(p => p.Ts).ToList();
            if (tagData.Count < 50) continue;

            var tagBaseline = baseline?.TagBaselines.GetValueOrDefault(group.Key);
            var result = DetectTagDegradation(deviceId, group.Key, tagData, tagBaseline, config);

            if (result != null)
            {
                results.Add(result);
            }
        }

        return results;
    }

    /// <summary>
    /// 检测所有设备的劣化
    /// </summary>
    public async Task<IReadOnlyList<DegradationResult>> DetectAllDevicesDegradationAsync(
        CancellationToken ct = default)
    {
        var devices = await _deviceRepo.ListAsync(ct);
        var allResults = new List<DegradationResult>();

        foreach (var device in devices.Where(d => d.Enabled))
        {
            try
            {
                var results = await DetectDeviceDegradationAsync(device.DeviceId, ct);
                allResults.AddRange(results);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to detect degradation for device {DeviceId}",
                    device.DeviceId);
            }
        }

        return allResults;
    }

    /// <summary>
    /// 检测单个标签的劣化
    /// </summary>
    private DegradationResult? DetectTagDegradation(
        string deviceId,
        string tagId,
        List<TelemetryPoint> data,
        TagBaseline? baseline,
        DegradationConfig config)
    {
        var values = ExtractValues(data);
        if (values.Count < 50) return null;

        // 应用移动平均滤波
        var smoothed = MovingAverage(values, config.NoiseFilterWindowHours);

        // 将数据分成多个时间段进行比较
        int segmentCount = Math.Min(config.ConfirmationCount + 1, 5);
        var segments = SplitIntoSegments(smoothed, segmentCount);

        if (segments.Count < 2) return null;

        // 分析各段的统计特征
        var segmentStats = segments.Select(s => (Mean: s.Average(), StdDev: CalculateStdDev(s))).ToList();

        // 检测渐进变化
        var degradationType = DetectDegradationType(segmentStats, baseline, config);

        if (degradationType == DegradationType.None)
        {
            return null;
        }

        // 计算劣化速率
        double startValue = segmentStats.First().Mean;
        double endValue = segmentStats.Last().Mean;
        double changePercent = startValue != 0
            ? (endValue - startValue) / Math.Abs(startValue) * 100
            : 0;

        double daysSpan = config.DetectionWindowDays;
        double dailyRate = changePercent / daysSpan;

        // 只有超过阈值才报告
        if (Math.Abs(dailyRate) < config.DegradationRateThreshold &&
            degradationType != DegradationType.IncreasingVariance)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        return new DegradationResult
        {
            DeviceId = deviceId,
            TagId = tagId,
            Timestamp = now,
            IsDegrading = true,
            DegradationRate = dailyRate,
            DegradationType = degradationType,
            StartValue = startValue,
            CurrentValue = endValue,
            ChangePercent = changePercent,
            Description = GenerateDescription(tagId, degradationType, changePercent, dailyRate)
        };
    }

    /// <summary>
    /// 检测劣化类型
    /// </summary>
    private DegradationType DetectDegradationType(
        List<(double Mean, double StdDev)> segmentStats,
        TagBaseline? baseline,
        DegradationConfig config)
    {
        int n = segmentStats.Count;
        if (n < 2) return DegradationType.None;

        // 检查均值变化趋势
        int increasingCount = 0;
        int decreasingCount = 0;

        for (int i = 1; i < n; i++)
        {
            double prevMean = segmentStats[i - 1].Mean;
            double currMean = segmentStats[i].Mean;
            double diff = currMean - prevMean;

            if (Math.Abs(diff) > Math.Abs(prevMean) * 0.01) // 超过1%变化
            {
                if (diff > 0) increasingCount++;
                else decreasingCount++;
            }
        }

        // 检查是否持续上升或下降
        if (increasingCount >= config.ConfirmationCount)
        {
            return DegradationType.GradualIncrease;
        }

        if (decreasingCount >= config.ConfirmationCount)
        {
            return DegradationType.GradualDecrease;
        }

        // 检查方差变化（波动增大）
        int varianceIncreasing = 0;
        for (int i = 1; i < n; i++)
        {
            double prevStd = segmentStats[i - 1].StdDev;
            double currStd = segmentStats[i].StdDev;

            if (currStd > prevStd * 1.2) // 方差增加20%以上
            {
                varianceIncreasing++;
            }
        }

        if (varianceIncreasing >= config.ConfirmationCount - 1)
        {
            return DegradationType.IncreasingVariance;
        }

        return DegradationType.None;
    }

    /// <summary>
    /// 提取数值
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
    /// 移动平均滤波
    /// </summary>
    private List<double> MovingAverage(List<double> values, int windowHours)
    {
        // 假设每小时约有一定数量的数据点
        int windowSize = Math.Max(1, values.Count / 24 * windowHours);
        if (windowSize >= values.Count) return values;

        var result = new List<double>();

        for (int i = 0; i < values.Count; i++)
        {
            int start = Math.Max(0, i - windowSize / 2);
            int end = Math.Min(values.Count, i + windowSize / 2 + 1);

            double avg = 0;
            for (int j = start; j < end; j++)
            {
                avg += values[j];
            }
            avg /= (end - start);
            result.Add(avg);
        }

        return result;
    }

    /// <summary>
    /// 将数据分成多个段
    /// </summary>
    private List<List<double>> SplitIntoSegments(List<double> values, int segmentCount)
    {
        var segments = new List<List<double>>();
        int segmentSize = values.Count / segmentCount;

        if (segmentSize < 10) return segments;

        for (int i = 0; i < segmentCount; i++)
        {
            int start = i * segmentSize;
            int end = (i == segmentCount - 1) ? values.Count : (i + 1) * segmentSize;

            segments.Add(values.GetRange(start, end - start));
        }

        return segments;
    }

    /// <summary>
    /// 计算标准差
    /// </summary>
    private double CalculateStdDev(List<double> values)
    {
        if (values.Count < 2) return 0;
        double mean = values.Average();
        double sumSq = values.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sumSq / values.Count);
    }

    /// <summary>
    /// 生成劣化描述
    /// </summary>
    private string GenerateDescription(
        string tagId,
        DegradationType type,
        double changePercent,
        double dailyRate)
    {
        return type switch
        {
            DegradationType.GradualIncrease =>
                $"{tagId} 持续上升，累计变化 {changePercent:F1}%，日均上升 {dailyRate:F2}%",
            DegradationType.GradualDecrease =>
                $"{tagId} 持续下降，累计变化 {changePercent:F1}%，日均下降 {Math.Abs(dailyRate):F2}%",
            DegradationType.IncreasingVariance =>
                $"{tagId} 波动增大，稳定性下降",
            DegradationType.CycleAnomaly =>
                $"{tagId} 周期特征异常",
            _ => $"{tagId} 检测到劣化趋势"
        };
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
/// v63: 劣化检测服务接口
/// </summary>
public interface IDegradationDetectionService
{
    /// <summary>检测单个设备的劣化</summary>
    Task<IReadOnlyList<DegradationResult>> DetectDeviceDegradationAsync(
        string deviceId, CancellationToken ct = default);

    /// <summary>检测所有设备的劣化</summary>
    Task<IReadOnlyList<DegradationResult>> DetectAllDevicesDegradationAsync(
        CancellationToken ct = default);
}
