using IntelliMaint.Core.Contracts;

namespace IntelliMaint.Core.Abstractions;

/// <summary>
/// 遥测管道接口 - 数据入口
/// </summary>
public interface ITelemetryPipeline
{
    /// <summary>写入数据点（可能触发背压）</summary>
    ValueTask<bool> WriteAsync(TelemetryPoint point, CancellationToken ct);
    
    /// <summary>批量写入数据点</summary>
    ValueTask<int> WriteBatchAsync(IReadOnlyList<TelemetryPoint> points, CancellationToken ct);
    
    /// <summary>获取管道统计</summary>
    PipelineStats GetStats();
    
    /// <summary>当前队列深度</summary>
    long QueueDepth { get; }
    
    /// <summary>队列容量</summary>
    long QueueCapacity { get; }
}

/// <summary>
/// 遥测分发器接口 - 从全局管道分发到各消费者
/// </summary>
public interface ITelemetryDispatcher
{
    /// <summary>启动分发</summary>
    Task StartAsync(CancellationToken ct);
    
    /// <summary>停止分发（排空队列）</summary>
    Task StopAsync(CancellationToken ct);
}

/// <summary>
/// 溢出导出器接口
/// </summary>
public interface IOverflowExporter
{
    /// <summary>导出被丢弃的数据点</summary>
    Task ExportAsync(TelemetryPoint point, CancellationToken ct);
    
    /// <summary>批量导出</summary>
    Task ExportBatchAsync(IReadOnlyList<TelemetryPoint> points, CancellationToken ct);
    
    /// <summary>获取导出统计</summary>
    OverflowStats GetStats();
}

/// <summary>
/// 溢出统计
/// </summary>
public sealed record OverflowStats
{
    public long TotalExported { get; init; }
    public long CurrentFileSize { get; init; }
    public int FileCount { get; init; }
    public string? CurrentFilePath { get; init; }
}

/// <summary>
/// 健康探针接口
/// </summary>
public interface IHealthProbe
{
    /// <summary>获取健康快照</summary>
    Task<HealthSnapshot> GetSnapshotAsync(CancellationToken ct);
    
    /// <summary>检查是否存活</summary>
    bool IsLive();
    
    /// <summary>检查是否就绪</summary>
    Task<bool> IsReadyAsync(CancellationToken ct);
}

/// <summary>
/// 数据库健康检查接口
/// </summary>
public interface IDatabaseHealthChecker
{
    /// <summary>检查数据库健康状态</summary>
    Task<DatabaseHealth> CheckAsync(CancellationToken ct);
}

/// <summary>
/// 数据库健康信息
/// </summary>
public sealed record DatabaseHealth
{
    public required DatabaseState State { get; init; }
    public double LatencyMs { get; init; }
    public string? ErrorMessage { get; init; }
    public long FileSizeBytes { get; init; }
}
