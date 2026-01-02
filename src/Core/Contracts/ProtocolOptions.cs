namespace IntelliMaint.Core.Contracts;

/// <summary>
/// 协议配置根节点
/// </summary>
public sealed class ProtocolsOptions
{
    public const string SectionName = "Protocols";
    
    public LibPlcTagOptions? LibPlcTag { get; init; }
    public OpcUaOptions? OpcUa { get; init; }
    public ModbusOptions? Modbus { get; init; }
    public OpcDaOptions? OpcDa { get; init; }
}

// ==================== LibPlcTag ====================

/// <summary>
/// LibPlcTag 配置
/// </summary>
public sealed record LibPlcTagOptions
{
    public bool Enabled { get; init; } = true;
    
    /// <summary>
    /// v55: 模拟模式 - 不连接真实 PLC，生成模拟数据用于测试
    /// </summary>
    public bool SimulationMode { get; init; } = false;
    
    public List<PlcEndpointConfig> Plcs { get; init; } = new();
}

/// <summary>
/// PLC 端点配置
/// </summary>
public sealed class PlcEndpointConfig
{
    /// <summary>PLC 标识</summary>
    public required string PlcId { get; init; }
    
    /// <summary>IP 地址</summary>
    public required string IpAddress { get; init; }
    
    /// <summary>路径 (如 "1,0")</summary>
    public string Path { get; init; } = "1,0";
    
    /// <summary>槽位</summary>
    public int Slot { get; init; } = 0;
    
    /// <summary>PLC 类型 ("ControlLogix" | "CompactLogix" | "Micro800")</summary>
    public string PlcType { get; init; } = "ControlLogix";
    
    /// <summary>最大连接数</summary>
    public int MaxConnections { get; init; } = 4;
    
    /// <summary>读取模式 ("BatchRead" | "ParallelRead")</summary>
    public string ReadMode { get; init; } = "BatchRead";
    
    /// <summary>超时（毫秒）</summary>
    public int TimeoutMs { get; init; } = 50;
    
    /// <summary>重试策略</summary>
    public RetryPolicyConfig Retry { get; init; } = new();
    
    /// <summary>标签组</summary>
    public List<TagGroupConfig> TagGroups { get; init; } = new();
}

/// <summary>
/// 标签组配置
/// </summary>
public sealed class TagGroupConfig
{
    /// <summary>组名 ("Fast" | "Normal" | "Slow")</summary>
    public required string Name { get; init; }
    
    /// <summary>扫描间隔（毫秒）</summary>
    public int ScanIntervalMs { get; init; } = 100;
    
    /// <summary>批量读取大小</summary>
    public int BatchReadSize { get; init; } = 100;
    
    /// <summary>标签列表</summary>
    public List<PlcTagConfig> Tags { get; init; } = new();
}

/// <summary>
/// PLC 标签配置
/// </summary>
public sealed class PlcTagConfig
{
    /// <summary>标签ID</summary>
    public required string TagId { get; init; }
    
    /// <summary>PLC 内部名称 (如 "Program:MainProgram.Motor.Speed")</summary>
    public required string Name { get; init; }
    
    /// <summary>CIP 类型 ("DINT" | "REAL" | "BOOL" 等)</summary>
    public required string CipType { get; init; }
    
    /// <summary>数组长度（0 表示非数组）</summary>
    public int ArrayLength { get; init; } = 0;
    
    /// <summary>单位</summary>
    public string? Unit { get; init; }
    
    /// <summary>描述</summary>
    public string? Description { get; init; }
}

/// <summary>
/// 重试策略配置
/// </summary>
public sealed class RetryPolicyConfig
{
    /// <summary>最大重试次数</summary>
    public int MaxRetries { get; init; } = 3;
    
    /// <summary>初始延迟（毫秒）</summary>
    public int InitialDelayMs { get; init; } = 1000;
    
    /// <summary>最大延迟（毫秒）</summary>
    public int MaxDelayMs { get; init; } = 60000;
    
    /// <summary>退避倍数</summary>
    public double BackoffMultiplier { get; init; } = 2.0;
}

// ==================== OPC UA ====================

/// <summary>
/// OPC UA 配置
/// </summary>
public sealed class OpcUaOptions
{
    public bool Enabled { get; init; } = false;
    public List<OpcUaEndpointConfig> Endpoints { get; init; } = new();
}

/// <summary>
/// OPC UA 端点配置
/// </summary>
public sealed class OpcUaEndpointConfig
{
    /// <summary>端点ID</summary>
    public required string EndpointId { get; init; }
    
    /// <summary>端点URL</summary>
    public required string EndpointUrl { get; init; }
    
    /// <summary>服务器类型 ("KEPServerEX" | "BuiltIn" | "Other")</summary>
    public string ServerType { get; init; } = "KEPServerEX";
    
    /// <summary>安全策略 ("None" | "Basic256Sha256")</summary>
    public string SecurityPolicy { get; init; } = "None";
    
    /// <summary>消息安全模式 ("None" | "Sign" | "SignAndEncrypt")</summary>
    public string MessageSecurityMode { get; init; } = "None";
    
    /// <summary>用户名（匿名留空）</summary>
    public string? Username { get; init; }
    
    /// <summary>密码</summary>
    public string? Password { get; init; }
    
    /// <summary>会话超时（毫秒）</summary>
    public int SessionTimeoutMs { get; init; } = 60000;
    
    /// <summary>订阅配置</summary>
    public OpcUaSubscriptionConfig Subscription { get; init; } = new();
    
    /// <summary>节点列表</summary>
    public List<OpcUaNodeConfig> Nodes { get; init; } = new();
}

/// <summary>
/// OPC UA 订阅配置
/// </summary>
public sealed class OpcUaSubscriptionConfig
{
    /// <summary>发布间隔（毫秒）</summary>
    public int PublishingIntervalMs { get; init; } = 100;
    
    /// <summary>生命周期计数</summary>
    public int LifetimeCount { get; init; } = 300;
    
    /// <summary>最大保活计数</summary>
    public int MaxKeepAliveCount { get; init; } = 10;
    
    /// <summary>每次发布最大通知数</summary>
    public int MaxNotificationsPerPublish { get; init; } = 1000;
    
    /// <summary>优先级</summary>
    public byte Priority { get; init; } = 0;
}

/// <summary>
/// OPC UA 节点配置
/// </summary>
public sealed class OpcUaNodeConfig
{
    /// <summary>标签ID</summary>
    public required string TagId { get; init; }
    
    /// <summary>NodeId (如 "ns=2;s=Channel1.Device1.Tag1")</summary>
    public required string NodeId { get; init; }
    
    /// <summary>值类型提示</summary>
    public string? ValueTypeHint { get; init; }
    
    /// <summary>采样间隔（毫秒）</summary>
    public int SamplingIntervalMs { get; init; } = 100;
    
    /// <summary>队列大小</summary>
    public int QueueSize { get; init; } = 10;
    
    /// <summary>丢弃最旧</summary>
    public bool DiscardOldest { get; init; } = true;
    
    /// <summary>单位</summary>
    public string? Unit { get; init; }
}

// ==================== Modbus ====================

/// <summary>
/// Modbus 配置
/// </summary>
public sealed class ModbusOptions
{
    public bool Enabled { get; init; } = false;
    public List<ModbusSlaveConfig> Slaves { get; init; } = new();
}

/// <summary>
/// Modbus 从站配置
/// </summary>
public sealed class ModbusSlaveConfig
{
    /// <summary>从站ID</summary>
    public required string SlaveId { get; init; }
    
    /// <summary>IP 地址</summary>
    public required string IpAddress { get; init; }
    
    /// <summary>端口</summary>
    public int Port { get; init; } = 502;
    
    /// <summary>单元ID</summary>
    public byte UnitId { get; init; } = 1;
    
    /// <summary>轮询间隔（毫秒）</summary>
    public int PollIntervalMs { get; init; } = 200;
    
    /// <summary>超时（毫秒）</summary>
    public int TimeoutMs { get; init; } = 1000;
    
    /// <summary>重试策略</summary>
    public RetryPolicyConfig Retry { get; init; } = new();
    
    /// <summary>寄存器列表</summary>
    public List<ModbusRegisterConfig> Registers { get; init; } = new();
}

/// <summary>
/// Modbus 寄存器配置
/// </summary>
public sealed class ModbusRegisterConfig
{
    /// <summary>标签ID</summary>
    public required string TagId { get; init; }
    
    /// <summary>寄存器地址</summary>
    public required int Address { get; init; }
    
    /// <summary>功能码 (1=Coil, 2=DiscreteInput, 3=HoldingRegister, 4=InputRegister)</summary>
    public int FunctionCode { get; init; } = 3;
    
    /// <summary>数据类型 ("Bool" | "Int16" | "UInt16" | "Int32" | "UInt32" | "Float32" | "Float64")</summary>
    public required string DataType { get; init; }
    
    /// <summary>字节序 ("BigEndian" | "LittleEndian" | "BigEndianWordSwap" | "LittleEndianWordSwap")</summary>
    public string Endian { get; init; } = "BigEndian";
    
    /// <summary>缩放因子</summary>
    public double Scale { get; init; } = 1.0;
    
    /// <summary>偏移量</summary>
    public double Offset { get; init; } = 0.0;
    
    /// <summary>单位</summary>
    public string? Unit { get; init; }
}

// ==================== OPC DA ====================

/// <summary>
/// OPC DA 配置
/// </summary>
public sealed class OpcDaOptions
{
    public bool Enabled { get; init; } = false;
    public List<OpcDaServerConfig> Servers { get; init; } = new();
}

/// <summary>
/// OPC DA 服务器配置
/// </summary>
public sealed class OpcDaServerConfig
{
    /// <summary>服务器ID</summary>
    public required string ServerId { get; init; }
    
    /// <summary>ProgId</summary>
    public required string ProgId { get; init; }
    
    /// <summary>主机名（本地为空）</summary>
    public string? Host { get; init; }
    
    /// <summary>更新速率（毫秒）</summary>
    public int UpdateRateMs { get; init; } = 100;
    
    /// <summary>COM 线程模型 ("STA" | "MTA")</summary>
    public string ComThreadingModel { get; init; } = "STA";
    
    /// <summary>Item 列表</summary>
    public List<OpcDaItemConfig> Items { get; init; } = new();
}

/// <summary>
/// OPC DA Item 配置
/// </summary>
public sealed class OpcDaItemConfig
{
    /// <summary>标签ID</summary>
    public required string TagId { get; init; }
    
    /// <summary>Item ID</summary>
    public required string ItemId { get; init; }
    
    /// <summary>值类型提示</summary>
    public string? ValueTypeHint { get; init; }
    
    /// <summary>单位</summary>
    public string? Unit { get; init; }
}
