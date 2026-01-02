using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;

namespace IntelliMaint.Infrastructure.Pipeline;

/// <summary>
/// 数据库写入循环
/// 遵循技术宪法：单写者、批量写入
/// </summary>
public sealed class DbWriterLoop : BackgroundService
{
    private readonly ChannelReader<TelemetryPoint> _reader;
    private readonly ITelemetryRepository _repository;
    private readonly ILogger<DbWriterLoop> _logger;
    private readonly int _batchSize;
    private readonly int _flushMs;
    
    // 统计
    private long _totalWritten;
    private long _totalBatches;
    private double _lastWriteMs;
    private readonly Stopwatch _latencyStopwatch = new();
    private readonly List<double> _latencySamples = new(100);
    
    public DbWriterLoop(
        ChannelReader<TelemetryPoint> reader,
        ITelemetryRepository repository,
        IOptions<EdgeOptions> options,
        ILogger<DbWriterLoop> logger)
    {
        _reader = reader;
        _repository = repository;
        _logger = logger;
        _batchSize = options.Value.WriterBatchSize;
        _flushMs = options.Value.WriterFlushMs;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DbWriterLoop started. BatchSize={BatchSize}, FlushMs={FlushMs}", 
            _batchSize, _flushMs);
        
        var batch = new List<TelemetryPoint>(_batchSize);
        var flushTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(_flushMs));
        
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // 收集批次
                batch.Clear();
                var deadline = DateTime.UtcNow.AddMilliseconds(_flushMs);
                
                while (batch.Count < _batchSize && DateTime.UtcNow < deadline)
                {
                    // 尝试读取，带超时
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero) break;
                    cts.CancelAfter(remaining);
                    
                    try
                    {
                        if (await _reader.WaitToReadAsync(cts.Token))
                        {
                            while (batch.Count < _batchSize && _reader.TryRead(out var point))
                            {
                                batch.Add(point);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // 超时或停止信号，刷新当前批次
                        break;
                    }
                }
                
                // 写入批次
                if (batch.Count > 0)
                {
                    // 如果正在关闭，使用独立 token 确保最后的数据能写入
                    var writeToken = stoppingToken.IsCancellationRequested 
                        ? CancellationToken.None 
                        : stoppingToken;
                    await WriteBatchAsync(batch, writeToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("DbWriterLoop stopping...");
            
            // 写入循环退出时可能还有未写入的数据
            if (batch.Count > 0)
            {
                _logger.LogDebug("Writing final batch of {Count} points before shutdown", batch.Count);
                await WriteBatchAsync(batch, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DbWriterLoop error");
            throw;
        }
        finally
        {
            // 刷新 Channel 中剩余数据（关闭时调用，不使用 CancellationToken）
            await DrainAsync();
            
            _logger.LogInformation("DbWriterLoop stopped. Total written: {Total}, Batches: {Batches}",
                _totalWritten, _totalBatches);
        }
    }
    
    /// <summary>
    /// 写入批次到数据库
    /// </summary>
    private async Task WriteBatchAsync(List<TelemetryPoint> batch, CancellationToken ct)
    {
        _latencyStopwatch.Restart();
        
        try
        {
            var written = await _repository.AppendBatchAsync(batch, ct);
            
            _latencyStopwatch.Stop();
            _lastWriteMs = _latencyStopwatch.Elapsed.TotalMilliseconds;
            
            Interlocked.Add(ref _totalWritten, written);
            Interlocked.Increment(ref _totalBatches);
            
            // 记录延迟样本
            lock (_latencySamples)
            {
                if (_latencySamples.Count >= 100)
                    _latencySamples.RemoveAt(0);
                _latencySamples.Add(_lastWriteMs);
            }
            
            _logger.LogDebug("Wrote {Count} points in {Ms:F2}ms", written, _lastWriteMs);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning("Write batch cancelled, {Count} points not written", batch.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write batch of {Count} points", batch.Count);
            // 不重试，数据已在溢出导出中处理
        }
    }
    
    /// <summary>
    /// 排空剩余数据（关闭时调用，不使用 CancellationToken）
    /// </summary>
    private async Task DrainAsync()
    {
        var batch = new List<TelemetryPoint>(_batchSize);
        
        while (_reader.TryRead(out var point))
        {
            batch.Add(point);
            
            if (batch.Count >= _batchSize)
            {
                await WriteBatchAsync(batch, CancellationToken.None);
                batch.Clear();
            }
        }
        
        if (batch.Count > 0)
        {
            await WriteBatchAsync(batch, CancellationToken.None);
        }
        
        _logger.LogInformation("Drained remaining data");
    }
    
    /// <summary>
    /// 获取写入延迟 P95
    /// </summary>
    public double GetLatencyP95()
    {
        lock (_latencySamples)
        {
            if (_latencySamples.Count == 0) return 0;
            
            var sorted = _latencySamples.OrderBy(x => x).ToList();
            var index = (int)(sorted.Count * 0.95);
            return sorted[Math.Min(index, sorted.Count - 1)];
        }
    }
    
    /// <summary>
    /// 获取统计
    /// </summary>
    public (long TotalWritten, long TotalBatches, double LastWriteMs, double P95Ms) GetStats()
    {
        return (
            Interlocked.Read(ref _totalWritten),
            Interlocked.Read(ref _totalBatches),
            _lastWriteMs,
            GetLatencyP95()
        );
    }
}
