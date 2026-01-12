namespace IntelliMaint.Core.Contracts;

/// <summary>
/// Edge 节点配置
/// </summary>
public record EdgeConfigDto
{
    public string EdgeId { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Description { get; init; }

    // 预处理配置
    public ProcessingConfigDto Processing { get; init; } = new();

    // 断网续传配置
    public StoreForwardConfigDto StoreForward { get; init; } = new();

    // 网络配置
    public NetworkConfigDto Network { get; init; } = new();

    // 元数据
    public long CreatedUtc { get; init; }
    public long? UpdatedUtc { get; init; }
    public string? UpdatedBy { get; init; }
}

/// <summary>
/// 数据预处理配置
/// </summary>
public record ProcessingConfigDto
{
    public bool Enabled { get; init; } = true;
    public double DefaultDeadband { get; init; } = 0.01;
    public double DefaultDeadbandPercent { get; init; } = 0.5;
    public int DefaultMinIntervalMs { get; init; } = 1000;
    public int ForceUploadIntervalMs { get; init; } = 60000;

    // 异常检测
    public bool OutlierEnabled { get; init; } = true;
    public double OutlierSigmaThreshold { get; init; } = 4.0;
    public string OutlierAction { get; init; } = "Mark"; // Drop/Mark/Pass
}

/// <summary>
/// 断网续传配置
/// </summary>
public record StoreForwardConfigDto
{
    public bool Enabled { get; init; } = true;
    public int MaxStoreSizeMB { get; init; } = 1000;
    public int RetentionDays { get; init; } = 7;
    public bool CompressionEnabled { get; init; } = true;
    public string CompressionAlgorithm { get; init; } = "Gzip"; // Gzip/Brotli
}

/// <summary>
/// 网络配置
/// </summary>
public record NetworkConfigDto
{
    public int HealthCheckIntervalMs { get; init; } = 5000;
    public int HealthCheckTimeoutMs { get; init; } = 3000;
    public int OfflineThreshold { get; init; } = 3;
    public int SendBatchSize { get; init; } = 500;
    public int SendIntervalMs { get; init; } = 500;
}

/// <summary>
/// 标签级预处理配置
/// </summary>
public record TagProcessingConfigDto
{
    public int Id { get; init; }
    public string EdgeId { get; init; } = "";
    public string TagId { get; init; } = "";
    public string? TagName { get; init; }
    public double? Deadband { get; init; }
    public double? DeadbandPercent { get; init; }
    public int? MinIntervalMs { get; init; }
    public bool Bypass { get; init; } = false;
    public int Priority { get; init; } = 0;
    public string? Description { get; init; }
    public long CreatedUtc { get; init; }
    public long? UpdatedUtc { get; init; }
}

/// <summary>
/// 批量更新标签配置请求
/// </summary>
public record BatchUpdateTagConfigRequest
{
    public List<TagProcessingConfigDto> Tags { get; init; } = new();
}

/// <summary>
/// Edge 运行状态
/// </summary>
public record EdgeStatusDto
{
    public string EdgeId { get; init; } = "";
    public bool IsOnline { get; init; }
    public int PendingPoints { get; init; }
    public double FilterRate { get; init; }
    public long SentCount { get; init; }
    public double StoredMB { get; init; }
    public long LastHeartbeatUtc { get; init; }
    public string? Version { get; init; }
}

/// <summary>
/// Edge 节点摘要（用于列表）
/// </summary>
public record EdgeSummaryDto
{
    public string EdgeId { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public bool IsOnline { get; init; }
    public long? LastHeartbeatUtc { get; init; }
    public int DeviceCount { get; init; }
    public int TagCount { get; init; }
}

/// <summary>
/// 分页标签配置列表
/// </summary>
public record PagedTagConfigResult
{
    public List<TagProcessingConfigDto> Items { get; init; } = new();
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}
