using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IntelliMaint.Application.Services;

/// <summary>
/// v62: 动态基线增量更新服务
/// 支持基线自动调整、异常数据过滤、老化衰减
/// </summary>
public sealed class DynamicBaselineService
{
    private readonly IHealthBaselineRepository _baselineRepo;
    private readonly ITelemetryRepository _telemetryRepo;
    private readonly IDeviceRepository _deviceRepo;
    private readonly HealthAssessmentOptions _options;
    private readonly ILogger<DynamicBaselineService> _logger;

    public DynamicBaselineService(
        IHealthBaselineRepository baselineRepo,
        ITelemetryRepository telemetryRepo,
        IDeviceRepository deviceRepo,
        IOptions<HealthAssessmentOptions> options,
        ILogger<DynamicBaselineService> logger)
    {
        _baselineRepo = baselineRepo;
        _telemetryRepo = telemetryRepo;
        _deviceRepo = deviceRepo;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// 增量更新所有设备的基线
    /// </summary>
    public async Task UpdateAllBaselinesAsync(CancellationToken ct = default)
    {
        var config = _options.DynamicBaseline;
        if (!config.Enabled)
        {
            _logger.LogDebug("Dynamic baseline update is disabled");
            return;
        }

        var devices = await _deviceRepo.ListAsync(ct);
        var baselines = await _baselineRepo.ListAsync(ct);
        var baselineDict = baselines.ToDictionary(b => b.DeviceId);

        int updatedCount = 0;
        foreach (var device in devices)
        {
            if (!device.Enabled) continue;

            try
            {
                var updated = await UpdateDeviceBaselineAsync(
                    device.DeviceId,
                    baselineDict.GetValueOrDefault(device.DeviceId),
                    config,
                    ct);

                if (updated) updatedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update baseline for device {DeviceId}", device.DeviceId);
            }
        }

        _logger.LogInformation("Dynamic baseline update completed: {Updated}/{Total} devices updated",
            updatedCount, devices.Count);
    }

    /// <summary>
    /// 增量更新单个设备的基线
    /// </summary>
    public async Task<bool> UpdateDeviceBaselineAsync(
        string deviceId,
        DeviceBaseline? existingBaseline,
        DynamicBaselineConfig config,
        CancellationToken ct)
    {
        // 检查是否需要更新（基于更新间隔）
        if (existingBaseline != null)
        {
            var hoursSinceUpdate = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - existingBaseline.UpdatedUtc)
                / 3600000.0;
            if (hoursSinceUpdate < config.UpdateIntervalHours)
            {
                _logger.LogDebug("Baseline for {DeviceId} was updated {Hours:F1} hours ago, skipping",
                    deviceId, hoursSinceUpdate);
                return false;
            }
        }

        // 获取最近的遥测数据（使用更新间隔作为时间窗口）
        var endTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var startTs = endTs - config.UpdateIntervalHours * 3600 * 1000L;

        var recentData = await _telemetryRepo.QuerySimpleAsync(
            deviceId, null, startTs, endTs, 10000, ct);

        if (recentData.Count < config.MinSampleCount)
        {
            _logger.LogDebug("Not enough samples for {DeviceId}: {Count} < {Min}",
                deviceId, recentData.Count, config.MinSampleCount);
            return false;
        }

        // 按标签分组数据
        var tagData = recentData
            .GroupBy(p => p.TagId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // 计算新的标签基线
        var newTagBaselines = new Dictionary<string, TagBaseline>();
        var existingTagBaselines = existingBaseline?.TagBaselines ?? new Dictionary<string, TagBaseline>();

        foreach (var (tagId, points) in tagData)
        {
            if (points.Count < 10) continue;

            var values = ExtractNumericValues(points);
            if (values.Count < 10) continue;

            // 过滤异常数据
            var filteredValues = FilterAnomalies(
                values,
                existingTagBaselines.GetValueOrDefault(tagId),
                config.AnomalyFilterThreshold);

            if (filteredValues.Count < config.MinSampleCount / 10)
            {
                _logger.LogDebug("Too few valid samples for tag {TagId} after filtering", tagId);
                continue;
            }

            // 计算新数据的统计量
            var newMean = filteredValues.Average();
            var newStdDev = CalculateStdDev(filteredValues, newMean);
            var newMin = filteredValues.Min();
            var newMax = filteredValues.Max();
            var newCV = Math.Abs(newMean) > 0.0001 ? newStdDev / Math.Abs(newMean) : 0;

            // 增量更新（加权平均）
            if (existingTagBaselines.TryGetValue(tagId, out var existing))
            {
                // 应用老化因子
                var daysSinceCreation = (endTs - existingBaseline!.CreatedUtc) / (24.0 * 3600 * 1000);
                var agingFactor = Math.Max(1.0 - daysSinceCreation * config.AgingFactor, 0.5);

                // 增量权重（新数据占比）
                var newWeight = config.IncrementalWeight;
                var oldWeight = (1 - newWeight) * agingFactor;
                var totalWeight = newWeight + oldWeight;

                newTagBaselines[tagId] = new TagBaseline
                {
                    TagId = tagId,
                    NormalMean = (existing.NormalMean * oldWeight + newMean * newWeight) / totalWeight,
                    NormalStdDev = (existing.NormalStdDev * oldWeight + newStdDev * newWeight) / totalWeight,
                    NormalMin = Math.Min(existing.NormalMin, newMin),
                    NormalMax = Math.Max(existing.NormalMax, newMax),
                    NormalCV = (existing.NormalCV * oldWeight + newCV * newWeight) / totalWeight
                };
            }
            else
            {
                // 新标签，直接使用新数据
                newTagBaselines[tagId] = new TagBaseline
                {
                    TagId = tagId,
                    NormalMean = newMean,
                    NormalStdDev = newStdDev,
                    NormalMin = newMin,
                    NormalMax = newMax,
                    NormalCV = newCV
                };
            }
        }

        if (newTagBaselines.Count == 0)
        {
            _logger.LogDebug("No valid tag baselines computed for {DeviceId}", deviceId);
            return false;
        }

        // 保留未更新的旧标签基线（如果存在）
        foreach (var (tagId, baseline) in existingTagBaselines)
        {
            if (!newTagBaselines.ContainsKey(tagId))
            {
                newTagBaselines[tagId] = baseline;
            }
        }

        // 保存更新后的基线
        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var updatedBaseline = new DeviceBaseline
        {
            DeviceId = deviceId,
            CreatedUtc = existingBaseline?.CreatedUtc ?? nowUtc,
            UpdatedUtc = nowUtc,
            SampleCount = (existingBaseline?.SampleCount ?? 0) + recentData.Count,
            LearningHours = (existingBaseline?.LearningHours ?? 0) + config.UpdateIntervalHours,
            TagBaselines = newTagBaselines
        };

        await _baselineRepo.SaveAsync(updatedBaseline, ct);

        _logger.LogInformation(
            "Baseline updated for {DeviceId}: {TagCount} tags, {Samples} new samples, total {TotalSamples}",
            deviceId, newTagBaselines.Count, recentData.Count, updatedBaseline.SampleCount);

        return true;
    }

    /// <summary>
    /// 过滤异常数据点
    /// </summary>
    private List<double> FilterAnomalies(
        List<double> values,
        TagBaseline? existingBaseline,
        double zScoreThreshold)
    {
        if (existingBaseline == null || existingBaseline.NormalStdDev < 0.0001)
        {
            // 无基线时，使用当前数据的统计量过滤极端值
            var mean = values.Average();
            var stdDev = CalculateStdDev(values, mean);

            if (stdDev < 0.0001)
                return values;

            return values
                .Where(v => Math.Abs(v - mean) / stdDev <= zScoreThreshold)
                .ToList();
        }

        // 使用现有基线过滤
        return values
            .Where(v => Math.Abs(v - existingBaseline.NormalMean) / existingBaseline.NormalStdDev <= zScoreThreshold)
            .ToList();
    }

    /// <summary>
    /// 提取数值列表
    /// </summary>
    private List<double> ExtractNumericValues(List<TelemetryPoint> points)
    {
        var values = new List<double>(points.Count);
        foreach (var point in points)
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

    private double CalculateStdDev(List<double> values, double mean)
    {
        if (values.Count < 2) return 0;
        var sumSq = values.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sumSq / values.Count);
    }
}

/// <summary>
/// v62: 扩展 HealthAssessmentOptions 以包含动态基线配置
/// </summary>
public static class HealthAssessmentOptionsExtensions
{
    /// <summary>
    /// 获取动态基线配置（如果未配置则使用默认值）
    /// </summary>
    public static DynamicBaselineConfig GetDynamicBaselineConfig(this HealthAssessmentOptions options)
    {
        return options.DynamicBaseline ?? new DynamicBaselineConfig();
    }
}
