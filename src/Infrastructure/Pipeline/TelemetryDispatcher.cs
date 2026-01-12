using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;

namespace IntelliMaint.Infrastructure.Pipeline;

/// <summary>
/// 遥测分发器
/// 从源 Channel 读取，分发到多个目标 Channel
///
/// 架构说明（v56.2）:
/// - 当前采用双重缓冲架构：TelemetryPipeline(Buffer1) → Dispatcher → TargetChannels(Buffer2)
/// - 优点：解耦采集与消费，各消费者独立背压，故障隔离
/// - 缺点：额外内存开销，约2x数据缓冲
/// - 未来优化方向：若内存成为瓶颈，可考虑直连模式或共享内存池
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
    /// 分发到所有目标（优化：快速路径使用 TryWrite，慢速路径并行等待）
    /// </summary>
    private async ValueTask DispatchAsync(TelemetryPoint point, CancellationToken ct)
    {
        // 快速路径：所有目标都能立即写入
        var allWritten = true;
        var pendingTargets = new List<ChannelWriter<TelemetryPoint>>();

        foreach (var target in _targets)
        {
            if (!target.TryWrite(point))
            {
                allWritten = false;
                pendingTargets.Add(target);
            }
        }

        if (allWritten)
            return;

        // 慢速路径：对无法立即写入的目标进行短超时等待
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(10));

        var pendingTasks = pendingTargets.Select(async target =>
        {
            try
            {
                await target.WriteAsync(point, cts.Token);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        });

        var results = await Task.WhenAll(pendingTasks);
        var dropped = results.Count(r => !r);
        if (dropped > 0)
        {
            Interlocked.Add(ref _totalDroppedByTarget, dropped);
        }
    }
    
    public new Task StartAsync(CancellationToken ct) => base.StartAsync(ct);
    public new Task StopAsync(CancellationToken ct) => base.StopAsync(ct);
}
