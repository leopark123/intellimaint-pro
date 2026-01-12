using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace IntelliMaint.Infrastructure.Pipeline;

/// <summary>
/// v56: 变化率告警用的滑动窗口数据结构
/// 线程安全，使用循环缓冲区限制内存使用
/// </summary>
public sealed class RocSlidingWindow
{
    // Key: "deviceId|tagId", Value: 数据点列表
    private readonly ConcurrentDictionary<string, WindowData> _windows = new();

    // 每个标签最大保留的数据点数
    private const int MaxPointsPerTag = 1000;

    // 最大窗口时间（毫秒）= 1小时
    private const long MaxWindowMs = 3600_000;

    /// <summary>
    /// 添加数据点到滑动窗口
    /// </summary>
    public void Add(string deviceId, string tagId, long ts, double value)
    {
        var key = MakeKey(deviceId, tagId);
        var windowData = _windows.GetOrAdd(key, _ => new WindowData(MaxPointsPerTag));
        windowData.Add(ts, value);
    }

    /// <summary>
    /// 获取窗口内的统计信息
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    /// <param name="tagId">标签ID</param>
    /// <param name="windowMs">时间窗口（毫秒）</param>
    /// <returns>统计信息，如果窗口内无数据则返回 null</returns>
    public WindowStats? GetWindowStats(string deviceId, string tagId, long windowMs)
    {
        var key = MakeKey(deviceId, tagId);
        if (!_windows.TryGetValue(key, out var windowData))
            return null;

        var cutoffTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - windowMs;
        return windowData.GetStats(cutoffTs);
    }

    /// <summary>
    /// 获取指定窗口内的变化率
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    /// <param name="tagId">标签ID</param>
    /// <param name="windowMs">时间窗口（毫秒）</param>
    /// <returns>变化率信息，如果窗口内数据不足则返回 null</returns>
    public RocResult? GetRateOfChange(string deviceId, string tagId, long windowMs)
    {
        var stats = GetWindowStats(deviceId, tagId, windowMs);
        if (stats == null || stats.Count < 2)
            return null;

        var absoluteChange = stats.Max - stats.Min;
        var baseline = stats.First;
        var percentChange = Math.Abs(baseline) > 1e-9
            ? Math.Abs(absoluteChange / baseline) * 100
            : 0;

        return new RocResult
        {
            AbsoluteChange = absoluteChange,
            PercentChange = percentChange,
            Min = stats.Min,
            Max = stats.Max,
            First = stats.First,
            Last = stats.Last,
            Count = stats.Count
        };
    }

    /// <summary>
    /// 清理所有过期数据
    /// </summary>
    public void CleanupExpired()
    {
        var cutoffTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - MaxWindowMs;
        foreach (var kvp in _windows)
        {
            kvp.Value.RemoveOlderThan(cutoffTs);
        }

        // 移除空窗口
        var emptyKeys = _windows
            .Where(kvp => kvp.Value.Count == 0)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in emptyKeys)
        {
            _windows.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// 获取当前追踪的标签数量
    /// </summary>
    public int TagCount => _windows.Count;

    /// <summary>
    /// 获取所有窗口的总数据点数
    /// </summary>
    public int TotalPointCount => _windows.Values.Sum(w => w.Count);

    private static string MakeKey(string deviceId, string tagId) => $"{deviceId}|{tagId}";
}

/// <summary>
/// 窗口统计信息
/// </summary>
public sealed class WindowStats
{
    public double Min { get; init; }
    public double Max { get; init; }
    public double First { get; init; }
    public double Last { get; init; }
    public double Avg { get; init; }
    public int Count { get; init; }
    public long OldestTs { get; init; }
    public long NewestTs { get; init; }
    /// <summary>
    /// v58: 标准差（波动告警用）
    /// </summary>
    public double StdDev { get; init; }
}

/// <summary>
/// 变化率计算结果
/// </summary>
public sealed class RocResult
{
    public double AbsoluteChange { get; init; }
    public double PercentChange { get; init; }
    public double Min { get; init; }
    public double Max { get; init; }
    public double First { get; init; }
    public double Last { get; init; }
    public int Count { get; init; }
}

/// <summary>
/// 内部窗口数据结构（循环缓冲区）
/// </summary>
internal sealed class WindowData
{
    private readonly object _lock = new();
    private readonly int _maxSize;
    private readonly List<(long Ts, double Value)> _data = new();

    public WindowData(int maxSize)
    {
        _maxSize = maxSize;
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _data.Count;
            }
        }
    }

    public void Add(long ts, double value)
    {
        lock (_lock)
        {
            // 添加新数据点
            _data.Add((ts, value));

            // 如果超过最大容量，移除最旧的
            while (_data.Count > _maxSize)
            {
                _data.RemoveAt(0);
            }
        }
    }

    public void RemoveOlderThan(long cutoffTs)
    {
        lock (_lock)
        {
            _data.RemoveAll(p => p.Ts < cutoffTs);
        }
    }

    public WindowStats? GetStats(long cutoffTs)
    {
        lock (_lock)
        {
            // 过滤出窗口内的数据
            var windowData = _data.Where(p => p.Ts >= cutoffTs).ToList();
            if (windowData.Count == 0)
                return null;

            // 按时间排序
            windowData.Sort((a, b) => a.Ts.CompareTo(b.Ts));

            var min = windowData.Min(p => p.Value);
            var max = windowData.Max(p => p.Value);
            var avg = windowData.Average(p => p.Value);
            var first = windowData[0].Value;
            var last = windowData[^1].Value;
            var oldestTs = windowData[0].Ts;
            var newestTs = windowData[^1].Ts;

            // v58: 计算标准差
            var stdDev = 0.0;
            if (windowData.Count > 1)
            {
                var sumOfSquares = windowData.Sum(p => (p.Value - avg) * (p.Value - avg));
                var variance = sumOfSquares / windowData.Count;
                stdDev = Math.Sqrt(variance);
            }

            return new WindowStats
            {
                Min = min,
                Max = max,
                Avg = avg,
                First = first,
                Last = last,
                Count = windowData.Count,
                OldestTs = oldestTs,
                NewestTs = newestTs,
                StdDev = stdDev
            };
        }
    }
}
