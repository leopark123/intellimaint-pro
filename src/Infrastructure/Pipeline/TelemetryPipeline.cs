using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;

namespace IntelliMaint.Infrastructure.Pipeline;

/// <summary>
/// 遥测数据管道实现
/// 遵循技术宪法：有界队列 + DropOldest + 溢出导出
/// </summary>
public sealed class TelemetryPipeline : ITelemetryPipeline, IDisposable
{
    private readonly Channel<TelemetryPoint> _channel;
    private readonly IOverflowExporter? _overflowExporter;
    private readonly ILogger<TelemetryPipeline> _logger;
    private readonly int _capacity;
    
    // 统计
    private long _totalReceived;
    private long _totalWritten;
    private long _totalDropped;
    
    public long QueueDepth => _channel.Reader.Count;
    public long QueueCapacity => _capacity;
    
    public TelemetryPipeline(
        IOptions<ChannelCapacityOptions> options,
        ILogger<TelemetryPipeline> logger,
        IOverflowExporter? overflowExporter = null)
    {
        _capacity = options.Value.GlobalCapacity;
        _logger = logger;
        _overflowExporter = overflowExporter;
        
        // 创建有界 Channel
        // 注意：BoundedChannelFullMode.DropOldest 需要 SingleReader
        // 但我们需要多 Reader，所以使用 Wait 模式 + 手动 DropOldest
        _channel = Channel.CreateBounded<TelemetryPoint>(new BoundedChannelOptions(_capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = false
        });
        
        _logger.LogInformation("TelemetryPipeline initialized with capacity {Capacity}", _capacity);
    }
    
    /// <summary>
    /// 写入数据点
    /// 队列满时实现 DropOldest 策略
    /// </summary>
    public async ValueTask<bool> WriteAsync(TelemetryPoint point, CancellationToken ct)
    {
        Interlocked.Increment(ref _totalReceived);
        
        // 尝试直接写入
        if (_channel.Writer.TryWrite(point))
        {
            Interlocked.Increment(ref _totalWritten);
            return true;
        }
        
        // 队列满，执行 DropOldest
        await HandleBackpressureAsync(point, ct);
        return true;
    }
    
    /// <summary>
    /// 批量写入
    /// </summary>
    public async ValueTask<int> WriteBatchAsync(IReadOnlyList<TelemetryPoint> points, CancellationToken ct)
    {
        var written = 0;
        foreach (var point in points)
        {
            if (await WriteAsync(point, ct))
                written++;
        }
        return written;
    }
    
    /// <summary>
    /// 获取管道统计
    /// </summary>
    public PipelineStats GetStats()
    {
        return new PipelineStats
        {
            TotalReceived = Interlocked.Read(ref _totalReceived),
            TotalWritten = Interlocked.Read(ref _totalWritten),
            TotalDropped = Interlocked.Read(ref _totalDropped),
            CurrentQueueDepth = _channel.Reader.Count
        };
    }
    
    /// <summary>
    /// 获取 Channel Reader（供下游消费）
    /// </summary>
    public ChannelReader<TelemetryPoint> Reader => _channel.Reader;
    
    /// <summary>
    /// 获取 Channel Writer（供上游写入）
    /// </summary>
    public ChannelWriter<TelemetryPoint> Writer => _channel.Writer;
    
    /// <summary>
    /// 完成写入（优雅关闭时调用）
    /// </summary>
    public void Complete()
    {
        _channel.Writer.Complete();
    }
    
    /// <summary>
    /// 处理背压
    /// 丢弃最旧的数据点并导出到溢出文件
    /// </summary>
    private async ValueTask HandleBackpressureAsync(TelemetryPoint newPoint, CancellationToken ct)
    {
        // 尝试读取最旧的数据点
        if (_channel.Reader.TryRead(out var oldestPoint))
        {
            Interlocked.Increment(ref _totalDropped);
            
            // 导出被丢弃的数据
            if (_overflowExporter != null)
            {
                try
                {
                    await _overflowExporter.ExportAsync(oldestPoint, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to export dropped point");
                }
            }
            
            // 每 1000 次丢弃记录一次日志
            var dropped = Interlocked.Read(ref _totalDropped);
            if (dropped % 1000 == 0)
            {
                _logger.LogWarning("Pipeline backpressure: {Dropped} points dropped so far", dropped);
            }
        }
        
        // 重新尝试写入新数据点
        if (!_channel.Writer.TryWrite(newPoint))
        {
            // 仍然失败，使用 await 等待（带超时）
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMilliseconds(100));
            
            try
            {
                await _channel.Writer.WriteAsync(newPoint, cts.Token);
                Interlocked.Increment(ref _totalWritten);
            }
            catch (OperationCanceledException)
            {
                // 超时，丢弃新数据点
                Interlocked.Increment(ref _totalDropped);
                _logger.LogWarning("Pipeline timeout, new point dropped: {TagId}", newPoint.TagId);
            }
        }
        else
        {
            Interlocked.Increment(ref _totalWritten);
        }
    }
    
    public void Dispose()
    {
        _channel.Writer.TryComplete();
    }
}
