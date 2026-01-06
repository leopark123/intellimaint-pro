using Dapper;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.TimescaleDb;

/// <summary>
/// TimescaleDB Alarm repository implementation
/// </summary>
public sealed class AlarmRepository : IAlarmRepository
{
    private readonly INpgsqlConnectionFactory _factory;
    private readonly ILogger<AlarmRepository> _logger;

    public AlarmRepository(INpgsqlConnectionFactory factory, ILogger<AlarmRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task CreateAsync(AlarmRecord alarm, CancellationToken ct)
    {
        const string sql = @"
            INSERT INTO alarm (alarm_id, device_id, tag_id, ts, severity, code, message, status, created_utc, updated_utc)
            VALUES (@AlarmId, @DeviceId, @TagId, @Ts, @Severity, @Code, @Message, @Status, @CreatedUtc, @UpdatedUtc)
            ON CONFLICT (alarm_id, ts) DO NOTHING";

        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new
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
        }, cancellationToken: ct));

        _logger.LogInformation("Alarm created: {AlarmId} device={DeviceId} tag={TagId} severity={Severity} code={Code}",
            alarm.AlarmId, alarm.DeviceId, alarm.TagId, alarm.Severity, alarm.Code);
    }

    public async Task AckAsync(AlarmAckRequest request, CancellationToken ct)
    {
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

        using var conn = _factory.CreateConnection();
        using var transaction = conn.BeginTransaction();

        try
        {
            // Update alarm status
            const string updateSql = @"
                UPDATE alarm
                SET status = @NewStatus, updated_utc = @UpdatedUtc
                WHERE alarm_id = @AlarmId";

            await conn.ExecuteAsync(new CommandDefinition(updateSql, new
            {
                AlarmId = request.AlarmId,
                NewStatus = (int)AlarmStatus.Acknowledged,
                UpdatedUtc = now
            }, transaction: transaction, cancellationToken: ct));

            // Insert or update ack record
            const string ackSql = @"
                INSERT INTO alarm_ack (alarm_id, acked_by, ack_note, acked_utc)
                VALUES (@AlarmId, @AckedBy, @AckNote, @AckedUtc)
                ON CONFLICT (alarm_id) DO UPDATE SET
                    acked_by = EXCLUDED.acked_by,
                    ack_note = EXCLUDED.ack_note,
                    acked_utc = EXCLUDED.acked_utc";

            await conn.ExecuteAsync(new CommandDefinition(ackSql, new
            {
                AlarmId = request.AlarmId,
                AckedBy = request.AckedBy,
                AckNote = request.AckNote,
                AckedUtc = now
            }, transaction: transaction, cancellationToken: ct));

            transaction.Commit();
            _logger.LogInformation("Alarm acked: {AlarmId} by={AckedBy}", request.AlarmId, request.AckedBy);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
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
            SET status = @Status, updated_utc = @UpdatedUtc
            WHERE alarm_id = @AlarmId";

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            AlarmId = alarmId,
            Status = (int)AlarmStatus.Closed,
            UpdatedUtc = now
        }, cancellationToken: ct));

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
            WHERE a.alarm_id = @AlarmId
            ORDER BY a.ts DESC
            LIMIT 1";

        using var conn = _factory.CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync<AlarmRow>(
            new CommandDefinition(sql, new { AlarmId = alarmId }, cancellationToken: ct));
        return row is null ? null : MapToRecord(row);
    }

    public async Task<PagedResult<AlarmRecord>> QueryAsync(AlarmQuery query, CancellationToken ct)
    {
        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(query.DeviceId))
        {
            conditions.Add("a.device_id = @DeviceId");
            parameters.Add("DeviceId", query.DeviceId);
        }

        if (query.Status.HasValue)
        {
            conditions.Add("a.status = @Status");
            parameters.Add("Status", (int)query.Status.Value);
        }

        if (query.MinSeverity.HasValue)
        {
            conditions.Add("a.severity >= @MinSeverity");
            parameters.Add("MinSeverity", query.MinSeverity.Value);
        }

        if (query.StartTs.HasValue)
        {
            conditions.Add("a.ts >= @StartTs");
            parameters.Add("StartTs", query.StartTs.Value);
        }

        if (query.EndTs.HasValue)
        {
            conditions.Add("a.ts <= @EndTs");
            parameters.Add("EndTs", query.EndTs.Value);
        }

        if (query.After is not null)
        {
            conditions.Add("(a.ts < @AfterTs OR (a.ts = @AfterTs AND a.ctid < @AfterCtid::tid))");
            parameters.Add("AfterTs", query.After.LastTs);
            parameters.Add("AfterCtid", $"(0,{query.After.LastSeq})");
        }

        var whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : string.Empty;
        var limit = query.Limit <= 0 ? 100 : Math.Min(query.Limit, 1000);
        parameters.Add("Limit", limit);

        var sql = $@"
            SELECT a.alarm_id, a.device_id, a.tag_id, a.ts, a.severity, a.code, a.message,
                   a.status, a.created_utc, a.updated_utc, a.ctid::text as row_ctid,
                   k.acked_by, k.acked_utc, k.ack_note,
                   COUNT(*) OVER() as total_count
            FROM alarm a
            LEFT JOIN alarm_ack k ON a.alarm_id = k.alarm_id
            {whereClause}
            ORDER BY a.ts DESC, a.ctid DESC
            LIMIT @Limit";

        using var conn = _factory.CreateConnection();
        var rows = (await conn.QueryAsync<AlarmRowWithTotal>(
            new CommandDefinition(sql, parameters, cancellationToken: ct))).ToList();

        if (rows.Count == 0)
        {
            return PagedResult<AlarmRecord>.Empty() with { TotalCount = 0 };
        }

        var totalCount = rows[0].total_count;
        var hasMore = rows.Count == limit;

        // Parse the last ctid for keyset pagination
        long lastSeq = 0;
        if (hasMore && !string.IsNullOrEmpty(rows[^1].row_ctid))
        {
            // ctid format is "(page,offset)" like "(0,5)"
            var ctid = rows[^1].row_ctid;
            var parts = ctid.Trim('(', ')').Split(',');
            if (parts.Length == 2 && int.TryParse(parts[1], out var offset))
            {
                lastSeq = offset;
            }
        }

        var items = rows.Select(MapToRecordFromTotal).ToList();

        return new PagedResult<AlarmRecord>
        {
            Items = items,
            NextToken = hasMore ? new PageToken(items[^1].Ts, lastSeq) : null,
            HasMore = hasMore,
            TotalCount = (int)Math.Min(totalCount, int.MaxValue)
        };
    }

    public async Task<int> GetOpenCountAsync(string? deviceId, CancellationToken ct)
    {
        var sql = "SELECT COUNT(*) FROM alarm WHERE status = 0";
        var parameters = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            sql += " AND device_id = @DeviceId";
            parameters.Add("DeviceId", deviceId);
        }

        using var conn = _factory.CreateConnection();
        var count = await conn.ExecuteScalarAsync<long>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));
        return (int)Math.Min(count, int.MaxValue);
    }

    public async Task<int> DeleteBeforeAsync(long cutoffTs, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();

        // Delete related ack records first
        const string deleteAckSql = @"
            DELETE FROM alarm_ack
            WHERE alarm_id IN (SELECT alarm_id FROM alarm WHERE ts < @CutoffTs)";
        await conn.ExecuteAsync(new CommandDefinition(deleteAckSql, new { CutoffTs = cutoffTs }, cancellationToken: ct));

        // Delete alarms
        const string deleteAlarmSql = "DELETE FROM alarm WHERE ts < @CutoffTs";
        var affected = await conn.ExecuteAsync(new CommandDefinition(deleteAlarmSql, new { CutoffTs = cutoffTs }, cancellationToken: ct));

        _logger.LogInformation("Deleted {Count} alarms before {CutoffTs}", affected, cutoffTs);
        return affected;
    }

    private static AlarmRecord MapToRecord(AlarmRow row)
    {
        var statusInt = row.status;
        var status = Enum.IsDefined(typeof(AlarmStatus), statusInt)
            ? (AlarmStatus)statusInt
            : AlarmStatus.Open;

        return new AlarmRecord
        {
            AlarmId = row.alarm_id,
            DeviceId = row.device_id,
            TagId = row.tag_id,
            Ts = row.ts,
            Severity = row.severity,
            Code = row.code,
            Message = row.message,
            Status = status,
            CreatedUtc = row.created_utc,
            UpdatedUtc = row.updated_utc,
            AckedBy = row.acked_by,
            AckedUtc = row.acked_utc,
            AckNote = row.ack_note
        };
    }

    private static AlarmRecord MapToRecordFromTotal(AlarmRowWithTotal row)
    {
        var statusInt = row.status;
        var status = Enum.IsDefined(typeof(AlarmStatus), statusInt)
            ? (AlarmStatus)statusInt
            : AlarmStatus.Open;

        return new AlarmRecord
        {
            AlarmId = row.alarm_id,
            DeviceId = row.device_id,
            TagId = row.tag_id,
            Ts = row.ts,
            Severity = row.severity,
            Code = row.code,
            Message = row.message,
            Status = status,
            CreatedUtc = row.created_utc,
            UpdatedUtc = row.updated_utc,
            AckedBy = row.acked_by,
            AckedUtc = row.acked_utc,
            AckNote = row.ack_note
        };
    }

    // Dapper mapping classes - using class with properties for proper column-name mapping
    private sealed class AlarmRow
    {
        public string alarm_id { get; set; } = "";
        public string device_id { get; set; } = "";
        public string? tag_id { get; set; }
        public long ts { get; set; }
        public int severity { get; set; }
        public string code { get; set; } = "";
        public string message { get; set; } = "";
        public int status { get; set; }
        public long created_utc { get; set; }
        public long updated_utc { get; set; }
        public string? acked_by { get; set; }
        public long? acked_utc { get; set; }
        public string? ack_note { get; set; }
    }

    private sealed class AlarmRowWithTotal
    {
        public string alarm_id { get; set; } = "";
        public string device_id { get; set; } = "";
        public string? tag_id { get; set; }
        public long ts { get; set; }
        public int severity { get; set; }
        public string code { get; set; } = "";
        public string message { get; set; } = "";
        public int status { get; set; }
        public long created_utc { get; set; }
        public long updated_utc { get; set; }
        public string? row_ctid { get; set; }
        public string? acked_by { get; set; }
        public long? acked_utc { get; set; }
        public string? ack_note { get; set; }
        public long total_count { get; set; }
    }
}
