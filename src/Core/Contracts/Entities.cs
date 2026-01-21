namespace IntelliMaint.Core.Contracts;

/// <summary>
/// 设备DTO
/// </summary>
public sealed record DeviceDto
{
    public required string DeviceId { get; init; }
    public string? Name { get; init; }
    public string? Location { get; init; }
    public string? Model { get; init; }
    public string? Protocol { get; init; }  // opcua, libplctag, modbus

    /// <summary>所属 Edge 节点 ID</summary>
    public string? EdgeId { get; init; }

    // 连接配置
    public string? Host { get; init; }              // 主机地址，如 localhost 或 192.168.1.100
    public int? Port { get; init; }                 // 端口，如 49320 (KEPServerEX OPC UA)
    public string? ConnectionString { get; init; }  // 完整连接字符串，优先使用（如 opc.tcp://localhost:49320）

    public bool Enabled { get; init; } = true;
    public long CreatedUtc { get; init; }
    public long UpdatedUtc { get; init; }
    public Dictionary<string, string>? Metadata { get; init; }
    
    /// <summary>
    /// 获取有效的连接地址
    /// </summary>
    public string? GetEffectiveEndpoint()
    {
        if (!string.IsNullOrWhiteSpace(ConnectionString))
            return ConnectionString;
        
        if (!string.IsNullOrWhiteSpace(Host))
        {
            var port = Port ?? GetDefaultPort();
            if (port <= 0)
                return Host;
            
            return Protocol?.ToLowerInvariant() switch
            {
                "opcua" => $"opc.tcp://{Host}:{port}",
                "libplctag" => $"{Host}:{port}",
                "modbus" => $"{Host}:{port}",
                _ => $"{Host}:{port}"
            };
        }
        
        return null;
    }
    
    private int GetDefaultPort() => Protocol?.ToLowerInvariant() switch
    {
        "opcua" => 4840,      // OPC UA 默认端口
        "libplctag" => 44818, // EtherNet/IP 默认端口
        "modbus" => 502,      // Modbus TCP 默认端口
        _ => 0
    };
}

/// <summary>
/// 标签DTO
/// </summary>
public sealed record TagDto
{
    public required string TagId { get; init; }
    public required string DeviceId { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Unit { get; init; }
    public required TagValueType DataType { get; init; }
    public bool Enabled { get; init; } = true;
    public long CreatedUtc { get; init; }
    public long UpdatedUtc { get; init; }
    
    // 协议特定配置
    public string? Address { get; init; }           // Modbus 地址 / OPC NodeId
    public int? ScanIntervalMs { get; init; }       // 采集周期
    public string? TagGroup { get; init; }          // Fast/Normal/Slow
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// 告警记录
/// </summary>
public sealed record AlarmRecord
{
    public required string AlarmId { get; init; }
    public required string DeviceId { get; init; }
    public string? TagId { get; init; }
    public required long Ts { get; init; }
    public required int Severity { get; init; }     // 1=Info, 2=Warning, 3=Alarm, 4=Critical
    public required string Code { get; init; }
    public required string Message { get; init; }
    public AlarmStatus Status { get; init; } = AlarmStatus.Open;
    public long CreatedUtc { get; init; }
    public long UpdatedUtc { get; init; }
    public string? AckedBy { get; init; }
    public long? AckedUtc { get; init; }
    public string? AckNote { get; init; }
}

/// <summary>
/// 告警状态
/// </summary>
public enum AlarmStatus
{
    Open = 0,
    Acknowledged = 1,
    Closed = 2
}

/// <summary>
/// 告警确认请求
/// </summary>
public sealed record AlarmAckRequest
{
    public required string AlarmId { get; init; }
    public required string AckedBy { get; init; }
    public string? AckNote { get; init; }
}

/// <summary>
/// 告警查询
/// </summary>
public sealed record AlarmQuery
{
    public string? DeviceId { get; init; }
    public AlarmStatus? Status { get; init; }
    public int? MinSeverity { get; init; }
    public long? StartTs { get; init; }
    public long? EndTs { get; init; }
    public int Limit { get; init; } = 100;
    public PageToken? After { get; init; }
}

/// <summary>
/// 系统设置
/// </summary>
public sealed record SystemSetting
{
    public required string Key { get; init; }
    public required string Value { get; init; }
    public long UpdatedUtc { get; init; }
}

/// <summary>
/// 审计日志条目
/// </summary>
public sealed record AuditLogEntry
{
    public long Id { get; init; }
    public long Ts { get; init; }
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public required string Action { get; init; }
    public required string ResourceType { get; init; }
    public string? ResourceId { get; init; }
    public string? Details { get; init; }
    public string? IpAddress { get; init; }
}

/// <summary>
/// 告警规则
/// </summary>
public sealed record AlarmRule
{
    public required string RuleId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string TagId { get; init; }
    public string? DeviceId { get; init; }
    public required string ConditionType { get; init; }  // gt, gte, lt, lte, eq, ne, offline, roc_percent, roc_absolute
    public required double Threshold { get; init; }      // 阈值告警=数值阈值, 离线=超时秒数, 变化率=变化阈值
    public int DurationMs { get; init; }                 // 0=立即触发（仅阈值告警使用）
    public int Severity { get; init; } = 3;              // 1-5
    public string? MessageTemplate { get; init; }
    public bool Enabled { get; init; } = true;
    public long CreatedUtc { get; init; }
    public long UpdatedUtc { get; init; }

    // === v56 新增字段 ===
    /// <summary>
    /// 变化率告警的时间窗口（毫秒），仅 roc_percent 和 roc_absolute 类型使用
    /// </summary>
    public int RocWindowMs { get; init; }

    /// <summary>
    /// 规则类型分类: threshold | offline | roc
    /// 默认为 threshold 以保持向后兼容
    /// </summary>
    public string RuleType { get; init; } = "threshold";
}

/// <summary>
/// v59: 告警聚合组
/// 将同设备同规则触发的多个告警合并为一个聚合组
/// </summary>
public sealed record AlarmGroup
{
    /// <summary>聚合组ID</summary>
    public required string GroupId { get; init; }

    /// <summary>设备ID</summary>
    public required string DeviceId { get; init; }

    /// <summary>标签ID（取第一条告警的标签）</summary>
    public string? TagId { get; init; }

    /// <summary>规则ID（从 alarm.Code 提取）</summary>
    public required string RuleId { get; init; }

    /// <summary>最高严重级别</summary>
    public int Severity { get; init; }

    /// <summary>告警代码</summary>
    public string? Code { get; init; }

    /// <summary>最后一条告警消息</summary>
    public string? Message { get; init; }

    /// <summary>聚合的告警数量</summary>
    public int AlarmCount { get; init; }

    /// <summary>第一条告警时间（毫秒时间戳）</summary>
    public long FirstOccurredUtc { get; init; }

    /// <summary>最后一条告警时间（毫秒时间戳）</summary>
    public long LastOccurredUtc { get; init; }

    /// <summary>聚合状态</summary>
    public AlarmStatus AggregateStatus { get; init; } = AlarmStatus.Open;

    public long CreatedUtc { get; init; }
    public long UpdatedUtc { get; init; }
}

/// <summary>
/// v59: 告警聚合组查询
/// </summary>
public sealed record AlarmGroupQuery
{
    public string? DeviceId { get; init; }
    public AlarmStatus? Status { get; init; }
    public int? MinSeverity { get; init; }
    public long? StartTs { get; init; }
    public long? EndTs { get; init; }
    public int Limit { get; init; } = 50;
    public PageToken? After { get; init; }
}
