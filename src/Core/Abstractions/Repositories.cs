using IntelliMaint.Core.Contracts;

namespace IntelliMaint.Core.Abstractions;

// Note: TagImportanceConfig and TagImportance are defined in Core/Contracts/HealthAssessmentConfig.cs

/// <summary>
/// 遥测数据仓储接口
/// </summary>
public interface ITelemetryRepository
{
    /// <summary>批量追加数据点</summary>
    Task<int> AppendBatchAsync(IReadOnlyList<TelemetryPoint> batch, CancellationToken ct);
    
    /// <summary>查询历史数据（Keyset分页）</summary>
    Task<PagedResult<TelemetryPoint>> QueryAsync(HistoryQuery query, CancellationToken ct);
    
    /// <summary>删除指定时间之前的数据</summary>
    Task<int> DeleteBeforeAsync(long cutoffTs, CancellationToken ct);
    
    /// <summary>获取数据统计</summary>
    Task<TelemetryStats> GetStatsAsync(string? deviceId, CancellationToken ct);

    // ---------------------------
    // Batch 7: 新增查询 API 方法
    // ---------------------------

    /// <summary>
    /// 查询历史数据（简化版：可选 deviceId/tagId + 时间范围 + limit）
    /// </summary>
    Task<IReadOnlyList<TelemetryPoint>> QuerySimpleAsync(
        string? deviceId,
        string? tagId,
        long? startTs,
        long? endTs,
        int limit,
        CancellationToken ct);
    
    /// <summary>
    /// v48: 游标分页查询历史数据
    /// </summary>
    Task<(IReadOnlyList<TelemetryPoint> Data, bool HasMore)> QueryWithCursorAsync(
        string? deviceId,
        string? tagId,
        long? startTs,
        long? endTs,
        int limit,
        long? cursorTs,
        int? cursorSeq,
        CancellationToken ct);

    /// <summary>
    /// 获取最新值：每个 (deviceId, tagId) 返回最新一条记录（可选过滤）
    /// </summary>
    Task<IReadOnlyList<TelemetryPoint>> GetLatestAsync(
        string? deviceId,
        string? tagId,
        CancellationToken ct);

    /// <summary>
    /// 获取所有已知的 Tag 列表（去重 + 统计）
    /// </summary>
    Task<IReadOnlyList<TagInfo>> GetTagsAsync(CancellationToken ct);

    /// <summary>
    /// 聚合查询：按 intervalMs 分桶聚合
    /// </summary>
    Task<IReadOnlyList<AggregateResult>> AggregateAsync(
        string deviceId,
        string tagId,
        long startTs,
        long endTs,
        int intervalMs,
        AggregateFunction func,
        CancellationToken ct);
}

/// <summary>
/// 遥测数据统计
/// </summary>
public sealed record TelemetryStats
{
    public long TotalCount { get; init; }
    public long? OldestTs { get; init; }
    public long? NewestTs { get; init; }
    public int DeviceCount { get; init; }
    public int TagCount { get; init; }
}

/// <summary>
/// Tag 列表与统计信息（Batch 7 新增）
/// </summary>
public sealed record TagInfo
{
    public required string DeviceId { get; init; }
    public required string TagId { get; init; }
    public required TagValueType ValueType { get; init; }
    public string? Unit { get; init; }
    public long? LastTs { get; init; }
    public int PointCount { get; init; }
}

/// <summary>
/// 聚合函数（Batch 7 新增）
/// </summary>
public enum AggregateFunction
{
    Avg,
    Min,
    Max,
    Sum,
    Count,
    First,
    Last
}

/// <summary>
/// 聚合结果（Batch 7 新增）
/// </summary>
public sealed record AggregateResult
{
    public required long Ts { get; init; }      // 时间桶起始时间
    public required double Value { get; init; } // 聚合值
    public required int Count { get; init; }    // 样本数量
}

/// <summary>
/// 设备仓储接口
/// </summary>
public interface IDeviceRepository
{
    Task<IReadOnlyList<DeviceDto>> ListAsync(CancellationToken ct);
    Task<DeviceDto?> GetAsync(string deviceId, CancellationToken ct);
    Task UpsertAsync(DeviceDto device, CancellationToken ct);
    Task DeleteAsync(string deviceId, CancellationToken ct);
}

/// <summary>
/// 标签仓储接口
/// </summary>
public interface ITagRepository
{
    Task<IReadOnlyList<TagDto>> ListAllAsync(CancellationToken ct);
    Task<IReadOnlyList<TagDto>> ListByDeviceAsync(string deviceId, CancellationToken ct);
    Task<TagDto?> GetAsync(string tagId, CancellationToken ct);
    Task UpsertAsync(TagDto tag, CancellationToken ct);
    Task DeleteAsync(string tagId, CancellationToken ct);
    Task<IReadOnlyList<TagDto>> ListByGroupAsync(string deviceId, string tagGroup, CancellationToken ct);

    /// <summary>
    /// v56.1: 批量获取多个设备的标签（避免 N+1 查询）
    /// </summary>
    Task<Dictionary<string, List<TagDto>>> ListByDevicesAsync(IEnumerable<string> deviceIds, CancellationToken ct);
}

/// <summary>
/// 告警仓储接口
/// </summary>
public interface IAlarmRepository
{
    Task CreateAsync(AlarmRecord alarm, CancellationToken ct);
    Task AckAsync(AlarmAckRequest request, CancellationToken ct);
    Task CloseAsync(string alarmId, CancellationToken ct);
    Task<AlarmRecord?> GetAsync(string alarmId, CancellationToken ct);
    /// <summary>批量获取告警（优化N+1查询）</summary>
    Task<IReadOnlyList<AlarmRecord>> GetByIdsAsync(IEnumerable<string> alarmIds, CancellationToken ct);
    Task<PagedResult<AlarmRecord>> QueryAsync(AlarmQuery query, CancellationToken ct);
    Task<int> GetOpenCountAsync(string? deviceId, CancellationToken ct);
    /// <summary>获取各状态的告警数量</summary>
    Task<AlarmStatusCounts> GetStatusCountsAsync(string? deviceId, CancellationToken ct);
    /// <summary>批量获取设备的未关闭告警数量（优化N+1查询）</summary>
    Task<IReadOnlyDictionary<string, int>> GetOpenCountByDevicesAsync(IEnumerable<string> deviceIds, CancellationToken ct);
    Task<int> DeleteBeforeAsync(long cutoffTs, CancellationToken ct);

    /// <summary>检查是否存在指定 code 的未关闭告警（用于告警去重）</summary>
    Task<bool> HasUnclosedByCodeAsync(string code, CancellationToken ct);

    /// <summary>v59: 获取告警趋势数据（按时间桶聚合）</summary>
    Task<IReadOnlyList<AlarmTrendBucket>> GetTrendAsync(AlarmTrendQuery query, CancellationToken ct);
}

/// <summary>
/// v59: 告警趋势查询参数
/// </summary>
public sealed record AlarmTrendQuery
{
    public string? DeviceId { get; init; }
    public long? StartTs { get; init; }
    public long? EndTs { get; init; }
    /// <summary>时间桶大小（毫秒），默认1小时=3600000</summary>
    public long BucketSizeMs { get; init; } = 3600000;
    public int? Limit { get; init; }
}

/// <summary>
/// v59: 告警趋势数据桶
/// </summary>
public sealed record AlarmTrendBucket
{
    public long Bucket { get; init; }
    public string? DeviceId { get; init; }
    public int TotalCount { get; init; }
    public int OpenCount { get; init; }
    public int CriticalCount { get; init; }
    public int WarningCount { get; init; }
}

/// <summary>
/// 告警状态数量统计
/// </summary>
public sealed record AlarmStatusCounts
{
    public int OpenCount { get; init; }
    public int AcknowledgedCount { get; init; }
    public int ClosedCount { get; init; }
}

/// <summary>
/// 健康快照仓储接口
/// </summary>
public interface IHealthSnapshotRepository
{
    Task SaveAsync(HealthSnapshot snapshot, CancellationToken ct);
    Task<IReadOnlyList<HealthSnapshot>> GetRecentAsync(int count, CancellationToken ct);
    Task<int> DeleteBeforeAsync(long cutoffTs, CancellationToken ct);
}

/// <summary>
/// MQTT Outbox 仓储接口
/// </summary>
public interface IMqttOutboxRepository
{
    Task EnqueueAsync(OutboxMessage message, CancellationToken ct);
    Task<IReadOnlyList<OutboxMessage>> DequeueBatchAsync(int limit, CancellationToken ct);
    Task MarkSentAsync(IEnumerable<long> ids, CancellationToken ct);
    Task MarkFailedAsync(long id, string error, CancellationToken ct);
    Task<int> GetPendingCountAsync(CancellationToken ct);
    Task CleanupSentAsync(long olderThanTs, CancellationToken ct);
}

/// <summary>
/// Outbox 消息
/// </summary>
public sealed record OutboxMessage
{
    public long Id { get; init; }
    public required string Topic { get; init; }
    public required byte[] Payload { get; init; }
    public int Qos { get; init; }
    public long CreatedUtc { get; init; }
    public int RetryCount { get; init; }
    public long? LastRetryUtc { get; init; }
    public OutboxStatus Status { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Outbox 消息状态
/// </summary>
public enum OutboxStatus
{
    Pending = 0,
    Sending = 1,
    Sent = 2,
    Failed = 3
}

// ========================================
// 以下接口从 Infrastructure.Sqlite 迁移 (v36)
// ========================================

/// <summary>
/// 告警规则仓储接口
/// </summary>
public interface IAlarmRuleRepository
{
    Task<IReadOnlyList<AlarmRule>> ListAsync(CancellationToken ct);
    Task<IReadOnlyList<AlarmRule>> ListEnabledAsync(CancellationToken ct);
    Task<AlarmRule?> GetAsync(string ruleId, CancellationToken ct);
    Task UpsertAsync(AlarmRule rule, CancellationToken ct);
    Task DeleteAsync(string ruleId, CancellationToken ct);
    Task SetEnabledAsync(string ruleId, bool enabled, CancellationToken ct);
}

/// <summary>
/// 审计日志仓储接口
/// </summary>
public interface IAuditLogRepository
{
    Task<long> CreateAsync(AuditLogEntry entry, CancellationToken ct);
    Task<(IReadOnlyList<AuditLogEntry> Items, int TotalCount)> QueryAsync(AuditLogQuery query, CancellationToken ct);
    Task<IReadOnlyList<string>> GetDistinctActionsAsync(CancellationToken ct);
    Task<IReadOnlyList<string>> GetDistinctResourceTypesAsync(CancellationToken ct);

    /// <summary>删除指定时间之前的审计日志</summary>
    Task<int> DeleteBeforeAsync(long cutoffTs, CancellationToken ct);
}

/// <summary>
/// 系统设置仓储接口
/// </summary>
public interface ISystemSettingRepository
{
    Task<IReadOnlyList<SystemSetting>> GetAllAsync(CancellationToken ct);
    Task<string?> GetAsync(string key, CancellationToken ct);
    Task SetAsync(string key, string value, CancellationToken ct);
}

/// <summary>
/// 用户仓储接口
/// </summary>
public interface IUserRepository
{
    Task<UserDto?> GetByUsernameAsync(string username, CancellationToken ct);
    Task<UserDto?> GetByIdAsync(string userId, CancellationToken ct);  // v40 新增
    Task<UserDto?> ValidateCredentialsAsync(string username, string password, CancellationToken ct);
    Task<UserDto?> CreateAsync(string username, string password, string role, string? displayName, CancellationToken ct);
    Task<UserDto?> UpdateAsync(string userId, string? displayName, string? role, bool? enabled, CancellationToken ct);  // v40 新增
    Task<bool> UpdatePasswordAsync(string userId, string currentPassword, string newPassword, CancellationToken ct);  // v40 新增
    Task<bool> ResetPasswordAsync(string userId, string newPassword, CancellationToken ct);  // v40 新增
    Task<bool> DisableAsync(string userId, CancellationToken ct);  // v40 新增
    Task UpdateLastLoginAsync(string userId, CancellationToken ct);
    Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken ct);
    
    /// <summary>保存 Refresh Token</summary>
    Task SaveRefreshTokenAsync(string userId, string refreshToken, long expiresUtc, CancellationToken ct);
    
    /// <summary>通过 Refresh Token 获取用户</summary>
    Task<UserDto?> GetByRefreshTokenAsync(string refreshToken, CancellationToken ct);
    
    /// <summary>清除 Refresh Token（登出）</summary>
    Task ClearRefreshTokenAsync(string userId, CancellationToken ct);
    
    // v48: 账号锁定相关方法
    /// <summary>检查账号是否被锁定</summary>
    Task<(bool IsLocked, int RemainingMinutes)> CheckLockoutAsync(string username, CancellationToken ct);
    
    /// <summary>获取失败登录次数</summary>
    Task<int> GetFailedLoginCountAsync(string username, CancellationToken ct);
}

// ========================================
// 采集规则相关接口 (v46)
// ========================================

/// <summary>
/// 采集规则仓储接口
/// </summary>
public interface ICollectionRuleRepository
{
    /// <summary>获取所有规则</summary>
    Task<IReadOnlyList<CollectionRule>> ListAsync(CancellationToken ct);
    
    /// <summary>获取所有启用的规则</summary>
    Task<IReadOnlyList<CollectionRule>> ListEnabledAsync(CancellationToken ct);
    
    /// <summary>按设备ID获取规则</summary>
    Task<IReadOnlyList<CollectionRule>> ListByDeviceAsync(string deviceId, CancellationToken ct);
    
    /// <summary>获取单个规则</summary>
    Task<CollectionRule?> GetAsync(string ruleId, CancellationToken ct);
    
    /// <summary>创建或更新规则</summary>
    Task UpsertAsync(CollectionRule rule, CancellationToken ct);
    
    /// <summary>删除规则</summary>
    Task DeleteAsync(string ruleId, CancellationToken ct);
    
    /// <summary>设置启用状态</summary>
    Task SetEnabledAsync(string ruleId, bool enabled, CancellationToken ct);
    
    /// <summary>增加触发计数并更新最后触发时间</summary>
    Task IncrementTriggerCountAsync(string ruleId, CancellationToken ct);
}

/// <summary>
/// 采集片段仓储接口
/// </summary>
public interface ICollectionSegmentRepository
{
    /// <summary>创建片段，返回自增ID</summary>
    Task<long> CreateAsync(CollectionSegment segment, CancellationToken ct);
    
    /// <summary>获取单个片段</summary>
    Task<CollectionSegment?> GetAsync(long id, CancellationToken ct);
    
    /// <summary>按规则ID获取片段列表</summary>
    Task<IReadOnlyList<CollectionSegment>> ListByRuleAsync(string ruleId, int limit, CancellationToken ct);
    
    /// <summary>按设备ID和时间范围获取片段列表</summary>
    Task<IReadOnlyList<CollectionSegment>> ListByDeviceAsync(
        string deviceId, 
        long? startTimeUtc, 
        long? endTimeUtc, 
        int limit, 
        CancellationToken ct);
    
    /// <summary>查询片段</summary>
    Task<IReadOnlyList<CollectionSegment>> QueryAsync(CollectionSegmentQuery query, CancellationToken ct);
    
    /// <summary>更新片段状态和数据点计数</summary>
    Task UpdateStatusAsync(long id, SegmentStatus status, int dataPointCount, CancellationToken ct);
    
    /// <summary>更新片段结束时间</summary>
    Task SetEndTimeAsync(long id, long endTimeUtc, CancellationToken ct);
    
    /// <summary>删除片段</summary>
    Task DeleteAsync(long id, CancellationToken ct);
    
    /// <summary>获取指定规则当前正在采集的片段</summary>
    Task<CollectionSegment?> GetActiveByRuleAsync(string ruleId, CancellationToken ct);
    
    /// <summary>删除指定时间之前的片段</summary>
    Task<int> DeleteBeforeAsync(long cutoffUtc, CancellationToken ct);
}

// ========================================
// 数据分析相关接口 (v47)
// ========================================

/// <summary>
/// 工作周期仓储接口
/// </summary>
public interface IWorkCycleRepository
{
    /// <summary>创建周期记录，返回ID</summary>
    Task<long> CreateAsync(WorkCycle cycle, CancellationToken ct);
    
    /// <summary>批量创建</summary>
    Task<int> CreateBatchAsync(IEnumerable<WorkCycle> cycles, CancellationToken ct);
    
    /// <summary>获取单个周期</summary>
    Task<WorkCycle?> GetAsync(long id, CancellationToken ct);
    
    /// <summary>查询周期</summary>
    Task<IReadOnlyList<WorkCycle>> QueryAsync(WorkCycleQuery query, CancellationToken ct);
    
    /// <summary>获取设备最近N个周期</summary>
    Task<IReadOnlyList<WorkCycle>> GetRecentByDeviceAsync(string deviceId, int count, CancellationToken ct);
    
    /// <summary>获取设备异常周期</summary>
    Task<IReadOnlyList<WorkCycle>> GetAnomaliesByDeviceAsync(string deviceId, long? afterUtc, int limit, CancellationToken ct);
    
    /// <summary>删除周期</summary>
    Task DeleteAsync(long id, CancellationToken ct);
    
    /// <summary>删除指定时间之前的周期</summary>
    Task<int> DeleteBeforeAsync(long cutoffUtc, CancellationToken ct);
    
    /// <summary>获取周期统计</summary>
    Task<CycleStatsSummary?> GetStatsSummaryAsync(string deviceId, long? startUtc, long? endUtc, CancellationToken ct);
}

/// <summary>
/// 周期分析基线仓储接口
/// </summary>
public interface ICycleDeviceBaselineRepository
{
    /// <summary>获取设备基线</summary>
    Task<CycleDeviceBaseline?> GetAsync(string deviceId, string baselineType, CancellationToken ct);
    
    /// <summary>获取设备所有基线</summary>
    Task<IReadOnlyList<CycleDeviceBaseline>> GetAllByDeviceAsync(string deviceId, CancellationToken ct);
    
    /// <summary>保存基线 (Upsert)</summary>
    Task UpsertAsync(CycleDeviceBaseline baseline, CancellationToken ct);
    
    /// <summary>删除基线</summary>
    Task DeleteAsync(string deviceId, string baselineType, CancellationToken ct);
}


// ========================================
// 告警评估接口 (v56.1)
// ========================================

/// <summary>
/// v56.1: 告警评估器接口 - 用于评估遥测数据是否触发告警
/// </summary>
public interface IAlarmEvaluator
{
    Task<AlarmRecord?> EvaluateAsync(TelemetryPoint point, CancellationToken ct);
    Task RefreshRulesAsync(CancellationToken ct);
    int RuleCount { get; }
}

// ========================================
// 设备健康快照接口 (v60)
// ========================================

/// <summary>
/// v60: 设备健康快照仓储接口 - 存储历史健康评分
/// </summary>
public interface IDeviceHealthSnapshotRepository
{
    Task SaveAsync(DeviceHealthSnapshot snapshot, CancellationToken ct);
    Task SaveBatchAsync(IEnumerable<DeviceHealthSnapshot> snapshots, CancellationToken ct);
    Task<IReadOnlyList<DeviceHealthSnapshot>> GetHistoryAsync(string deviceId, long startTs, long endTs, CancellationToken ct);
    Task<IReadOnlyList<DeviceHealthSnapshot>> GetLatestAllAsync(CancellationToken ct);
    Task<int> DeleteBeforeAsync(long cutoffTs, CancellationToken ct);
}

/// <summary>
/// 设备健康快照记录
/// </summary>
public sealed record DeviceHealthSnapshot
{
    public required string DeviceId { get; init; }
    public long Timestamp { get; init; }
    public int Index { get; init; }
    public HealthLevel Level { get; init; }
    public int DeviationScore { get; init; }
    public int TrendScore { get; init; }
    public int StabilityScore { get; init; }
    public int AlarmScore { get; init; }
}

// ========================================
// 告警聚合接口 (v59)
// ========================================

/// <summary>
/// v59: 告警聚合组仓储接口
/// </summary>
public interface IAlarmGroupRepository
{
    Task<AlarmGroup?> GetAsync(string groupId, CancellationToken ct);
    Task<AlarmGroup?> FindActiveGroupAsync(string deviceId, string ruleId, CancellationToken ct);
    Task CreateAsync(AlarmGroup group, CancellationToken ct);
    Task UpdateAsync(AlarmGroup group, CancellationToken ct);
    Task AddAlarmToGroupAsync(string alarmId, string groupId, CancellationToken ct);
    Task<PagedResult<AlarmGroup>> QueryAsync(AlarmGroupQuery query, CancellationToken ct);
    Task<List<string>> GetChildAlarmIdsAsync(string groupId, CancellationToken ct);
    Task<int> GetOpenGroupCountAsync(string? deviceId, CancellationToken ct);
    Task SetStatusAsync(string groupId, AlarmStatus status, CancellationToken ct);
    Task AckGroupAsync(string groupId, string ackedBy, string? ackNote, CancellationToken ct);
    Task CloseGroupAsync(string groupId, CancellationToken ct);
}

// ========================================
// 健康评估增强接口 (v61)
// ========================================

/// <summary>
/// v61: 标签重要性配置仓储接口
/// </summary>
public interface ITagImportanceRepository
{
    /// <summary>获取所有配置（按优先级降序）</summary>
    Task<IReadOnlyList<TagImportanceConfig>> ListAsync(CancellationToken ct);

    /// <summary>获取启用的配置（按优先级降序）</summary>
    Task<IReadOnlyList<TagImportanceConfig>> ListEnabledAsync(CancellationToken ct);

    /// <summary>获取单个配置</summary>
    Task<TagImportanceConfig?> GetAsync(int id, CancellationToken ct);

    /// <summary>创建配置</summary>
    Task<int> CreateAsync(TagImportanceConfig config, CancellationToken ct);

    /// <summary>更新配置</summary>
    Task UpdateAsync(TagImportanceConfig config, CancellationToken ct);

    /// <summary>删除配置</summary>
    Task DeleteAsync(int id, CancellationToken ct);

    /// <summary>设置启用状态</summary>
    Task SetEnabledAsync(int id, bool enabled, CancellationToken ct);
}

/// <summary>
/// v61: 标签重要性匹配服务接口
/// </summary>
public interface ITagImportanceMatcher
{
    /// <summary>获取标签的重要性级别</summary>
    TagImportance GetImportance(string tagId);

    /// <summary>批量获取多个标签的重要性</summary>
    IReadOnlyDictionary<string, TagImportance> GetImportances(IEnumerable<string> tagIds);

    /// <summary>刷新配置缓存</summary>
    Task RefreshAsync(CancellationToken ct);
}

// ========================================
// P1: 多标签关联分析接口 (v62)
// ========================================

/// <summary>
/// v62: 标签关联规则仓储接口
/// </summary>
public interface ITagCorrelationRepository
{
    /// <summary>获取所有规则</summary>
    Task<IReadOnlyList<TagCorrelationRule>> ListAsync(CancellationToken ct);

    /// <summary>获取启用的规则</summary>
    Task<IReadOnlyList<TagCorrelationRule>> ListEnabledAsync(CancellationToken ct);

    /// <summary>按设备模式获取规则</summary>
    Task<IReadOnlyList<TagCorrelationRule>> ListByDevicePatternAsync(string deviceId, CancellationToken ct);

    /// <summary>获取单个规则</summary>
    Task<TagCorrelationRule?> GetAsync(int id, CancellationToken ct);

    /// <summary>创建规则</summary>
    Task<int> CreateAsync(TagCorrelationRule rule, CancellationToken ct);

    /// <summary>更新规则</summary>
    Task UpdateAsync(TagCorrelationRule rule, CancellationToken ct);

    /// <summary>删除规则</summary>
    Task DeleteAsync(int id, CancellationToken ct);

    /// <summary>设置启用状态</summary>
    Task SetEnabledAsync(int id, bool enabled, CancellationToken ct);
}

/// <summary>
/// v62: 关联分析服务接口
/// </summary>
public interface ICorrelationAnalyzer
{
    /// <summary>分析设备的标签关联异常</summary>
    Task<IReadOnlyList<CorrelationAnomaly>> AnalyzeAsync(
        string deviceId,
        IReadOnlyList<TelemetryPoint> recentData,
        CancellationToken ct);

    /// <summary>刷新规则缓存</summary>
    Task RefreshRulesAsync(CancellationToken ct);

    /// <summary>规则数量</summary>
    int RuleCount { get; }
}

// ========================================
// 电机故障预测接口 (v64)
// ========================================

/// <summary>
/// v64: 电机模型仓储接口
/// </summary>
public interface IMotorModelRepository
{
    /// <summary>获取所有电机模型</summary>
    Task<IReadOnlyList<MotorModel>> ListAsync(CancellationToken ct);

    /// <summary>获取单个电机模型</summary>
    Task<MotorModel?> GetAsync(string modelId, CancellationToken ct);

    /// <summary>创建电机模型</summary>
    Task CreateAsync(MotorModel model, CancellationToken ct);

    /// <summary>更新电机模型</summary>
    Task UpdateAsync(MotorModel model, CancellationToken ct);

    /// <summary>删除电机模型</summary>
    Task DeleteAsync(string modelId, CancellationToken ct);
}

/// <summary>
/// v64: 电机实例仓储接口
/// </summary>
public interface IMotorInstanceRepository
{
    /// <summary>获取所有电机实例</summary>
    Task<IReadOnlyList<MotorInstance>> ListAsync(CancellationToken ct);

    /// <summary>按设备获取电机实例</summary>
    Task<IReadOnlyList<MotorInstance>> ListByDeviceAsync(string deviceId, CancellationToken ct);

    /// <summary>按模型获取电机实例</summary>
    Task<IReadOnlyList<MotorInstance>> ListByModelAsync(string modelId, CancellationToken ct);

    /// <summary>获取单个电机实例</summary>
    Task<MotorInstance?> GetAsync(string instanceId, CancellationToken ct);

    /// <summary>获取电机实例详情（包含模型、映射、模式）</summary>
    Task<MotorInstanceDetail?> GetDetailAsync(string instanceId, CancellationToken ct);

    /// <summary>创建电机实例</summary>
    Task CreateAsync(MotorInstance instance, CancellationToken ct);

    /// <summary>更新电机实例</summary>
    Task UpdateAsync(MotorInstance instance, CancellationToken ct);

    /// <summary>删除电机实例</summary>
    Task DeleteAsync(string instanceId, CancellationToken ct);

    /// <summary>更新累计运行小时</summary>
    Task UpdateOperatingHoursAsync(string instanceId, double hours, CancellationToken ct);
}

/// <summary>
/// v64: 电机参数映射仓储接口
/// </summary>
public interface IMotorParameterMappingRepository
{
    /// <summary>获取电机实例的所有参数映射</summary>
    Task<IReadOnlyList<MotorParameterMapping>> ListByInstanceAsync(string instanceId, CancellationToken ct);

    /// <summary>获取单个映射</summary>
    Task<MotorParameterMapping?> GetAsync(string mappingId, CancellationToken ct);

    /// <summary>创建映射</summary>
    Task CreateAsync(MotorParameterMapping mapping, CancellationToken ct);

    /// <summary>批量创建映射</summary>
    Task CreateBatchAsync(IEnumerable<MotorParameterMapping> mappings, CancellationToken ct);

    /// <summary>更新映射</summary>
    Task UpdateAsync(MotorParameterMapping mapping, CancellationToken ct);

    /// <summary>删除映射</summary>
    Task DeleteAsync(string mappingId, CancellationToken ct);

    /// <summary>删除电机实例的所有映射</summary>
    Task DeleteByInstanceAsync(string instanceId, CancellationToken ct);
}

/// <summary>
/// v64: 操作模式仓储接口
/// </summary>
public interface IOperationModeRepository
{
    /// <summary>获取电机实例的所有操作模式</summary>
    Task<IReadOnlyList<OperationMode>> ListByInstanceAsync(string instanceId, CancellationToken ct);

    /// <summary>获取电机实例的启用操作模式</summary>
    Task<IReadOnlyList<OperationMode>> ListEnabledByInstanceAsync(string instanceId, CancellationToken ct);

    /// <summary>获取单个操作模式</summary>
    Task<OperationMode?> GetAsync(string modeId, CancellationToken ct);

    /// <summary>创建操作模式</summary>
    Task CreateAsync(OperationMode mode, CancellationToken ct);

    /// <summary>更新操作模式</summary>
    Task UpdateAsync(OperationMode mode, CancellationToken ct);

    /// <summary>删除操作模式</summary>
    Task DeleteAsync(string modeId, CancellationToken ct);

    /// <summary>设置启用状态</summary>
    Task SetEnabledAsync(string modeId, bool enabled, CancellationToken ct);

    /// <summary>删除电机实例的所有操作模式</summary>
    Task DeleteByInstanceAsync(string instanceId, CancellationToken ct);
}

/// <summary>
/// v64: 基线配置仓储接口
/// </summary>
public interface IBaselineProfileRepository
{
    /// <summary>获取操作模式的所有基线</summary>
    Task<IReadOnlyList<BaselineProfile>> ListByModeAsync(string modeId, CancellationToken ct);

    /// <summary>获取电机实例的所有基线</summary>
    Task<IReadOnlyList<BaselineProfile>> ListByInstanceAsync(string instanceId, CancellationToken ct);

    /// <summary>获取单个基线</summary>
    Task<BaselineProfile?> GetAsync(string baselineId, CancellationToken ct);

    /// <summary>获取指定模式和参数的基线</summary>
    Task<BaselineProfile?> GetByModeAndParameterAsync(string modeId, MotorParameter parameter, CancellationToken ct);

    /// <summary>创建基线</summary>
    Task CreateAsync(BaselineProfile baseline, CancellationToken ct);

    /// <summary>批量创建/更新基线 (按 modeId + parameter 去重)</summary>
    Task SaveBatchAsync(IEnumerable<BaselineProfile> baselines, CancellationToken ct);

    /// <summary>更新基线</summary>
    Task UpdateAsync(BaselineProfile baseline, CancellationToken ct);

    /// <summary>删除基线</summary>
    Task DeleteAsync(string baselineId, CancellationToken ct);

    /// <summary>删除操作模式的所有基线</summary>
    Task DeleteByModeAsync(string modeId, CancellationToken ct);

    /// <summary>删除电机实例的所有基线</summary>
    Task DeleteByInstanceAsync(string instanceId, CancellationToken ct);

    /// <summary>获取基线数量统计</summary>
    Task<int> GetCountByInstanceAsync(string instanceId, CancellationToken ct);
}

// ========================================
// Edge 配置管理接口 (v65)
// ========================================

/// <summary>
/// v65: Edge 配置仓储接口
/// </summary>
public interface IEdgeConfigRepository
{
    /// <summary>获取 Edge 配置</summary>
    Task<EdgeConfigDto?> GetAsync(string edgeId, CancellationToken ct);

    /// <summary>获取所有 Edge 列表</summary>
    Task<IReadOnlyList<EdgeSummaryDto>> ListAllAsync(CancellationToken ct);

    /// <summary>创建或更新 Edge 配置</summary>
    Task UpsertAsync(EdgeConfigDto config, CancellationToken ct);

    /// <summary>删除 Edge 配置</summary>
    Task DeleteAsync(string edgeId, CancellationToken ct);
}

/// <summary>
/// v65: 标签处理配置仓储接口
/// </summary>
public interface ITagProcessingConfigRepository
{
    /// <summary>获取 Edge 的所有标签配置（分页）</summary>
    Task<PagedTagConfigResult> ListByEdgeAsync(string edgeId, int page, int pageSize, string? search, CancellationToken ct);

    /// <summary>获取单个标签配置</summary>
    Task<TagProcessingConfigDto?> GetAsync(string edgeId, string tagId, CancellationToken ct);

    /// <summary>批量创建或更新标签配置</summary>
    Task BatchUpsertAsync(string edgeId, IEnumerable<TagProcessingConfigDto> configs, CancellationToken ct);

    /// <summary>删除标签配置</summary>
    Task DeleteAsync(string edgeId, string tagId, CancellationToken ct);

    /// <summary>删除 Edge 的所有标签配置</summary>
    Task DeleteByEdgeAsync(string edgeId, CancellationToken ct);
}

/// <summary>
/// v65: Edge 状态仓储接口
/// </summary>
public interface IEdgeStatusRepository
{
    /// <summary>获取 Edge 状态</summary>
    Task<EdgeStatusDto?> GetAsync(string edgeId, CancellationToken ct);

    /// <summary>更新 Edge 状态（心跳）</summary>
    Task UpdateAsync(EdgeStatusDto status, CancellationToken ct);

    /// <summary>获取所有 Edge 状态</summary>
    Task<IReadOnlyList<EdgeStatusDto>> ListAllAsync(CancellationToken ct);
}

/// <summary>
/// v65: Edge 配置变更通知服务接口
/// </summary>
public interface IEdgeNotificationService
{
    /// <summary>通知 Edge 配置已变更</summary>
    Task NotifyConfigChangedAsync(string edgeId, CancellationToken ct);
}
