using IntelliMaint.Core.Contracts;

namespace IntelliMaint.Core.Abstractions;

/// <summary>
/// v56.2: 时序数据库抽象层
/// 为 SQLite → TimescaleDB 迁移提供统一接口
/// </summary>
public interface ITimeSeriesDb
{
    /// <summary>数据库类型</summary>
    TimeSeriesDbType DbType { get; }

    /// <summary>数据库版本</summary>
    string Version { get; }

    /// <summary>是否支持原生时序功能（如 TimescaleDB 的 hypertable）</summary>
    bool SupportsNativeTimeSeries { get; }

    /// <summary>是否支持连续聚合</summary>
    bool SupportsContinuousAggregates { get; }

    /// <summary>获取数据库健康状态</summary>
    Task<DbHealthStatus> GetHealthAsync(CancellationToken ct = default);

    /// <summary>获取数据库统计信息</summary>
    Task<DbStatistics> GetStatisticsAsync(CancellationToken ct = default);

    /// <summary>执行数据库维护（清理、压缩等）</summary>
    Task<MaintenanceResult> PerformMaintenanceAsync(MaintenanceOptions options, CancellationToken ct = default);
}

/// <summary>
/// 时序数据库类型
/// </summary>
public enum TimeSeriesDbType
{
    /// <summary>SQLite (开发/MVP 阶段)</summary>
    Sqlite = 1,

    /// <summary>TimescaleDB (生产阶段)</summary>
    TimescaleDb = 2,

    /// <summary>PostgreSQL (无 TimescaleDB 扩展)</summary>
    PostgreSql = 3
}

/// <summary>
/// 数据库健康状态
/// </summary>
public sealed record DbHealthStatus
{
    /// <summary>是否健康</summary>
    public bool IsHealthy { get; init; }

    /// <summary>状态描述</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>连接延迟（毫秒）</summary>
    public double LatencyMs { get; init; }

    /// <summary>活跃连接数</summary>
    public int ActiveConnections { get; init; }

    /// <summary>最大连接数</summary>
    public int MaxConnections { get; init; }

    /// <summary>是否可写</summary>
    public bool IsWritable { get; init; }

    /// <summary>最后检查时间</summary>
    public DateTimeOffset LastCheckedUtc { get; init; }

    /// <summary>错误消息（如果有）</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>详细诊断信息</summary>
    public Dictionary<string, object>? Diagnostics { get; init; }
}

/// <summary>
/// 数据库统计信息
/// </summary>
public sealed record DbStatistics
{
    /// <summary>数据库大小（字节）</summary>
    public long DatabaseSizeBytes { get; init; }

    /// <summary>表统计</summary>
    public IReadOnlyList<TableStatistics> Tables { get; init; } = [];

    /// <summary>索引总数</summary>
    public int TotalIndexCount { get; init; }

    /// <summary>最老数据时间戳</summary>
    public long? OldestDataTs { get; init; }

    /// <summary>最新数据时间戳</summary>
    public long? NewestDataTs { get; init; }

    /// <summary>遥测数据总行数</summary>
    public long TotalTelemetryRows { get; init; }

    /// <summary>每秒写入速率（最近 1 分钟）</summary>
    public double WritesPerSecond { get; init; }

    /// <summary>每秒读取速率（最近 1 分钟）</summary>
    public double ReadsPerSecond { get; init; }

    /// <summary>缓存命中率（0-1）</summary>
    public double CacheHitRatio { get; init; }

    /// <summary>统计时间</summary>
    public DateTimeOffset GeneratedUtc { get; init; }
}

/// <summary>
/// 表统计信息
/// </summary>
public sealed record TableStatistics
{
    /// <summary>表名</summary>
    public required string TableName { get; init; }

    /// <summary>行数</summary>
    public long RowCount { get; init; }

    /// <summary>表大小（字节）</summary>
    public long SizeBytes { get; init; }

    /// <summary>索引大小（字节）</summary>
    public long IndexSizeBytes { get; init; }

    /// <summary>是否为 hypertable（TimescaleDB）</summary>
    public bool IsHypertable { get; init; }

    /// <summary>分区数（如果是 hypertable）</summary>
    public int? ChunkCount { get; init; }

    /// <summary>压缩后大小（如果启用压缩）</summary>
    public long? CompressedSizeBytes { get; init; }
}

/// <summary>
/// 维护选项
/// </summary>
public sealed record MaintenanceOptions
{
    /// <summary>是否执行 VACUUM/ANALYZE</summary>
    public bool Vacuum { get; init; } = true;

    /// <summary>是否重建索引</summary>
    public bool ReindexAll { get; init; } = false;

    /// <summary>是否清理过期数据</summary>
    public bool CleanupExpiredData { get; init; } = true;

    /// <summary>是否压缩旧数据（TimescaleDB）</summary>
    public bool CompressOldChunks { get; init; } = false;

    /// <summary>压缩超过 N 天的数据</summary>
    public int CompressAfterDays { get; init; } = 7;

    /// <summary>是否更新统计信息</summary>
    public bool UpdateStatistics { get; init; } = true;

    /// <summary>最大执行时间（秒）</summary>
    public int TimeoutSeconds { get; init; } = 300;
}

/// <summary>
/// 维护结果
/// </summary>
public sealed record MaintenanceResult
{
    /// <summary>是否成功</summary>
    public bool Success { get; init; }

    /// <summary>执行时间（毫秒）</summary>
    public long DurationMs { get; init; }

    /// <summary>释放的空间（字节）</summary>
    public long SpaceReclaimedBytes { get; init; }

    /// <summary>删除的过期行数</summary>
    public long RowsDeleted { get; init; }

    /// <summary>压缩的分区数（TimescaleDB）</summary>
    public int ChunksCompressed { get; init; }

    /// <summary>操作详情</summary>
    public IReadOnlyList<string> Operations { get; init; } = [];

    /// <summary>警告信息</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>错误信息</summary>
    public string? Error { get; init; }

    /// <summary>完成时间</summary>
    public DateTimeOffset CompletedUtc { get; init; }
}

/// <summary>
/// 数据库迁移信息
/// </summary>
public sealed record MigrationInfo
{
    /// <summary>当前 Schema 版本</summary>
    public int CurrentVersion { get; init; }

    /// <summary>目标 Schema 版本</summary>
    public int TargetVersion { get; init; }

    /// <summary>待执行的迁移</summary>
    public IReadOnlyList<MigrationStep> PendingMigrations { get; init; } = [];

    /// <summary>已执行的迁移</summary>
    public IReadOnlyList<MigrationStep> AppliedMigrations { get; init; } = [];
}

/// <summary>
/// 迁移步骤
/// </summary>
public sealed record MigrationStep
{
    /// <summary>版本号</summary>
    public int Version { get; init; }

    /// <summary>描述</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>应用时间</summary>
    public DateTimeOffset? AppliedUtc { get; init; }

    /// <summary>是否可回滚</summary>
    public bool IsReversible { get; init; }
}
