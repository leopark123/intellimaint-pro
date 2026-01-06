using Dapper;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.TimescaleDb;

/// <summary>
/// TimescaleDB AlarmGroup repository implementation
/// </summary>
public sealed class AlarmGroupRepository : IAlarmGroupRepository
{
    private readonly INpgsqlConnectionFactory _factory;
    private readonly ILogger<AlarmGroupRepository> _logger;

    public AlarmGroupRepository(INpgsqlConnectionFactory factory, ILogger<AlarmGroupRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<AlarmGroup?> GetAsync(string groupId, CancellationToken ct)
    {
        const string sql = @"
            SELECT group_id, device_id, tag_id, rule_id, severity, code, message,
                   alarm_count, first_occurred_utc, last_occurred_utc,
                   aggregate_status, created_utc, updated_utc
            FROM alarm_group
            WHERE group_id = @GroupId";

        using var conn = _factory.CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync<AlarmGroupRow>(
            new CommandDefinition(sql, new { GroupId = groupId }, cancellationToken: ct));
        return row is null ? null : MapToAlarmGroup(row);
    }

    public async Task<AlarmGroup?> FindActiveGroupAsync(string deviceId, string ruleId, CancellationToken ct)
    {
        const string sql = @"
            SELECT group_id, device_id, tag_id, rule_id, severity, code, message,
                   alarm_count, first_occurred_utc, last_occurred_utc,
                   aggregate_status, created_utc, updated_utc
            FROM alarm_group
            WHERE device_id = @DeviceId
              AND rule_id = @RuleId
              AND aggregate_status <> 2
            ORDER BY last_occurred_utc DESC
            LIMIT 1";

        using var conn = _factory.CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync<AlarmGroupRow>(
            new CommandDefinition(sql, new { DeviceId = deviceId, RuleId = ruleId }, cancellationToken: ct));
        return row is null ? null : MapToAlarmGroup(row);
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
            )";

        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new
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
        }, cancellationToken: ct));

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
            WHERE group_id = @GroupId";

        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            group.GroupId,
            group.Severity,
            group.Message,
            group.AlarmCount,
            group.LastOccurredUtc,
            AggregateStatus = (int)group.AggregateStatus,
            group.UpdatedUtc
        }, cancellationToken: ct));
    }

    public async Task AddAlarmToGroupAsync(string alarmId, string groupId, CancellationToken ct)
    {
        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        const string sql = @"
            INSERT INTO alarm_to_group (alarm_id, group_id, added_utc)
            VALUES (@AlarmId, @GroupId, @AddedUtc)
            ON CONFLICT (alarm_id) DO NOTHING";

        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            AlarmId = alarmId,
            GroupId = groupId,
            AddedUtc = nowUtc
        }, cancellationToken: ct));
    }

    public async Task<PagedResult<AlarmGroup>> QueryAsync(AlarmGroupQuery query, CancellationToken ct)
    {
        var conditions = new List<string>();
        var p = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(query.DeviceId))
        {
            conditions.Add("device_id = @DeviceId");
            p.Add("DeviceId", query.DeviceId);
        }

        if (query.Status.HasValue)
        {
            conditions.Add("aggregate_status = @Status");
            p.Add("Status", (int)query.Status.Value);
        }

        if (query.MinSeverity.HasValue)
        {
            conditions.Add("severity >= @MinSeverity");
            p.Add("MinSeverity", query.MinSeverity.Value);
        }

        if (query.StartTs.HasValue)
        {
            conditions.Add("first_occurred_utc >= @StartTs");
            p.Add("StartTs", query.StartTs.Value);
        }

        if (query.EndTs.HasValue)
        {
            conditions.Add("last_occurred_utc <= @EndTs");
            p.Add("EndTs", query.EndTs.Value);
        }

        if (query.After is not null)
        {
            conditions.Add("last_occurred_utc < @AfterTs");
            p.Add("AfterTs", query.After.LastTs);
        }

        var whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : string.Empty;
        var limit = query.Limit <= 0 ? 50 : Math.Min(query.Limit, 500);
        p.Add("Limit", limit);

        var sql = $@"
            SELECT group_id, device_id, tag_id, rule_id, severity, code, message,
                   alarm_count, first_occurred_utc, last_occurred_utc,
                   aggregate_status, created_utc, updated_utc,
                   COUNT(*) OVER() as total_count
            FROM alarm_group
            {whereClause}
            ORDER BY last_occurred_utc DESC
            LIMIT @Limit";

        using var conn = _factory.CreateConnection();
        var rows = (await conn.QueryAsync<AlarmGroupRowWithTotal>(
            new CommandDefinition(sql, p, cancellationToken: ct))).ToList();

        if (rows.Count == 0)
        {
            return PagedResult<AlarmGroup>.Empty() with { TotalCount = 0 };
        }

        var totalCount = (int)Math.Min(rows[0].total_count, int.MaxValue);
        var hasMore = rows.Count == limit;
        var items = rows.Select(MapToAlarmGroupFromTotal).ToList();

        return new PagedResult<AlarmGroup>
        {
            Items = items,
            NextToken = hasMore ? new PageToken(items[^1].LastOccurredUtc, 0) : null,
            HasMore = hasMore,
            TotalCount = totalCount
        };
    }

    public async Task<List<string>> GetChildAlarmIdsAsync(string groupId, CancellationToken ct)
    {
        const string sql = @"
            SELECT alarm_id
            FROM alarm_to_group
            WHERE group_id = @GroupId
            ORDER BY added_utc DESC";

        using var conn = _factory.CreateConnection();
        var ids = await conn.QueryAsync<string>(
            new CommandDefinition(sql, new { GroupId = groupId }, cancellationToken: ct));
        return ids.ToList();
    }

    public async Task<int> GetOpenGroupCountAsync(string? deviceId, CancellationToken ct)
    {
        var sql = "SELECT COUNT(1) FROM alarm_group WHERE aggregate_status = 0";
        var p = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            sql += " AND device_id = @DeviceId";
            p.Add("DeviceId", deviceId);
        }

        using var conn = _factory.CreateConnection();
        var count = await conn.ExecuteScalarAsync<long>(
            new CommandDefinition(sql, p, cancellationToken: ct));
        return (int)Math.Min(count, int.MaxValue);
    }

    public async Task SetStatusAsync(string groupId, AlarmStatus status, CancellationToken ct)
    {
        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        const string sql = @"
            UPDATE alarm_group
            SET aggregate_status = @Status, updated_utc = @UpdatedUtc
            WHERE group_id = @GroupId";

        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            GroupId = groupId,
            Status = (int)status,
            UpdatedUtc = nowUtc
        }, cancellationToken: ct));
    }

    public async Task AckGroupAsync(string groupId, string ackedBy, string? ackNote, CancellationToken ct)
    {
        // Update group status to Acknowledged
        await SetStatusAsync(groupId, AlarmStatus.Acknowledged, ct);

        // Get child alarm IDs
        var childAlarmIds = await GetChildAlarmIdsAsync(groupId, ct);
        if (childAlarmIds.Count == 0)
        {
            _logger.LogDebug("No child alarms to ack for group {GroupId}", groupId);
            return;
        }

        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        using var conn = _factory.CreateConnection();

        // Batch update alarm status
        const string updateAlarmSql = @"
            UPDATE alarm
            SET status = @Status, updated_utc = @UpdatedUtc
            WHERE alarm_id IN (SELECT alarm_id FROM alarm_to_group WHERE group_id = @GroupId)
              AND status <> 2";

        await conn.ExecuteAsync(new CommandDefinition(updateAlarmSql, new
        {
            Status = (int)AlarmStatus.Acknowledged,
            UpdatedUtc = nowUtc,
            GroupId = groupId
        }, cancellationToken: ct));

        // Insert/update ack records
        foreach (var alarmId in childAlarmIds)
        {
            const string ackSql = @"
                INSERT INTO alarm_ack (alarm_id, acked_by, ack_note, acked_utc)
                VALUES (@AlarmId, @AckedBy, @AckNote, @AckedUtc)
                ON CONFLICT (alarm_id) DO UPDATE SET
                    acked_by = EXCLUDED.acked_by,
                    ack_note = EXCLUDED.ack_note,
                    acked_utc = EXCLUDED.acked_utc";

            await conn.ExecuteAsync(new CommandDefinition(ackSql, new
            {
                AlarmId = alarmId,
                AckedBy = ackedBy,
                AckNote = ackNote,
                AckedUtc = nowUtc
            }, cancellationToken: ct));
        }

        _logger.LogInformation("AlarmGroup acked: {GroupId} by={AckedBy}, {Count} child alarms",
            groupId, ackedBy, childAlarmIds.Count);
    }

    public async Task CloseGroupAsync(string groupId, CancellationToken ct)
    {
        // Update group status to Closed
        await SetStatusAsync(groupId, AlarmStatus.Closed, ct);

        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        const string sql = @"
            UPDATE alarm
            SET status = @Status, updated_utc = @UpdatedUtc
            WHERE alarm_id IN (SELECT alarm_id FROM alarm_to_group WHERE group_id = @GroupId)
              AND status <> 2";

        using var conn = _factory.CreateConnection();
        var affected = await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            Status = (int)AlarmStatus.Closed,
            UpdatedUtc = nowUtc,
            GroupId = groupId
        }, cancellationToken: ct));

        _logger.LogInformation("AlarmGroup closed: {GroupId}, {Count} child alarms closed", groupId, affected);
    }

    private static AlarmGroup MapToAlarmGroup(AlarmGroupRow row)
    {
        var status = Enum.IsDefined(typeof(AlarmStatus), row.aggregate_status)
            ? (AlarmStatus)row.aggregate_status
            : AlarmStatus.Open;

        return new AlarmGroup
        {
            GroupId = row.group_id,
            DeviceId = row.device_id,
            TagId = row.tag_id,
            RuleId = row.rule_id,
            Severity = row.severity,
            Code = row.code,
            Message = row.message,
            AlarmCount = row.alarm_count,
            FirstOccurredUtc = row.first_occurred_utc,
            LastOccurredUtc = row.last_occurred_utc,
            AggregateStatus = status,
            CreatedUtc = row.created_utc,
            UpdatedUtc = row.updated_utc
        };
    }

    private static AlarmGroup MapToAlarmGroupFromTotal(AlarmGroupRowWithTotal row)
    {
        var status = Enum.IsDefined(typeof(AlarmStatus), row.aggregate_status)
            ? (AlarmStatus)row.aggregate_status
            : AlarmStatus.Open;

        return new AlarmGroup
        {
            GroupId = row.group_id,
            DeviceId = row.device_id,
            TagId = row.tag_id,
            RuleId = row.rule_id,
            Severity = row.severity,
            Code = row.code,
            Message = row.message,
            AlarmCount = row.alarm_count,
            FirstOccurredUtc = row.first_occurred_utc,
            LastOccurredUtc = row.last_occurred_utc,
            AggregateStatus = status,
            CreatedUtc = row.created_utc,
            UpdatedUtc = row.updated_utc
        };
    }

    private sealed class AlarmGroupRow
    {
        public string group_id { get; set; } = "";
        public string device_id { get; set; } = "";
        public string? tag_id { get; set; }
        public string rule_id { get; set; } = "";
        public int severity { get; set; }
        public string? code { get; set; }
        public string? message { get; set; }
        public int alarm_count { get; set; }
        public long first_occurred_utc { get; set; }
        public long last_occurred_utc { get; set; }
        public int aggregate_status { get; set; }
        public long created_utc { get; set; }
        public long updated_utc { get; set; }
    }

    private sealed class AlarmGroupRowWithTotal
    {
        public string group_id { get; set; } = "";
        public string device_id { get; set; } = "";
        public string? tag_id { get; set; }
        public string rule_id { get; set; } = "";
        public int severity { get; set; }
        public string? code { get; set; }
        public string? message { get; set; }
        public int alarm_count { get; set; }
        public long first_occurred_utc { get; set; }
        public long last_occurred_utc { get; set; }
        public int aggregate_status { get; set; }
        public long created_utc { get; set; }
        public long updated_utc { get; set; }
        public long total_count { get; set; }
    }
}
