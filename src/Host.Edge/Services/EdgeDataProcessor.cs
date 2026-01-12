using System.Collections.Concurrent;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Host.Edge.Services;

/// <summary>
/// v65: Edge 数据预处理器 - 死区过滤 + 采样控制 + 异常检测
/// </summary>
public sealed class EdgeDataProcessor
{
    private readonly ConfigSyncService _configSync;
    private readonly ILogger<EdgeDataProcessor> _logger;

    // 每个标签的状态跟踪
    private readonly ConcurrentDictionary<string, TagState> _tagStates = new();

    // 统计信息
    private long _totalReceived;
    private long _totalFiltered;
    private long _totalOutliers;

    public EdgeDataProcessor(
        ConfigSyncService configSync,
        ILogger<EdgeDataProcessor> logger)
    {
        _configSync = configSync;
        _logger = logger;
    }

    /// <summary>
    /// 处理数据点，返回需要发送的点
    /// </summary>
    public IReadOnlyList<TelemetryPoint> Process(IEnumerable<TelemetryPoint> points)
    {
        var result = new List<TelemetryPoint>();
        var config = _configSync.ProcessingConfig;

        if (config == null || !config.Enabled)
        {
            return points.ToList();
        }

        foreach (var point in points)
        {
            Interlocked.Increment(ref _totalReceived);

            var processed = ProcessPoint(point, config);
            if (processed != null)
            {
                result.Add(processed);
            }
            else
            {
                Interlocked.Increment(ref _totalFiltered);
            }
        }

        return result;
    }

    /// <summary>
    /// 处理单个数据点
    /// </summary>
    private TelemetryPoint? ProcessPoint(TelemetryPoint point, ProcessingConfigDto config)
    {
        var tagKey = $"{point.DeviceId}:{point.TagId}";
        var state = _tagStates.GetOrAdd(tagKey, _ => new TagState());
        var tagConfig = _configSync.GetTagConfig(point.TagId);

        // 如果标签配置为绕过，直接返回
        if (tagConfig?.Bypass == true)
        {
            state.LastValue = GetNumericValue(point);
            state.LastTimestamp = point.Timestamp;
            return point;
        }

        // 获取有效的配置值（标签级优先于全局）
        var deadband = tagConfig?.Deadband ?? config.DefaultDeadband;
        var deadbandPercent = tagConfig?.DeadbandPercent ?? config.DefaultDeadbandPercent;
        var minIntervalMs = tagConfig?.MinIntervalMs ?? config.DefaultMinIntervalMs;
        var forceIntervalMs = config.ForceUploadIntervalMs;

        var currentValue = GetNumericValue(point);
        if (currentValue == null)
        {
            // 非数值类型，直接发送
            return point;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 1. 强制上传检查（超过强制间隔必须发送）
        var timeSinceLastSend = now - state.LastSentTimestamp;
        if (timeSinceLastSend >= forceIntervalMs)
        {
            UpdateState(state, currentValue.Value, now, true);
            return point;
        }

        // 2. 最小间隔检查
        if (timeSinceLastSend < minIntervalMs)
        {
            return null;
        }

        // 3. 异常检测
        if (config.OutlierEnabled && state.SampleCount > 10)
        {
            var zscore = CalculateZScore(currentValue.Value, state);
            if (Math.Abs(zscore) > config.OutlierSigmaThreshold)
            {
                Interlocked.Increment(ref _totalOutliers);

                switch (config.OutlierAction)
                {
                    case "Drop":
                        _logger.LogWarning("Outlier dropped: {Tag} value={Value} zscore={ZScore}",
                            tagKey, currentValue.Value, zscore);
                        return null;

                    case "Mark":
                        // 标记为可疑数据质量
                        var marked = point with { Quality = 0xC0 + 0x40 }; // OPC Uncertain
                        UpdateState(state, currentValue.Value, now, true);
                        return marked;

                    // "Pass" - 继续处理
                }
            }
        }

        // 4. 死区过滤
        if (state.LastSentValue.HasValue)
        {
            var change = Math.Abs(currentValue.Value - state.LastSentValue.Value);
            var percentChange = state.LastSentValue.Value != 0
                ? change / Math.Abs(state.LastSentValue.Value) * 100
                : 100;

            // 变化不满足死区条件
            if (change < deadband && percentChange < deadbandPercent)
            {
                // 更新统计但不发送
                state.UpdateStats(currentValue.Value);
                state.LastValue = currentValue.Value;
                state.LastTimestamp = now;
                return null;
            }
        }

        // 通过所有检查，发送数据
        UpdateState(state, currentValue.Value, now, true);
        return point;
    }

    /// <summary>
    /// 更新标签状态
    /// </summary>
    private void UpdateState(TagState state, double value, long timestamp, bool sent)
    {
        state.UpdateStats(value);
        state.LastValue = value;
        state.LastTimestamp = timestamp;

        if (sent)
        {
            state.LastSentValue = value;
            state.LastSentTimestamp = timestamp;
        }
    }

    /// <summary>
    /// 获取数值型值
    /// </summary>
    private static double? GetNumericValue(TelemetryPoint point)
    {
        return point.ValueType switch
        {
            DataType.Float32 => point.Float32Value,
            DataType.Float64 => point.Float64Value,
            DataType.Int32 => point.Int32Value,
            DataType.Int64 => point.Int64Value,
            DataType.Int16 => point.Int16Value,
            DataType.UInt16 => point.UInt16Value,
            DataType.UInt32 => point.UInt32Value,
            DataType.Int8 => point.Int8Value,
            DataType.UInt8 => point.UInt8Value,
            _ => null
        };
    }

    /// <summary>
    /// 计算 Z-Score
    /// </summary>
    private static double CalculateZScore(double value, TagState state)
    {
        if (state.StdDev < 0.0001) return 0;
        return (value - state.Mean) / state.StdDev;
    }

    /// <summary>
    /// 获取过滤率
    /// </summary>
    public double FilterRate
    {
        get
        {
            var total = Interlocked.Read(ref _totalReceived);
            if (total == 0) return 0;
            return (double)Interlocked.Read(ref _totalFiltered) / total * 100;
        }
    }

    /// <summary>
    /// 获取统计信息
    /// </summary>
    public ProcessorStats GetStats()
    {
        return new ProcessorStats
        {
            TotalReceived = Interlocked.Read(ref _totalReceived),
            TotalFiltered = Interlocked.Read(ref _totalFiltered),
            TotalOutliers = Interlocked.Read(ref _totalOutliers),
            FilterRate = FilterRate,
            TrackedTags = _tagStates.Count
        };
    }

    /// <summary>
    /// 重置统计
    /// </summary>
    public void ResetStats()
    {
        Interlocked.Exchange(ref _totalReceived, 0);
        Interlocked.Exchange(ref _totalFiltered, 0);
        Interlocked.Exchange(ref _totalOutliers, 0);
    }

    /// <summary>
    /// 标签状态跟踪
    /// </summary>
    private class TagState
    {
        public double? LastValue { get; set; }
        public double? LastSentValue { get; set; }
        public long LastTimestamp { get; set; }
        public long LastSentTimestamp { get; set; }

        // Welford 在线算法计算均值和标准差
        public int SampleCount { get; private set; }
        public double Mean { get; private set; }
        public double M2 { get; private set; }
        public double StdDev => SampleCount > 1 ? Math.Sqrt(M2 / (SampleCount - 1)) : 0;

        public void UpdateStats(double value)
        {
            SampleCount++;
            var delta = value - Mean;
            Mean += delta / SampleCount;
            var delta2 = value - Mean;
            M2 += delta * delta2;
        }
    }
}

/// <summary>
/// 处理器统计信息
/// </summary>
public class ProcessorStats
{
    public long TotalReceived { get; set; }
    public long TotalFiltered { get; set; }
    public long TotalOutliers { get; set; }
    public double FilterRate { get; set; }
    public int TrackedTags { get; set; }
}
