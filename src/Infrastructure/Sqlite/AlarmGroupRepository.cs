using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.Sqlite;

/// <summary>
/// v59: 告警聚合组存储实现
/// </summary>
public sealed class AlarmGroupRepository : IAlarmGroupRepository
{
    private readonly IDbExecutor _db;
    private readonly ILogger<AlarmGroupRepository> _logger;

    public AlarmGroupRepository(IDbExecutor db, ILogger<AlarmGroupRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<AlarmGroup?> GetAsync(string groupId, CancellationToken ct)
    {
        const string sql = @"
SELECT group_id, device_id, tag_id, rule_id, severity, code, message,
       alarm_count, first_occurred_utc, last_occurred_utc,
       aggregate_status, created_utc, updated_utc
FROM alarm_group
WHERE group_id = @GroupId;";

        return await _db.QuerySingleAsync(sql, MapAlarmGroup, new { GroupId = groupId }, ct);
    }

    public async Task<AlarmGroup?> FindActiveGroupAsync(string deviceId, string ruleId, CancellationToken ct)
    {
        // 查找设备+规则对应的活跃聚合组（status != Closed）
        const string sql = @"
SELECT group_id, device_id, tag_id, rule_id, severity, code, message,
       alarm_count, first_occurred_utc, last_occurred_utc,
       aggregate_status, created_utc, updated_utc
FROM alarm_group
WHERE device_id = @DeviceId
  AND rule_id = @RuleId
  AND aggregate_status <> 2
ORDER BY last_occurred_utc DESC
LIMIT 1;";

        return await _db.QuerySingleAsync(sql, MapAlarmGroup, new { DeviceId = deviceId, RuleId = ruleId }, ct);
    }

    public async Task CreateAsync(AlarmGroup group, CancellationToken ct)
    {
        const string sql = @"
INSERT INTO alarm_group (
    group_id, device_id, tag_id, rule_id, severity, code, message,
    alarm_count, first_occurred_utc, last_occurred_utc,
    aggregate_status, created_utc, updated_utc
) VALUES (
    @GroupId, @DeviceId, @TagId, @RuleId, @Severity, @Code, @Message,
    @AlarmCount, @FirstOccurredUtc, @LastOccurredUtc,
    @AggregateStatus, @CreatedUtc, @UpdatedUtc
);";

        await _db.ExecuteNonQueryAsync(sql, new
        {
            group.GroupId,
            group.DeviceId,
            group.TagId,
            group.RuleId,
            group.Severity,
            group.Code,
            group.Message,
            group.AlarmCount,
            group.FirstOccurredUtc,
            group.LastOccurredUtc,
            AggregateStatus = (int)group.AggregateStatus,
            group.CreatedUtc,
            group.UpdatedUtc
        }, ct);

        _logger.LogDebug("AlarmGroup created: {GroupId} device={DeviceId} rule={RuleId}",
            group.GroupId, group.DeviceId, group.RuleId);
    }

    public async Task UpdateAsync(AlarmGroup group, CancellationToken ct)
    {
        const string sql = @"
UPDATE alarm_group
SET severity = @Severity,
    message = @Message,
    alarm_count = @AlarmCount,
    last_occurred_utc = @LastOccurredUtc,
    aggregate_status = @AggregateStatus,
    updated_utc = @UpdatedUtc
WHERE group_id = @GroupId;";

        await _db.ExecuteNonQueryAsync(sql, new
        {
            group.GroupId,
            group.Severity,
            group.Message,
            group.AlarmCount,
            group.LastOccurredUtc,
            AggregateStatus = (int)group.AggregateStatus,
            group.UpdatedUtc
        }, ct);
    }

    public async Task AddAlarmToGroupAsync(string alarmId, string groupId, CancellationToken ct)
    {
        const string sql = @"
INSERT OR IGNORE INTO alarm_to_group (alarm_id, group_id, added_utc)
VALUES (@AlarmId, @GroupId, @AddedUtc);";

        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.ExecuteNonQueryAsync(sql, new { AlarmId = alarmId, GroupId = groupId, AddedUtc = nowUtc }, ct);
    }

    public async Task<PagedResult<AlarmGroup>> QueryAsync(AlarmGroupQuery query, CancellationToken ct)
    {
        var conditions = new List<string>();
        var parameters = new Dictionary<string, object?>();

        if (!string.IsNullOrWhiteSpace(query.DeviceId))
        {
            conditions.Add("device_id = @DeviceId");
            parameters["DeviceId"] = query.DeviceId;
        }

        if (query.Status.HasValue)
        {
            conditions.Add("aggregate_status = @Status");
            parameters["Status"] = (int)query.Status.Value;
        }

        if (query.MinSeverity.HasValue)
        {
            conditions.Add("severity >= @MinSeverity");
            parameters["MinSeverity"] = query.MinSeverity.Value;
        }

        if (query.StartTs.HasValue)
        {
            conditions.Add("first_occurred_utc >= @StartTs");
            parameters["StartTs"] = query.StartTs.Value;
        }

        if (query.EndTs.HasValue)
        {
            conditions.Add("last_occurred_utc <= @EndTs");
            parameters["EndTs"] = query.EndTs.Value;
        }

        if (query.After is not null)
        {
            // keyset: (last_occurred_utc DESC)
            conditions.Add("last_occurred_utc < @AfterTs");
            parameters["AfterTs"] = query.After.LastTs;
        }

        var whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : string.Empty;

        var limit = query.Limit <= 0 ? 50 : Math.Min(query.Limit, 500);
        parameters["Limit"] = limit;

        var sql = $@"
SELECT group_id, device_id, tag_id, rule_id, severity, code, message,
       alarm_count, first_occurred_utc, last_occurred_utc,
       aggregate_status, created_utc, updated_utc,
       COUNT(*) OVER() as total_count
FROM alarm_group
{whereClause}
ORDER BY last_occurred_utc DESC
LIMIT @Limit;";

        long totalCount = 0;
        var items = await _db.QueryAsync(sql, reader =>
        {
            if (totalCount == 0)
            {
                totalCount = reader.GetInt64(reader.GetOrdinal("total_count"));
            }
            return MapAlarmGroup(reader);
        }, parameters, ct);

        if (items.Count == 0)
        {
            return PagedResult<AlarmGroup>.Empty() with { TotalCount = 0 };
        }

        var hasMore = items.Count == limit;

        return new PagedResult<AlarmGroup>
        {
            Items = items,
            NextToken = hasMore ? new PageToken(items[^1].LastOccurredUtc, 0) : null,
            HasMore = hasMore,
            TotalCount = (int)Math.Min(totalCount, int.MaxValue)
        };
    }

    public async Task<List<string>> GetChildAlarmIdsAsync(string groupId, CancellationToken ct)
    {
        const string sql = @"
SELECT alarm_id
FROM alarm_to_group
WHERE group_id = @GroupId
ORDER BY added_utc DESC;";

        return await _db.QueryAsync(sql,
            reader => reader.GetString(reader.GetOrdinal("alarm_id")),
            new { GroupId = groupId }, ct);
    }

    public async Task<int> GetOpenGroupCountAsync(string? deviceId, CancellationToken ct)
    {
        var sql = @"
SELECT COUNT(1)
FROM alarm_group
WHERE aggregate_status = 0";

        object parameters;

        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            sql += " AND device_id = @DeviceId";
            parameters = new { DeviceId = deviceId };
        }
        else
        {
            parameters = new { };
        }

        var count = await _db.ExecuteScalarAsync<long>(sql, parameters, ct);
        return (int)Math.Min(count, int.MaxValue);
    }

    public async Task SetStatusAsync(string groupId, AlarmStatus status, CancellationToken ct)
    {
        const string sql = @"
UPDATE alarm_group
SET aggregate_status = @Status,
    updated_utc = @UpdatedUtc
WHERE group_id = @GroupId;";

        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.ExecuteNonQueryAsync(sql, new
        {
            GroupId = groupId,
            Status = (int)status,
            UpdatedUtc = nowUtc
        }, ct);
    }

    public async Task AckGroupAsync(string groupId, string ackedBy, string? ackNote, CancellationToken ct)
    {
        // 更新聚合组状态为 Acknowledged
        await SetStatusAsync(groupId, AlarmStatus.Acknowledged, ct);

        // 获取子告警并批量确认
        var childAlarmIds = await GetChildAlarmIdsAsync(groupId, ct);
        if (childAlarmIds.Count == 0)
        {
            _logger.LogDebug("No child alarms to ack for group {GroupId}", groupId);
            return;
        }

        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 批量更新告警状态（使用 IN 子句优化）
        var updateAlarmSql = $@"
UPDATE alarm
SET status = @Status,
    updated_utc = @UpdatedUtc
WHERE alarm_id IN (SELECT alarm_id FROM alarm_to_group WHERE group_id = @GroupId)
  AND status <> 2;";

        await _db.ExecuteNonQueryAsync(updateAlarmSql, new
        {
            Status = (int)AlarmStatus.Acknowledged,
            UpdatedUtc = nowUtc,
            GroupId = groupId
        }, ct);

        // 批量插入/更新 ack 记录
        foreach (var alarmId in childAlarmIds)
        {
            const string ackSql = @"
INSERT INTO alarm_ack (alarm_id, acked_by, ack_note, acked_utc)
VALUES (@AlarmId, @AckedBy, @AckNote, @AckedUtc)
ON CONFLICT(alarm_id) DO UPDATE SET
    acked_by = excluded.acked_by,
    ack_note = excluded.ack_note,
    acked_utc = excluded.acked_utc;";

            await _db.ExecuteNonQueryAsync(ackSql, new
            {
                AlarmId = alarmId,
                AckedBy = ackedBy,
                AckNote = ackNote,
                AckedUtc = nowUtc
            }, ct);
        }

        _logger.LogInformation("AlarmGroup acked: {GroupId} by={AckedBy}, {Count} child alarms",
            groupId, ackedBy, childAlarmIds.Count);
    }

    public async Task CloseGroupAsync(string groupId, CancellationToken ct)
    {
        // 更新聚合组状态为 Closed
        await SetStatusAsync(groupId, AlarmStatus.Closed, ct);

        // 批量关闭子告警
        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        const string sql = @"
UPDATE alarm
SET status = @Status,
    updated_utc = @UpdatedUtc
WHERE alarm_id IN (SELECT alarm_id FROM alarm_to_group WHERE group_id = @GroupId)
  AND status <> 2;";

        var affected = await _db.ExecuteNonQueryAsync(sql, new
        {
            Status = (int)AlarmStatus.Closed,
            UpdatedUtc = nowUtc,
            GroupId = groupId
        }, ct);

        _logger.LogInformation("AlarmGroup closed: {GroupId}, {Count} child alarms closed", groupId, affected);
    }

    private static AlarmGroup MapAlarmGroup(SqliteDataReader reader)
    {
        string? tagId = null;
        if (!reader.IsDBNull(reader.GetOrdinal("tag_id")))
            tagId = reader.GetString(reader.GetOrdinal("tag_id"));

        string? code = null;
        if (!reader.IsDBNull(reader.GetOrdinal("code")))
            code = reader.GetString(reader.GetOrdinal("code"));

        string? message = null;
        if (!reader.IsDBNull(reader.GetOrdinal("message")))
            message = reader.GetString(reader.GetOrdinal("message"));

        var statusInt = reader.GetInt32(reader.GetOrdinal("aggregate_status"));
        var status = Enum.IsDefined(typeof(AlarmStatus), statusInt)
            ? (AlarmStatus)statusInt
            : AlarmStatus.Open;

        return new AlarmGroup
        {
            GroupId = reader.GetString(reader.GetOrdinal("group_id")),
            DeviceId = reader.GetString(reader.GetOrdinal("device_id")),
            TagId = tagId,
            RuleId = reader.GetString(reader.GetOrdinal("rule_id")),
            Severity = reader.GetInt32(reader.GetOrdinal("severity")),
            Code = code,
            Message = message,
            AlarmCount = reader.GetInt32(reader.GetOrdinal("alarm_count")),
            FirstOccurredUtc = reader.GetInt64(reader.GetOrdinal("first_occurred_utc")),
            LastOccurredUtc = reader.GetInt64(reader.GetOrdinal("last_occurred_utc")),
            AggregateStatus = status,
            CreatedUtc = reader.GetInt64(reader.GetOrdinal("created_utc")),
            UpdatedUtc = reader.GetInt64(reader.GetOrdinal("updated_utc"))
        };
    }
}
