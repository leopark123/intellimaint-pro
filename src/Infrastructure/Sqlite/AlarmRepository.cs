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

    /// <summary>批量获取告警（优化N+1查询）</summary>
    public async Task<IReadOnlyList<AlarmRecord>> GetByIdsAsync(IEnumerable<string> alarmIds, CancellationToken ct)
    {
        var idList = alarmIds.ToList();
        if (idList.Count == 0)
            return Array.Empty<AlarmRecord>();

        // SQLite 使用 IN 子句进行批量查询
        var placeholders = string.Join(",", idList.Select((_, i) => $"@Id{i}"));
        var sql = $@"
SELECT a.alarm_id, a.device_id, a.tag_id, a.ts, a.severity, a.code, a.message,
       a.status, a.created_utc, a.updated_utc,
       k.acked_by, k.acked_utc, k.ack_note
FROM alarm a
LEFT JOIN alarm_ack k ON a.alarm_id = k.alarm_id
WHERE a.alarm_id IN ({placeholders})
ORDER BY a.ts DESC;";

        var parameters = new Dictionary<string, object?>();
        for (var i = 0; i < idList.Count; i++)
        {
            parameters[$"Id{i}"] = idList[i];
        }

        return await _db.QueryAsync(sql, MapAlarm, parameters, ct);
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

        // v56.1: 使用窗口函数 COUNT(*) OVER() 和子查询获取 rowid，避免多次查询
        var sql = $@"
SELECT a.alarm_id, a.device_id, a.tag_id, a.ts, a.severity, a.code, a.message,
       a.status, a.created_utc, a.updated_utc, a.rowid,
       k.acked_by, k.acked_utc, k.ack_note,
       COUNT(*) OVER() as total_count
FROM alarm a
LEFT JOIN alarm_ack k ON a.alarm_id = k.alarm_id
{whereClause}
ORDER BY a.ts DESC, a.rowid DESC
LIMIT @Limit;";

        long totalCount = 0;
        long lastRowId = 0;
        var items = await _db.QueryAsync(sql, reader =>
        {
            // 每行都有 total_count 和 rowid，只取一次 total_count
            if (totalCount == 0)
            {
                totalCount = reader.GetInt64(reader.GetOrdinal("total_count"));
            }
            lastRowId = reader.GetInt64(reader.GetOrdinal("rowid"));
            return MapAlarm(reader);
        }, parameters, ct);

        if (items.Count == 0)
        {
            return PagedResult<AlarmRecord>.Empty() with
            {
                TotalCount = 0
            };
        }

        var hasMore = items.Count == limit;

        return new PagedResult<AlarmRecord>
        {
            Items = items,
            NextToken = hasMore ? new PageToken(items[^1].Ts, lastRowId) : null,
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

    public async Task<AlarmStatusCounts> GetStatusCountsAsync(string? deviceId, CancellationToken ct)
    {
        var sql = @"
SELECT
    SUM(CASE WHEN status = 0 THEN 1 ELSE 0 END) as open_count,
    SUM(CASE WHEN status = 1 THEN 1 ELSE 0 END) as acked_count,
    SUM(CASE WHEN status = 2 THEN 1 ELSE 0 END) as closed_count
FROM alarm";

        object parameters;

        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            sql += " WHERE device_id = @DeviceId";
            parameters = new { DeviceId = deviceId };
        }
        else
        {
            parameters = new { };
        }

        var result = await _db.QuerySingleAsync(sql, reader => new AlarmStatusCounts
        {
            OpenCount = (int)Math.Min(reader.IsDBNull(0) ? 0 : reader.GetInt64(0), int.MaxValue),
            AcknowledgedCount = (int)Math.Min(reader.IsDBNull(1) ? 0 : reader.GetInt64(1), int.MaxValue),
            ClosedCount = (int)Math.Min(reader.IsDBNull(2) ? 0 : reader.GetInt64(2), int.MaxValue)
        }, parameters, ct);

        return result ?? new AlarmStatusCounts();
    }

    /// <summary>批量获取设备的未关闭告警数量（优化N+1查询）</summary>
    public async Task<IReadOnlyDictionary<string, int>> GetOpenCountByDevicesAsync(IEnumerable<string> deviceIds, CancellationToken ct)
    {
        var idList = deviceIds.ToList();
        if (idList.Count == 0)
            return new Dictionary<string, int>();

        // 使用 GROUP BY 进行批量查询
        var placeholders = string.Join(",", idList.Select((_, i) => $"@Id{i}"));
        var sql = $@"
SELECT device_id, COUNT(1) as cnt
FROM alarm
WHERE status = 0 AND device_id IN ({placeholders})
GROUP BY device_id;";

        var parameters = new Dictionary<string, object?>();
        for (var i = 0; i < idList.Count; i++)
        {
            parameters[$"Id{i}"] = idList[i];
        }

        var result = new Dictionary<string, int>();
        var rows = await _db.QueryAsync(sql, reader =>
        {
            var deviceId = reader.GetString(0);
            var cnt = reader.GetInt64(1);
            return (deviceId, count: (int)Math.Min(cnt, int.MaxValue));
        }, parameters, ct);

        foreach (var (deviceId, count) in rows)
        {
            result[deviceId] = count;
        }

        // 确保所有请求的 deviceId 都有返回值（没有告警的设备返回 0）
        foreach (var deviceId in idList)
        {
            if (!result.ContainsKey(deviceId))
                result[deviceId] = 0;
        }

        return result;
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

    public async Task<bool> HasUnclosedByCodeAsync(string code, CancellationToken ct)
    {
        const string sql = @"
SELECT COUNT(*)
FROM alarm
WHERE code = @Code AND status <> 2;";

        var count = await _db.ExecuteScalarAsync<long>(sql, new { Code = code }, ct);
        return count > 0;
    }

    public async Task<IReadOnlyList<AlarmTrendBucket>> GetTrendAsync(AlarmTrendQuery query, CancellationToken ct)
    {
        var conditions = new List<string>();
        var parameters = new Dictionary<string, object?>();

        if (!string.IsNullOrWhiteSpace(query.DeviceId))
        {
            conditions.Add("device_id = @DeviceId");
            parameters["DeviceId"] = query.DeviceId;
        }

        if (query.StartTs.HasValue)
        {
            conditions.Add("ts >= @StartTs");
            parameters["StartTs"] = query.StartTs.Value;
        }

        if (query.EndTs.HasValue)
        {
            conditions.Add("ts <= @EndTs");
            parameters["EndTs"] = query.EndTs.Value;
        }

        var whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : string.Empty;
        var bucketSize = query.BucketSizeMs > 0 ? query.BucketSizeMs : 3600000;
        var limit = query.Limit > 0 ? Math.Min(query.Limit ?? 168, 500) : 168;

        parameters["BucketSize"] = bucketSize;
        parameters["Limit"] = limit;

        // SQLite uses integer division for time bucketing
        var sql = $@"
SELECT
    (ts / @BucketSize) * @BucketSize AS bucket,
    device_id,
    COUNT(*) AS total_count,
    SUM(CASE WHEN status = 0 THEN 1 ELSE 0 END) AS open_count,
    SUM(CASE WHEN severity >= 4 THEN 1 ELSE 0 END) AS critical_count,
    SUM(CASE WHEN severity = 2 OR severity = 3 THEN 1 ELSE 0 END) AS warning_count
FROM alarm
{whereClause}
GROUP BY (ts / @BucketSize) * @BucketSize, device_id
ORDER BY bucket DESC
LIMIT @Limit;";

        var results = await _db.QueryAsync(sql, reader =>
        {
            return new AlarmTrendBucket
            {
                Bucket = reader.GetInt64(reader.GetOrdinal("bucket")),
                DeviceId = reader.IsDBNull(reader.GetOrdinal("device_id"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("device_id")),
                TotalCount = reader.GetInt32(reader.GetOrdinal("total_count")),
                OpenCount = reader.GetInt32(reader.GetOrdinal("open_count")),
                CriticalCount = reader.GetInt32(reader.GetOrdinal("critical_count")),
                WarningCount = reader.GetInt32(reader.GetOrdinal("warning_count"))
            };
        }, parameters, ct);

        return results;
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
