namespace IntelliMaint.Core.Contracts;

/// <summary>
/// 采集规则 - 定义条件触发的数据采集逻辑
/// </summary>
public sealed record CollectionRule
{
    /// <summary>规则唯一标识</summary>
    public required string RuleId { get; init; }
    
    /// <summary>规则名称</summary>
    public required string Name { get; init; }
    
    /// <summary>规则描述</summary>
    public string? Description { get; init; }
    
    /// <summary>关联设备ID</summary>
    public required string DeviceId { get; init; }
    
    /// <summary>是否启用</summary>
    public bool Enabled { get; init; } = true;
    
    /// <summary>开始条件配置 (JSON)</summary>
    public required string StartConditionJson { get; init; }
    
    /// <summary>停止条件配置 (JSON)</summary>
    public required string StopConditionJson { get; init; }
    
    /// <summary>采集配置 (JSON)</summary>
    public required string CollectionConfigJson { get; init; }
    
    /// <summary>后处理动作配置 (JSON，可选)</summary>
    public string? PostActionsJson { get; init; }
    
    /// <summary>触发次数统计</summary>
    public int TriggerCount { get; init; }
    
    /// <summary>最后触发时间 (Unix毫秒)</summary>
    public long? LastTriggerUtc { get; init; }
    
    /// <summary>创建时间 (Unix毫秒)</summary>
    public long CreatedUtc { get; init; }
    
    /// <summary>更新时间 (Unix毫秒)</summary>
    public long UpdatedUtc { get; init; }
}

/// <summary>
/// 采集片段 - 单次采集的数据段
/// </summary>
public sealed record CollectionSegment
{
    /// <summary>片段ID (自增)</summary>
    public long Id { get; init; }
    
    /// <summary>关联规则ID</summary>
    public required string RuleId { get; init; }
    
    /// <summary>关联设备ID</summary>
    public required string DeviceId { get; init; }
    
    /// <summary>采集开始时间 (Unix毫秒)</summary>
    public required long StartTimeUtc { get; init; }
    
    /// <summary>采集结束时间 (Unix毫秒)</summary>
    public long? EndTimeUtc { get; init; }
    
    /// <summary>片段状态</summary>
    public SegmentStatus Status { get; init; } = SegmentStatus.Collecting;
    
    /// <summary>数据点数量</summary>
    public int DataPointCount { get; init; }
    
    /// <summary>元数据 (JSON，可选)</summary>
    public string? MetadataJson { get; init; }
    
    /// <summary>创建时间 (Unix毫秒)</summary>
    public long CreatedUtc { get; init; }
}

/// <summary>
/// 片段状态
/// </summary>
public enum SegmentStatus
{
    /// <summary>采集中</summary>
    Collecting = 0,
    
    /// <summary>已完成</summary>
    Completed = 1,
    
    /// <summary>失败</summary>
    Failed = 2
}

/// <summary>
/// 条件配置 - 定义触发/停止条件
/// </summary>
public sealed record ConditionConfig
{
    /// <summary>逻辑组合: AND, OR</summary>
    public required string Logic { get; init; }
    
    /// <summary>条件列表</summary>
    public required List<ConditionItem> Conditions { get; init; }
}

/// <summary>
/// 条件项
/// </summary>
public sealed record ConditionItem
{
    /// <summary>条件类型: tag, duration</summary>
    public required string Type { get; init; }
    
    /// <summary>标签ID (type=tag时必填)</summary>
    public string? TagId { get; init; }
    
    /// <summary>比较操作符: gt, gte, lt, lte, eq, ne</summary>
    public string? Operator { get; init; }
    
    /// <summary>比较值 (type=tag时)</summary>
    public double? Value { get; init; }
    
    /// <summary>持续秒数 (type=duration时)</summary>
    public int? Seconds { get; init; }
}

/// <summary>
/// 采集配置
/// </summary>
public sealed record CollectionConfig
{
    /// <summary>要采集的标签ID列表</summary>
    public required List<string> TagIds { get; init; }
    
    /// <summary>前置缓冲秒数 (采集开始前也保留的数据)</summary>
    public int PreBufferSeconds { get; init; } = 5;
    
    /// <summary>后置缓冲秒数 (停止条件满足后继续采集)</summary>
    public int PostBufferSeconds { get; init; } = 3;
}

/// <summary>
/// 后处理动作
/// </summary>
public sealed record PostAction
{
    /// <summary>动作类型: saveCycleRecord, runAnomalyDetection, createAlarm, sendNotification</summary>
    public required string Type { get; init; }
    
    /// <summary>告警严重级别 (type=createAlarm时)</summary>
    public int? AlarmSeverity { get; init; }
    
    /// <summary>告警消息 (type=createAlarm时)</summary>
    public string? AlarmMessage { get; init; }
}

/// <summary>
/// 采集规则查询参数
/// </summary>
public sealed record CollectionRuleQuery
{
    /// <summary>设备ID过滤</summary>
    public string? DeviceId { get; init; }
    
    /// <summary>是否只查询启用的规则</summary>
    public bool? EnabledOnly { get; init; }
    
    /// <summary>返回数量限制</summary>
    public int Limit { get; init; } = 100;
}

/// <summary>
/// 采集片段查询参数
/// </summary>
public sealed record CollectionSegmentQuery
{
    /// <summary>规则ID过滤</summary>
    public string? RuleId { get; init; }
    
    /// <summary>设备ID过滤</summary>
    public string? DeviceId { get; init; }
    
    /// <summary>状态过滤</summary>
    public SegmentStatus? Status { get; init; }
    
    /// <summary>开始时间 (Unix毫秒)</summary>
    public long? StartTimeUtc { get; init; }
    
    /// <summary>结束时间 (Unix毫秒)</summary>
    public long? EndTimeUtc { get; init; }
    
    /// <summary>返回数量限制</summary>
    public int Limit { get; init; } = 100;
}
