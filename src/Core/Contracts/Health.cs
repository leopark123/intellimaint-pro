namespace IntelliMaint.Core.Contracts;

/// <summary>
/// 采集器状态
/// </summary>
public enum CollectorState
{
    /// <summary>正常连接</summary>
    Connected = 1,
    
    /// <summary>降级运行（部分功能受限）</summary>
    Degraded = 2,
    
    /// <summary>断开连接</summary>
    Disconnected = 3
}

/// <summary>
/// 数据库状态
/// </summary>
public enum DatabaseState
{
    Healthy = 1,
    Slow = 2,
    Unavailable = 3
}

/// <summary>
/// 队列状态
/// </summary>
public enum QueueState
{
    Normal = 1,
    Backpressure = 2,
    Full = 3
}

/// <summary>
/// 全局健康状态
/// </summary>
public enum HealthState
{
    Healthy = 1,
    Degraded = 2,
    NotReady = 3
}

/// <summary>
/// 采集器健康信息
/// </summary>
public sealed record CollectorHealth
{
    public required string Protocol { get; init; }
    public required CollectorState State { get; init; }
    public DateTimeOffset LastSuccessTime { get; init; }
    public int ConsecutiveErrors { get; init; }
    public int TypeMismatchCount { get; init; }
    public double AvgLatencyMs { get; init; }
    public double P95LatencyMs { get; init; }
    public string? LastError { get; init; }
    public int ActiveConnections { get; init; }
    public int TotalTagCount { get; init; }
    public int HealthyTagCount { get; init; }
}

/// <summary>
/// 健康快照
/// </summary>
public sealed record HealthSnapshot
{
    public required DateTimeOffset UtcTime { get; init; }
    public required HealthState OverallState { get; init; }
    public required DatabaseState DatabaseState { get; init; }
    public required QueueState QueueState { get; init; }
    public required long QueueDepth { get; init; }
    public required long DroppedPoints { get; init; }
    public required double WriteLatencyMsP95 { get; init; }
    public required Dictionary<string, CollectorHealth> Collectors { get; init; }
    public bool MqttConnected { get; init; }
    public long OutboxDepth { get; init; }
    public long MemoryUsedMb { get; init; }
}

/// <summary>
/// 管道统计
/// </summary>
public sealed record PipelineStats
{
    public long TotalReceived { get; init; }
    public long TotalWritten { get; init; }
    public long TotalDropped { get; init; }
    public long TotalPublished { get; init; }
    public long CurrentQueueDepth { get; init; }
    public double WriteRatePerSecond { get; init; }
    public double DropRatePerSecond { get; init; }
}
