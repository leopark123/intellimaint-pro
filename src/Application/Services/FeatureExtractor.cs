using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Application.Services;

/// <summary>
/// v45: 特征提取服务实现
/// 从遥测数据中提取统计特征用于健康评估
/// </summary>
public sealed class FeatureExtractor : IFeatureExtractor
{
    private readonly ITelemetryRepository _telemetryRepo;
    private readonly IDeviceRepository _deviceRepo;
    private readonly ILogger<FeatureExtractor> _logger;

    public FeatureExtractor(
        ITelemetryRepository telemetryRepo,
        IDeviceRepository deviceRepo,
        ILogger<FeatureExtractor> logger)
    {
        _telemetryRepo = telemetryRepo;
        _deviceRepo = deviceRepo;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DeviceFeatures?> ExtractAsync(string deviceId, int windowMinutes, CancellationToken ct)
    {
        var endTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var startTs = endTs - (windowMinutes * 60 * 1000L);

        // 查询时间窗口内的遥测数据
        var points = await _telemetryRepo.QuerySimpleAsync(
            deviceId, 
            null, 
            startTs, 
            endTs, 
            2000, // v56.2: 降低到 2000 提升性能
            ct);

        if (points.Count == 0)
        {
            _logger.LogDebug("No telemetry data for device {DeviceId} in last {Minutes} minutes", 
                deviceId, windowMinutes);
            return null;
        }

        // 按标签分组，过滤掉无效值
        var groupedByTag = points
            .Where(p => p.GetValue() != null)
            .GroupBy(p => p.TagId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var tagFeatures = new Dictionary<string, TagFeatures>();

        foreach (var kvp in groupedByTag)
        {
            var tagId = kvp.Key;
            var tagPoints = kvp.Value;
            
            var features = ExtractTagFeatures(tagId, tagPoints);
            if (features != null)
            {
                tagFeatures[tagId] = features;
            }
        }

        return new DeviceFeatures
        {
            DeviceId = deviceId,
            Timestamp = endTs,
            WindowMinutes = windowMinutes,
            SampleCount = points.Count,
            TagFeatures = tagFeatures
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DeviceFeatures>> ExtractAllAsync(int windowMinutes, CancellationToken ct)
    {
        var devices = await _deviceRepo.ListAsync(ct);
        var results = new List<DeviceFeatures>();

        foreach (var device in devices.Where(d => d.Enabled))
        {
            try
            {
                var features = await ExtractAsync(device.DeviceId, windowMinutes, ct);
                if (features != null)
                {
                    results.Add(features);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract features for device {DeviceId}", device.DeviceId);
            }
        }

        return results;
    }

    /// <summary>
    /// 提取单个标签的统计特征
    /// </summary>
    private TagFeatures? ExtractTagFeatures(string tagId, List<TelemetryPoint> points)
    {
        // 转换为数值数组
        var values = points
            .Select(p => ConvertToDouble(p.GetValue()))
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToArray();

        if (values.Length < 2)
        {
            return null; // 至少需要 2 个点才能计算
        }

        // 基础统计
        var mean = values.Average();
        var min = values.Min();
        var max = values.Max();
        var latest = values[^1];

        // 标准差
        var variance = values.Average(v => Math.Pow(v - mean, 2));
        var stdDev = Math.Sqrt(variance);

        // 变异系数
        var cv = mean != 0 ? stdDev / Math.Abs(mean) : 0;

        // 极差
        var range = max - min;

        // 趋势计算（简单线性回归）
        var (slope, direction) = CalculateTrend(values);

        return new TagFeatures
        {
            TagId = tagId,
            Count = values.Length,
            Mean = Math.Round(mean, 4),
            StdDev = Math.Round(stdDev, 4),
            Min = Math.Round(min, 4),
            Max = Math.Round(max, 4),
            Latest = Math.Round(latest, 4),
            TrendSlope = Math.Round(slope, 6),
            TrendDirection = direction,
            CoefficientOfVariation = Math.Round(cv, 4),
            Range = Math.Round(range, 4)
        };
    }

    /// <summary>
    /// 计算趋势（简单线性回归）
    /// </summary>
    private (double slope, int direction) CalculateTrend(double[] values)
    {
        int n = values.Length;
        if (n < 2) return (0, 0);

        // 使用索引作为 x 值
        double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;

        for (int i = 0; i < n; i++)
        {
            sumX += i;
            sumY += values[i];
            sumXY += i * values[i];
            sumX2 += i * i;
        }

        double denominator = n * sumX2 - sumX * sumX;
        if (Math.Abs(denominator) < 1e-10)
        {
            return (0, 0); // 防止除零
        }

        double slope = (n * sumXY - sumX * sumY) / denominator;

        // 判断趋势方向
        // 使用标准化斜率（相对于均值的百分比变化）
        double avgValue = sumY / n;
        double normalizedSlope = avgValue != 0 ? slope / Math.Abs(avgValue) : slope;

        int direction;
        if (normalizedSlope > 0.001) // 每个采样周期上升 0.1%
            direction = 1; // 上升
        else if (normalizedSlope < -0.001)
            direction = -1; // 下降
        else
            direction = 0; // 平稳

        return (slope, direction);
    }

    /// <summary>
    /// 将各种类型的值转换为 double
    /// </summary>
    private double? ConvertToDouble(object? value)
    {
        return value switch
        {
            null => null,
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            short s => s,
            ushort us => us,
            uint ui => ui,
            ulong ul => ul,
            sbyte sb => sb,
            byte b => b,
            decimal m => (double)m,
            bool bo => bo ? 1.0 : 0.0,
            string str when double.TryParse(str, out var result) => result,
            _ => null
        };
    }
}
