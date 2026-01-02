using IntelliMaint.Core.Contracts;

namespace IntelliMaint.Core.Abstractions;

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
    Task<PagedResult<AlarmRecord>> QueryAsync(AlarmQuery query, CancellationToken ct);
    Task<int> GetOpenCountAsync(string? deviceId, CancellationToken ct);
    Task<int> DeleteBeforeAsync(long cutoffTs, CancellationToken ct);
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
