using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;

namespace IntelliMaint.Infrastructure.Pipeline;

/// <summary>
/// 遥测分发器
/// 从源 Channel 读取，分发到多个目标 Channel
/// </summary>
public sealed class TelemetryDispatcher : BackgroundService, ITelemetryDispatcher
{
    private readonly ChannelReader<TelemetryPoint> _source;
    private readonly List<ChannelWriter<TelemetryPoint>> _targets = new();
    private readonly ILogger<TelemetryDispatcher> _logger;
    
    // 统计
    private long _totalDispatched;
    private long _totalDroppedByTarget;
    
    public TelemetryDispatcher(
        TelemetryPipeline pipeline,
        ILogger<TelemetryDispatcher> logger)
    {
        _source = pipeline.Reader;
        _logger = logger;
    }
    
    /// <summary>
    /// 添加目标 Channel
    /// </summary>
    public ChannelWriter<TelemetryPoint> AddTarget(int capacity, string name)
    {
        var channel = Channel.CreateBounded<TelemetryPoint>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = true
        });
        
        _targets.Add(channel.Writer);
        _logger.LogInformation("Added dispatch target: {Name} with capacity {Capacity}", name, capacity);
        
        return channel.Writer;
    }
    
    /// <summary>
    /// 获取目标 Channel Reader
    /// </summary>
    public ChannelReader<TelemetryPoint> GetTargetReader(int index)
    {
        // 需要额外跟踪 reader，这里简化处理
        throw new NotImplementedException("Use CreateTargetChannel instead");
    }
    
    /// <summary>
    /// 创建并返回目标 Channel（用于外部消费者）
    /// </summary>
    public (ChannelWriter<TelemetryPoint> Writer, ChannelReader<TelemetryPoint> Reader) CreateTargetChannel(int capacity, string name)
    {
        var channel = Channel.CreateBounded<TelemetryPoint>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = true
        });
        
        _targets.Add(channel.Writer);
        _logger.LogInformation("Created dispatch target: {Name} with capacity {Capacity}", name, capacity);
        
        return (channel.Writer, channel.Reader);
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TelemetryDispatcher started with {TargetCount} targets", _targets.Count);
        
        try
        {
            await foreach (var point in _source.ReadAllAsync(stoppingToken))
            {
                await DispatchAsync(point, stoppingToken);
                Interlocked.Increment(ref _totalDispatched);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("TelemetryDispatcher stopping...");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TelemetryDispatcher error");
            throw;
        }
        finally
        {
            // 完成所有目标 Channel
            foreach (var target in _targets)
            {
                target.TryComplete();
            }
            
            _logger.LogInformation("TelemetryDispatcher stopped. Total dispatched: {Total}, Dropped: {Dropped}",
                _totalDispatched, _totalDroppedByTarget);
        }
    }
    
    /// <summary>
    /// 分发到所有目标
    /// </summary>
    private async ValueTask DispatchAsync(TelemetryPoint point, CancellationToken ct)
    {
        // 并行写入所有目标（非阻塞）
        foreach (var target in _targets)
        {
            if (!target.TryWrite(point))
            {
                // 目标满，尝试等待（短超时）
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromMilliseconds(10));
                
                try
                {
                    await target.WriteAsync(point, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // 超时，记录但不阻塞
                    Interlocked.Increment(ref _totalDroppedByTarget);
                }
            }
        }
    }
    
    public new Task StartAsync(CancellationToken ct) => base.StartAsync(ct);
    public new Task StopAsync(CancellationToken ct) => base.StopAsync(ct);
}
