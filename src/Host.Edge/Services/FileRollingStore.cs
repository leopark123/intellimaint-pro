using System.IO.Compression;
using System.Text.Json;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IntelliMaint.Host.Edge.Services;

/// <summary>
/// v65: 文件滚动存储 - 用于断网续传
/// 不依赖 SQLite，使用压缩文件存储
/// </summary>
public sealed class FileRollingStore : IDisposable
{
    private readonly string _baseDir;
    private readonly int _maxSizeMB;
    private readonly int _retentionDays;
    private readonly bool _compressionEnabled;
    private readonly ILogger<FileRollingStore> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private const string IndexFile = "index.json";
    private const string BatchPrefix = "batch_";
    private const string BatchSuffix = ".bin.gz";

    private long _nextBatchId;
    private readonly Queue<BatchInfo> _pendingBatches = new();

    public FileRollingStore(
        IOptions<StoreForwardOptions> options,
        ILogger<FileRollingStore> logger)
    {
        _baseDir = Path.Combine(AppContext.BaseDirectory, "data", "outbox");
        _maxSizeMB = options.Value.MaxStoreSizeMB;
        _retentionDays = options.Value.RetentionDays;
        _compressionEnabled = options.Value.CompressionEnabled;
        _logger = logger;

        Directory.CreateDirectory(_baseDir);
        LoadIndex();

        _logger.LogInformation("FileRollingStore initialized: {Path}, MaxSize={Size}MB, Retention={Days}days",
            _baseDir, _maxSizeMB, _retentionDays);
    }

    /// <summary>
    /// 存储批量数据
    /// </summary>
    public async Task StoreAsync(IReadOnlyList<TelemetryPoint> points, CancellationToken ct)
    {
        if (points.Count == 0) return;

        await _writeLock.WaitAsync(ct);
        try
        {
            var batchId = Interlocked.Increment(ref _nextBatchId);
            var fileName = $"{BatchPrefix}{batchId:D10}{BatchSuffix}";
            var filePath = Path.Combine(_baseDir, fileName);

            // 序列化并压缩
            var json = JsonSerializer.SerializeToUtf8Bytes(points);
            if (_compressionEnabled)
            {
                await using var fs = new FileStream(filePath, FileMode.Create);
                await using var gzip = new GZipStream(fs, CompressionLevel.Fastest);
                await gzip.WriteAsync(json, ct);
            }
            else
            {
                await File.WriteAllBytesAsync(filePath.Replace(".gz", ""), json, ct);
            }

            var fileSize = new FileInfo(filePath).Length;
            var batchInfo = new BatchInfo
            {
                Id = batchId,
                FileName = fileName,
                PointCount = points.Count,
                SizeBytes = fileSize,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            _pendingBatches.Enqueue(batchInfo);
            await SaveIndexAsync(ct);

            _logger.LogDebug("Stored batch {Id}: {Count} points, {Size} bytes",
                batchId, points.Count, fileSize);

            // 检查容量限制
            await EnforceCapacityLimitAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// 读取下一个待发送批次
    /// </summary>
    public async Task<OutboxBatch?> ReadBatchAsync(int limit, CancellationToken ct)
    {
        if (_pendingBatches.Count == 0) return null;

        var batchInfo = _pendingBatches.Peek();
        var filePath = Path.Combine(_baseDir, batchInfo.FileName);

        if (!File.Exists(filePath))
        {
            _pendingBatches.Dequeue();
            await SaveIndexAsync(ct);
            return await ReadBatchAsync(limit, ct);
        }

        List<TelemetryPoint>? points;
        if (_compressionEnabled && batchInfo.FileName.EndsWith(".gz"))
        {
            await using var fs = new FileStream(filePath, FileMode.Open);
            await using var gzip = new GZipStream(fs, CompressionMode.Decompress);
            using var ms = new MemoryStream();
            await gzip.CopyToAsync(ms, ct);
            points = JsonSerializer.Deserialize<List<TelemetryPoint>>(ms.ToArray());
        }
        else
        {
            var data = await File.ReadAllBytesAsync(filePath, ct);
            points = JsonSerializer.Deserialize<List<TelemetryPoint>>(data);
        }

        return new OutboxBatch
        {
            Id = batchInfo.Id,
            Points = points ?? new(),
            CreatedAt = batchInfo.CreatedAt
        };
    }

    /// <summary>
    /// 确认批次已发送
    /// </summary>
    public async Task AcknowledgeAsync(long batchId, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            if (_pendingBatches.Count > 0 && _pendingBatches.Peek().Id == batchId)
            {
                var batch = _pendingBatches.Dequeue();
                var filePath = Path.Combine(_baseDir, batch.FileName);

                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                await SaveIndexAsync(ct);
                _logger.LogDebug("Acknowledged and deleted batch {Id}", batchId);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// 检查是否有待发送数据
    /// </summary>
    public bool HasData => _pendingBatches.Count > 0;

    /// <summary>
    /// 获取待发送数据点数量
    /// </summary>
    public int PendingPointCount => _pendingBatches.Sum(b => b.PointCount);

    /// <summary>
    /// 获取存储大小（MB）
    /// </summary>
    public double StoredSizeMB => _pendingBatches.Sum(b => b.SizeBytes) / (1024.0 * 1024.0);

    /// <summary>
    /// 清理过期数据
    /// </summary>
    public async Task CleanupAsync(CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-_retentionDays).ToUnixTimeMilliseconds();
            var expired = _pendingBatches.Where(b => b.CreatedAt < cutoff).ToList();

            foreach (var batch in expired)
            {
                var filePath = Path.Combine(_baseDir, batch.FileName);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }

            if (expired.Count > 0)
            {
                // 重建队列
                var remaining = _pendingBatches.Where(b => b.CreatedAt >= cutoff).ToList();
                _pendingBatches.Clear();
                foreach (var b in remaining)
                {
                    _pendingBatches.Enqueue(b);
                }
                await SaveIndexAsync(ct);
                _logger.LogInformation("Cleaned up {Count} expired batches", expired.Count);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// 获取存储统计信息
    /// </summary>
    public OutboxStats GetStats()
    {
        return new OutboxStats
        {
            PendingBatches = _pendingBatches.Count,
            PendingPoints = _pendingBatches.Sum(b => b.PointCount),
            TotalStoredBytes = _pendingBatches.Sum(b => b.SizeBytes)
        };
    }

    private async Task EnforceCapacityLimitAsync(CancellationToken ct)
    {
        var totalSizeMB = _pendingBatches.Sum(b => b.SizeBytes) / (1024.0 * 1024.0);

        while (totalSizeMB > _maxSizeMB && _pendingBatches.Count > 0)
        {
            var oldest = _pendingBatches.Dequeue();
            var filePath = Path.Combine(_baseDir, oldest.FileName);

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            totalSizeMB -= oldest.SizeBytes / (1024.0 * 1024.0);
            _logger.LogWarning("Capacity limit reached, deleted oldest batch {Id}", oldest.Id);
        }

        await SaveIndexAsync(ct);
    }

    private void LoadIndex()
    {
        var indexPath = Path.Combine(_baseDir, IndexFile);
        if (!File.Exists(indexPath)) return;

        try
        {
            var json = File.ReadAllText(indexPath);
            var index = JsonSerializer.Deserialize<OutboxIndex>(json);

            if (index != null)
            {
                _nextBatchId = index.NextBatchId;
                foreach (var batch in index.Batches.OrderBy(b => b.Id))
                {
                    _pendingBatches.Enqueue(batch);
                }
                _logger.LogInformation("Loaded {Count} pending batches from index", _pendingBatches.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load index, starting fresh");
        }
    }

    private async Task SaveIndexAsync(CancellationToken ct)
    {
        var index = new OutboxIndex
        {
            NextBatchId = _nextBatchId,
            Batches = _pendingBatches.ToList()
        };

        var json = JsonSerializer.Serialize(index);
        var indexPath = Path.Combine(_baseDir, IndexFile);
        await File.WriteAllTextAsync(indexPath, json, ct);
    }

    public void Dispose()
    {
        _writeLock.Dispose();
    }

    private class OutboxIndex
    {
        public long NextBatchId { get; set; }
        public List<BatchInfo> Batches { get; set; } = new();
    }

    private class BatchInfo
    {
        public long Id { get; set; }
        public string FileName { get; set; } = "";
        public int PointCount { get; set; }
        public long SizeBytes { get; set; }
        public long CreatedAt { get; set; }
    }
}

/// <summary>
/// Outbox 批次
/// </summary>
public class OutboxBatch
{
    public long Id { get; set; }
    public List<TelemetryPoint> Points { get; set; } = new();
    public long CreatedAt { get; set; }
}

/// <summary>
/// Outbox 统计信息
/// </summary>
public class OutboxStats
{
    public int PendingBatches { get; set; }
    public int PendingPoints { get; set; }
    public long TotalStoredBytes { get; set; }
}

/// <summary>
/// 断网续传配置选项
/// </summary>
public class StoreForwardOptions
{
    public const string SectionName = "StoreForward";

    public bool Enabled { get; set; } = true;
    public int MaxStoreSizeMB { get; set; } = 1000;
    public int RetentionDays { get; set; } = 7;
    public bool CompressionEnabled { get; set; } = true;
    public string CompressionAlgorithm { get; set; } = "Gzip";
}
