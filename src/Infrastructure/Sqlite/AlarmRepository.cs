using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.Sqlite;

public sealed class AlarmRepository : IAlarmRepository
{
    private readonly IDbExecutor _db;
    private readonly ILogger<AlarmRepository> _logger;

    public AlarmRepository(IDbExecutor db, ILogger<AlarmRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task CreateAsync(AlarmRecord alarm, CancellationToken ct)
    {
        const string sql = @"
INSERT INTO alarm (
    alarm_id, device_id, tag_id, ts, severity, code, message, status, created_utc, updated_utc
) VALUES (
    @AlarmId, @DeviceId, @TagId, @Ts, @Severity, @Code, @Message, @Status, @CreatedUtc, @UpdatedUtc
);";

        await _db.ExecuteNonQueryAsync(sql, new
        {
            alarm.AlarmId,
            alarm.DeviceId,
            alarm.TagId,
            alarm.Ts,
            alarm.Severity,
            alarm.Code,
            alarm.Message,
            Status = (int)alarm.Status,
            alarm.CreatedUtc,
            alarm.UpdatedUtc
        }, ct);

        _logger.LogInformation("Alarm created: {AlarmId} device={DeviceId} tag={TagId} severity={Severity} code={Code}",
            alarm.AlarmId, alarm.DeviceId, alarm.TagId, alarm.Severity, alarm.Code);
    }

    public async Task AckAsync(AlarmAckRequest request, CancellationToken ct)
    {
        // 状态流转：Closed 不允许 Ack；Open -> Acknowledged；Acknowledged 重复 Ack 允许更新备注/人员/时间
        var existing = await GetAsync(request.AlarmId, ct);
        if (existing is null)
        {
            _logger.LogWarning("AckAsync: alarm not found {AlarmId}", request.AlarmId);
            return;
        }

        if (existing.Status == AlarmStatus.Closed)
        {
            _logger.LogWarning("AckAsync: cannot ack closed alarm {AlarmId}", request.AlarmId);
            return;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 使用单个 ExecuteNonQueryAsync 执行多语句：在 SQLite 中同一命令可包含多条语句；
        // 配合 DbExecutor 的写锁，达到原子性（MVP 级别的事务保障）。
        const string sql = @"
UPDATE alarm
SET status = @NewStatus,
    updated_utc = @UpdatedUtc
WHERE alarm_id = @AlarmId;

INSERT INTO alarm_ack (alarm_id, acked_by, ack_note, acked_utc)
VALUES (@AlarmId, @AckedBy, @AckNote, @AckedUtc)
ON CONFLICT(alarm_id) DO UPDATE SET
    acked_by = excluded.acked_by,
    ack_note = excluded.ack_note,
    acked_utc = excluded.acked_utc;";

        await _db.ExecuteNonQueryAsync(sql, new
        {
            AlarmId = request.AlarmId,
            NewStatus = (int)AlarmStatus.Acknowledged,
            UpdatedUtc = now,
            AckedBy = request.AckedBy,
            AckNote = request.AckNote,
            AckedUtc = now
        }, ct);

        _logger.LogInformation("Alarm acked: {AlarmId} by={AckedBy}", request.AlarmId, request.AckedBy);
    }

    public async Task CloseAsync(string alarmId, CancellationToken ct)
    {
        var existing = await GetAsync(alarmId, ct);
        if (existing is null)
        {
            _logger.LogWarning("CloseAsync: alarm not found {AlarmId}", alarmId);
            return;
        }

        if (existing.Status == AlarmStatus.Closed)
        {
            _logger.LogInformation("CloseAsync: alarm already closed {AlarmId}", alarmId);
            return;
        }

        const string sql = @"
UPDATE alarm
SET status = @Status,
    updated_utc = @UpdatedUtc
WHERE alarm_id = @AlarmId;";

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.ExecuteNonQueryAsync(sql, new
        {
            AlarmId = alarmId,
            Status = (int)AlarmStatus.Closed,
            UpdatedUtc = now
        }, ct);

        _logger.LogInformation("Alarm closed: {AlarmId}", alarmId);
    }

    public async Task<AlarmRecord?> GetAsync(string alarmId, CancellationToken ct)
    {
        const string sql = @"
SELECT a.alarm_id, a.device_id, a.tag_id, a.ts, a.severity, a.code, a.message,
       a.status, a.created_utc, a.updated_utc,
       k.acked_by, k.acked_utc, k.ack_note
FROM alarm a
LEFT JOIN alarm_ack k ON a.alarm_id = k.alarm_id
WHERE a.alarm_id = @AlarmId;";

        return await _db.QuerySingleAsync(sql, MapAlarm, new { AlarmId = alarmId }, ct);
    }

    public async Task<PagedResult<AlarmRecord>> QueryAsync(AlarmQuery query, CancellationToken ct)
    {
        // 说明：
        // - 使用 Keyset Pagination：ORDER BY a.ts DESC, a.rowid DESC
        // - PageToken.LastTs 对应 a.ts；PageToken.LastSeq 对应 a.rowid
        var conditions = new List<string>();
        var parameters = new Dictionary<string, object?>();

        if (!string.IsNullOrWhiteSpace(query.DeviceId))
        {
            conditions.Add("a.device_id = @DeviceId");
            parameters["DeviceId"] = query.DeviceId;
        }

        if (query.Status.HasValue)
        {
            conditions.Add("a.status = @Status");
            parameters["Status"] = (int)query.Status.Value;
        }

        if (query.MinSeverity.HasValue)
        {
            conditions.Add("a.severity >= @MinSeverity");
            parameters["MinSeverity"] = query.MinSeverity.Value;
        }

        if (query.StartTs.HasValue)
        {
            conditions.Add("a.ts >= @StartTs");
            parameters["StartTs"] = query.StartTs.Value;
        }

        if (query.EndTs.HasValue)
        {
            conditions.Add("a.ts <= @EndTs");
            parameters["EndTs"] = query.EndTs.Value;
        }

        if (query.After is not null)
        {
            // keyset: (ts DESC, rowid DESC)
            conditions.Add("(a.ts < @AfterTs OR (a.ts = @AfterTs AND a.rowid < @AfterRowId))");
            parameters["AfterTs"] = query.After.LastTs;
            parameters["AfterRowId"] = query.After.LastSeq;
        }

        var whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : string.Empty;

        var limit = query.Limit <= 0 ? 100 : Math.Min(query.Limit, 1000);
        parameters["Limit"] = limit;

        var sql = $@"
SELECT a.alarm_id, a.device_id, a.tag_id, a.ts, a.severity, a.code, a.message,
       a.status, a.created_utc, a.updated_utc,
       k.acked_by, k.acked_utc, k.ack_note
FROM alarm a
LEFT JOIN alarm_ack k ON a.alarm_id = k.alarm_id
{whereClause}
ORDER BY a.ts DESC, a.rowid DESC
LIMIT @Limit;";

        var items = await _db.QueryAsync(sql, MapAlarm, parameters, ct);

        // totalCount：为了简单与准确，单独 COUNT（可接受，limit 默认 100）
        var countSql = $@"
SELECT COUNT(1)
FROM alarm a
{whereClause};";

        var totalCount = await _db.ExecuteScalarAsync<long>(countSql, parameters, ct);

        if (items.Count == 0)
        {
            return PagedResult<AlarmRecord>.Empty() with
            {
                TotalCount = (int)Math.Min(totalCount, int.MaxValue)
            };
        }

        // 下一页 token：最后一条记录的 (ts, rowid)
        // rowid 需要额外查询：使用 (alarm_id) 反查 rowid
        var last = items[^1];
        const string rowidSql = @"SELECT rowid FROM alarm WHERE alarm_id = @AlarmId;";
        var lastRowId = await _db.ExecuteScalarAsync<long>(rowidSql, new { AlarmId = last.AlarmId }, ct);

        var hasMore = items.Count == limit;

        return new PagedResult<AlarmRecord>
        {
            Items = items,
            NextToken = hasMore ? new PageToken(last.Ts, lastRowId) : null,
            HasMore = hasMore,
            TotalCount = (int)Math.Min(totalCount, int.MaxValue)
        };
    }

    public async Task<int> GetOpenCountAsync(string? deviceId, CancellationToken ct)
    {
        var sql = @"
SELECT COUNT(1)
FROM alarm
WHERE status = 0";

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

    public async Task<int> DeleteBeforeAsync(long cutoffTs, CancellationToken ct)
    {
        // 先删除关联的 alarm_ack 记录
        const string deleteAckSql = @"
DELETE FROM alarm_ack
WHERE alarm_id IN (SELECT alarm_id FROM alarm WHERE ts < @CutoffTs);";

        const string deleteAlarmSql = "DELETE FROM alarm WHERE ts < @CutoffTs;";

        await _db.ExecuteNonQueryAsync(deleteAckSql, new { CutoffTs = cutoffTs }, ct);
        return await _db.ExecuteNonQueryAsync(deleteAlarmSql, new { CutoffTs = cutoffTs }, ct);
    }

    private static AlarmRecord MapAlarm(SqliteDataReader reader)
    {
        string? tagId = null;
        if (!reader.IsDBNull(reader.GetOrdinal("tag_id")))
            tagId = reader.GetString(reader.GetOrdinal("tag_id"));

        string? ackedBy = null;
        if (!reader.IsDBNull(reader.GetOrdinal("acked_by")))
            ackedBy = reader.GetString(reader.GetOrdinal("acked_by"));

        long? ackedUtc = null;
        if (!reader.IsDBNull(reader.GetOrdinal("acked_utc")))
            ackedUtc = reader.GetInt64(reader.GetOrdinal("acked_utc"));

        string? ackNote = null;
        if (!reader.IsDBNull(reader.GetOrdinal("ack_note")))
            ackNote = reader.GetString(reader.GetOrdinal("ack_note"));

        var statusInt = reader.GetInt32(reader.GetOrdinal("status"));
        var status = Enum.IsDefined(typeof(AlarmStatus), statusInt)
            ? (AlarmStatus)statusInt
            : AlarmStatus.Open;

        return new AlarmRecord
        {
            AlarmId = reader.GetString(reader.GetOrdinal("alarm_id")),
            DeviceId = reader.GetString(reader.GetOrdinal("device_id")),
            TagId = tagId,
            Ts = reader.GetInt64(reader.GetOrdinal("ts")),
            Severity = reader.GetInt32(reader.GetOrdinal("severity")),
            Code = reader.GetString(reader.GetOrdinal("code")),
            Message = reader.GetString(reader.GetOrdinal("message")),
            Status = status,
            CreatedUtc = reader.GetInt64(reader.GetOrdinal("created_utc")),
            UpdatedUtc = reader.GetInt64(reader.GetOrdinal("updated_utc")),
            AckedBy = ackedBy,
            AckedUtc = ackedUtc,
            AckNote = ackNote
        };
    }
}
