namespace IntelliMaint.Core.Contracts;

/// <summary>
/// Edge 全局配置
/// </summary>
public sealed class EdgeOptions
{
    public const string SectionName = "Edge";

    /// <summary>Edge 实例ID</summary>
    public required string EdgeId { get; init; }

    /// <summary>数据库路径（SQLite 模式使用，TimescaleDB 模式可为空）</summary>
    public string? DatabasePath { get; init; }

    /// <summary>API 基础地址（用于健康上报）</summary>
    public string ApiBaseUrl { get; init; } = "http://localhost:5000";

    /// <summary>全局队列容量</summary>
    public int QueueCapacityGlobal { get; init; } = 200_000;

    /// <summary>写入批量大小</summary>
    public int WriterBatchSize { get; init; } = 1000;

    /// <summary>写入刷新间隔（毫秒）</summary>
    public int WriterFlushMs { get; init; } = 500;

    /// <summary>写入最大重试次数</summary>
    public int WriterMaxRetries { get; init; } = 3;

    /// <summary>写入重试延迟（毫秒）</summary>
    public int WriterRetryDelayMs { get; init; } = 1000;

    /// <summary>溢出导出配置</summary>
    public OverflowOptions Overflow { get; init; } = new();

    /// <summary>健康检查配置</summary>
    public HealthOptions Health { get; init; } = new();
}

/// <summary>
/// 溢出导出配置
/// </summary>
public sealed class OverflowOptions
{
    /// <summary>是否启用溢出导出</summary>
    public bool Enabled { get; init; } = true;
    
    /// <summary>导出目录</summary>
    public string Directory { get; init; } = "data/overflow";
    
    /// <summary>单文件滚动大小（MB）</summary>
    public int RollSizeMB { get; init; } = 100;
    
    /// <summary>是否压缩</summary>
    public bool Compress { get; init; } = true;
    
    /// <summary>保留天数</summary>
    public int RetentionDays { get; init; } = 7;
}

/// <summary>
/// 健康检查配置
/// </summary>
public sealed class HealthOptions
{
    /// <summary>快照间隔（秒）</summary>
    public int SnapshotIntervalSec { get; init; } = 30;
    
    /// <summary>内存警告阈值（MB）</summary>
    public int MemoryWarnMB { get; init; } = 500;
    
    /// <summary>内存告警阈值（MB）</summary>
    public int MemoryAlarmMB { get; init; } = 1000;
    
    /// <summary>写入延迟警告阈值（毫秒）</summary>
    public int WriteLatencyWarnMs { get; init; } = 100;
    
    /// <summary>丢弃率警告阈值（每分钟）</summary>
    public int DropRateWarnPerMinute { get; init; } = 1000;
}

/// <summary>
/// Channel 容量配置
/// </summary>
public sealed class ChannelCapacityOptions
{
    /// <summary>libplctag 协议队列容量</summary>
    public int LibPlcTagCapacity { get; init; } = 100_000;
    
    /// <summary>OPC UA 协议队列容量</summary>
    public int OpcUaCapacity { get; init; } = 50_000;
    
    /// <summary>Modbus 协议队列容量</summary>
    public int ModbusCapacity { get; init; } = 10_000;
    
    /// <summary>OPC DA 协议队列容量</summary>
    public int OpcDaCapacity { get; init; } = 10_000;
    
    /// <summary>全局合流队列容量</summary>
    public int GlobalCapacity { get; init; } = 200_000;
    
    /// <summary>数据库写入队列容量</summary>
    public int DbWriterCapacity { get; init; } = 50_000;
    
    /// <summary>MQTT 发布队列容量</summary>
    public int MqttPublisherCapacity { get; init; } = 50_000;
    
    /// <summary>告警处理队列容量</summary>
    public int AlarmProcessorCapacity { get; init; } = 10_000;
}

/// <summary>
/// MQTT 配置
/// </summary>
public sealed class MqttOptions
{
    public const string SectionName = "Mqtt";
    
    /// <summary>是否启用</summary>
    public bool Enabled { get; init; } = true;
    
    /// <summary>Broker 地址</summary>
    public required string Broker { get; init; }
    
    /// <summary>客户端ID</summary>
    public required string ClientId { get; init; }
    
    /// <summary>用户名</summary>
    public string? Username { get; init; }
    
    /// <summary>密码（支持环境变量 ${ENV_NAME}）</summary>
    public string? Password { get; init; }
    
    /// <summary>Clean Session</summary>
    public bool CleanSession { get; init; } = false;
    
    /// <summary>Keep Alive（秒）</summary>
    public int KeepAliveSec { get; init; } = 30;
    
    /// <summary>QoS 配置</summary>
    public MqttQosOptions Qos { get; init; } = new();
    
    /// <summary>发布配置</summary>
    public MqttPublishOptions Publish { get; init; } = new();
    
    /// <summary>Outbox 配置</summary>
    public MqttOutboxOptions Outbox { get; init; } = new();
    
    /// <summary>Topic 前缀</summary>
    public string TopicPrefix { get; init; } = "intellimaint/v1";
}

/// <summary>
/// MQTT QoS 配置
/// </summary>
public sealed class MqttQosOptions
{
    public int Telemetry { get; init; } = 0;
    public int Alarm { get; init; } = 1;
    public int Health { get; init; } = 1;
}

/// <summary>
/// MQTT 发布配置
/// </summary>
public sealed class MqttPublishOptions
{
    /// <summary>发布模式 ("Realtime" | "Batch" | "OnChange")</summary>
    public string Mode { get; init; } = "Batch";
    
    /// <summary>批量间隔（毫秒）</summary>
    public int BatchIntervalMs { get; init; } = 500;
    
    /// <summary>死区配置</summary>
    public DeadbandOptions Deadband { get; init; } = new();
    
    /// <summary>强制上报间隔（毫秒，即使没变化）</summary>
    public int ForcePublishIntervalMs { get; init; } = 60000;
}

/// <summary>
/// 死区配置
/// </summary>
public sealed class DeadbandOptions
{
    public float Float { get; init; } = 0.01f;
    public double Double { get; init; } = 0.01;
}

/// <summary>
/// MQTT Outbox 配置
/// </summary>
public sealed class MqttOutboxOptions
{
    public bool Enabled { get; init; } = true;
    public int MaxQueueItems { get; init; } = 200_000;
    public int DrainBatchSize { get; init; } = 100;
    public int DrainIntervalMs { get; init; } = 100;
}
