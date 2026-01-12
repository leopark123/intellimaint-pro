using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using IntelliMaint.Core.Contracts;
using IntelliMaint.Infrastructure.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.Pipeline;

/// <summary>
/// v56: 追踪每个标签的最后数据时间，用于离线检测
/// </summary>
public sealed class LastDataTracker : BackgroundService
{
    private readonly ChannelReader<TelemetryPoint> _reader;
    private readonly IDbExecutor? _db;
    private readonly ILogger<LastDataTracker> _logger;

    // Key: "deviceId|tagId", Value: 最后数据时间戳 (epoch ms)
    private readonly ConcurrentDictionary<string, long> _lastTsByTag = new();

    // 待持久化的变更
    private readonly ConcurrentDictionary<string, (string DeviceId, string TagId, long LastTs)> _pendingUpdates = new();

    // 持久化间隔（毫秒）
    private const int FlushIntervalMs = 5_000;

    public LastDataTracker(
        ChannelReader<TelemetryPoint> reader,
        IDbExecutor? db,
        ILogger<LastDataTracker> logger)
    {
        _reader = reader;
        _db = db;
        _logger = logger;
        if (_db == null)
        {
            _logger.LogInformation("LastDataTracker: IDbExecutor not available, persistence disabled");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LastDataTracker starting...");

        // 启动后台持久化任务
        _ = FlushLoopAsync(stoppingToken);

        try
        {
            await foreach (var point in _reader.ReadAllAsync(stoppingToken))
            {
                TrackPoint(point);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("LastDataTracker stopping...");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LastDataTracker crashed");
            throw;
        }
    }

    /// <summary>
    /// 追踪数据点的时间戳
    /// </summary>
    private void TrackPoint(TelemetryPoint point)
    {
        var key = MakeKey(point.DeviceId, point.TagId);
        var ts = point.Ts;

        // 更新内存缓存
        _lastTsByTag.AddOrUpdate(key, ts, (_, existing) => Math.Max(existing, ts));

        // 记录待持久化
        _pendingUpdates[key] = (point.DeviceId, point.TagId, ts);
    }

    /// <summary>
    /// 获取标签的最后数据时间
    /// </summary>
    public long? GetLastTs(string deviceId, string tagId)
    {
        var key = MakeKey(deviceId, tagId);
        return _lastTsByTag.TryGetValue(key, out var ts) ? ts : null;
    }

    /// <summary>
    /// 获取所有追踪的标签及其最后数据时间
    /// </summary>
    public IReadOnlyDictionary<string, long> GetAllLastTs()
    {
        return new Dictionary<string, long>(_lastTsByTag);
    }

    /// <summary>
    /// 后台循环：定期将变更写入数据库
    /// </summary>
    private async Task FlushLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(FlushIntervalMs, stoppingToken);
                await FlushAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // 正常退出，尝试最后一次刷新
                await FlushAsync(CancellationToken.None);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error flushing last data timestamps");
            }
        }
    }

    /// <summary>
    /// 将待持久化的变更写入数据库
    /// </summary>
    private async Task FlushAsync(CancellationToken ct)
    {
        if (_db == null || _pendingUpdates.IsEmpty)
            return;

        // 收集待更新的条目
        var updates = new List<(string DeviceId, string TagId, long LastTs)>();
        var keysToRemove = new List<string>();

        foreach (var kvp in _pendingUpdates)
        {
            updates.Add(kvp.Value);
            keysToRemove.Add(kvp.Key);
        }

        // 清除待更新列表
        foreach (var key in keysToRemove)
        {
            _pendingUpdates.TryRemove(key, out _);
        }

        if (updates.Count == 0)
            return;

        try
        {
            // 批量 UPSERT
            const string sql = @"
INSERT INTO tag_last_data (device_id, tag_id, last_ts, updated_utc)
VALUES (@DeviceId, @TagId, @LastTs, @UpdatedUtc)
ON CONFLICT(device_id, tag_id) DO UPDATE SET
    last_ts = MAX(tag_last_data.last_ts, @LastTs),
    updated_utc = @UpdatedUtc;";

            var updatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            foreach (var (deviceId, tagId, lastTs) in updates)
            {
                await _db.ExecuteNonQueryAsync(sql, new
                {
                    DeviceId = deviceId,
                    TagId = tagId,
                    LastTs = lastTs,
                    UpdatedUtc = updatedUtc
                }, ct);
            }

            _logger.LogDebug("Flushed {Count} last data timestamps to database", updates.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush {Count} last data timestamps", updates.Count);
            // 失败时重新添加到待更新列表
            foreach (var (deviceId, tagId, lastTs) in updates)
            {
                var key = MakeKey(deviceId, tagId);
                _pendingUpdates.TryAdd(key, (deviceId, tagId, lastTs));
            }
        }
    }

    private static string MakeKey(string deviceId, string tagId) => $"{deviceId}|{tagId}";
}
