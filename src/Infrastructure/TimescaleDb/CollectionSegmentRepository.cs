using System.Text;
using Dapper;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.TimescaleDb;

/// <summary>
/// TimescaleDB 采集片段仓储实现
/// </summary>
public sealed class CollectionSegmentRepository : ICollectionSegmentRepository
{
    private readonly INpgsqlConnectionFactory _factory;
    private readonly ILogger<CollectionSegmentRepository> _logger;

    public CollectionSegmentRepository(INpgsqlConnectionFactory factory, ILogger<CollectionSegmentRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<long> CreateAsync(CollectionSegment segment, CancellationToken ct)
    {
        const string sql = @"
            INSERT INTO collection_segment (
                rule_id, device_id, start_time_utc, end_time_utc, status,
                data_point_count, metadata_json, created_utc
            ) VALUES (
                @RuleId, @DeviceId, @StartTimeUtc, @EndTimeUtc, @Status,
                @DataPointCount, @MetadataJson, @CreatedUtc
            )
            RETURNING id";

        using var conn = _factory.CreateConnection();
        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(sql, new
        {
            segment.RuleId,
            segment.DeviceId,
            segment.StartTimeUtc,
            segment.EndTimeUtc,
            Status = (int)segment.Status,
            segment.DataPointCount,
            segment.MetadataJson,
            segment.CreatedUtc
        }, cancellationToken: ct));

        _logger.LogDebug("Created collection segment {Id} for rule {RuleId}", id, segment.RuleId);
        return id;
    }

    public async Task<CollectionSegment?> GetAsync(long id, CancellationToken ct)
    {
        const string sql = @"
            SELECT id, rule_id, device_id, start_time_utc, end_time_utc, status,
                   data_point_count, metadata_json, created_utc
            FROM collection_segment
            WHERE id = @Id";

        using var conn = _factory.CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync<SegmentRow>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));
        return row is null ? null : MapToSegment(row);
    }

    public async Task<IReadOnlyList<CollectionSegment>> ListByRuleAsync(string ruleId, int limit, CancellationToken ct)
    {
        const string sql = @"
            SELECT id, rule_id, device_id, start_time_utc, end_time_utc, status,
                   data_point_count, metadata_json, created_utc
            FROM collection_segment
            WHERE rule_id = @RuleId
            ORDER BY start_time_utc DESC
            LIMIT @Limit";

        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<SegmentRow>(
            new CommandDefinition(sql, new { RuleId = ruleId, Limit = limit }, cancellationToken: ct));
        return rows.Select(MapToSegment).ToList();
    }

    public async Task<IReadOnlyList<CollectionSegment>> ListByDeviceAsync(
        string deviceId,
        long? startTimeUtc,
        long? endTimeUtc,
        int limit,
        CancellationToken ct)
    {
        var sb = new StringBuilder(@"
            SELECT id, rule_id, device_id, start_time_utc, end_time_utc, status,
                   data_point_count, metadata_json, created_utc
            FROM collection_segment
            WHERE device_id = @DeviceId");

        var p = new DynamicParameters();
        p.Add("DeviceId", deviceId);
        p.Add("Limit", limit);

        if (startTimeUtc.HasValue)
        {
            sb.Append(" AND start_time_utc >= @StartTimeUtc");
            p.Add("StartTimeUtc", startTimeUtc.Value);
        }

        if (endTimeUtc.HasValue)
        {
            sb.Append(" AND start_time_utc <= @EndTimeUtc");
            p.Add("EndTimeUtc", endTimeUtc.Value);
        }

        sb.Append(" ORDER BY start_time_utc DESC LIMIT @Limit");

        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<SegmentRow>(
            new CommandDefinition(sb.ToString(), p, cancellationToken: ct));
        return rows.Select(MapToSegment).ToList();
    }

    public async Task<IReadOnlyList<CollectionSegment>> QueryAsync(CollectionSegmentQuery query, CancellationToken ct)
    {
        var sb = new StringBuilder(@"
            SELECT id, rule_id, device_id, start_time_utc, end_time_utc, status,
                   data_point_count, metadata_json, created_utc
            FROM collection_segment
            WHERE 1=1");

        var p = new DynamicParameters();
        p.Add("Limit", Math.Clamp(query.Limit, 1, 1000));

        if (!string.IsNullOrEmpty(query.RuleId))
        {
            sb.Append(" AND rule_id = @RuleId");
            p.Add("RuleId", query.RuleId);
        }

        if (!string.IsNullOrEmpty(query.DeviceId))
        {
            sb.Append(" AND device_id = @DeviceId");
            p.Add("DeviceId", query.DeviceId);
        }

        if (query.Status.HasValue)
        {
            sb.Append(" AND status = @Status");
            p.Add("Status", (int)query.Status.Value);
        }

        if (query.StartTimeUtc.HasValue)
        {
            sb.Append(" AND start_time_utc >= @StartTimeUtc");
            p.Add("StartTimeUtc", query.StartTimeUtc.Value);
        }

        if (query.EndTimeUtc.HasValue)
        {
            sb.Append(" AND start_time_utc <= @EndTimeUtc");
            p.Add("EndTimeUtc", query.EndTimeUtc.Value);
        }

        sb.Append(" ORDER BY start_time_utc DESC LIMIT @Limit");

        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<SegmentRow>(
            new CommandDefinition(sb.ToString(), p, cancellationToken: ct));
        return rows.Select(MapToSegment).ToList();
    }

    public async Task UpdateStatusAsync(long id, SegmentStatus status, int dataPointCount, CancellationToken ct)
    {
        const string sql = @"
            UPDATE collection_segment
            SET status = @Status, data_point_count = @DataPointCount
            WHERE id = @Id";

        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = id,
            Status = (int)status,
            DataPointCount = dataPointCount
        }, cancellationToken: ct));
    }

    public async Task SetEndTimeAsync(long id, long endTimeUtc, CancellationToken ct)
    {
        const string sql = @"
            UPDATE collection_segment
            SET end_time_utc = @EndTimeUtc
            WHERE id = @Id";

        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = id,
            EndTimeUtc = endTimeUtc
        }, cancellationToken: ct));
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        const string sql = "DELETE FROM collection_segment WHERE id = @Id";
        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));
    }

    public async Task<CollectionSegment?> GetActiveByRuleAsync(string ruleId, CancellationToken ct)
    {
        const string sql = @"
            SELECT id, rule_id, device_id, start_time_utc, end_time_utc, status,
                   data_point_count, metadata_json, created_utc
            FROM collection_segment
            WHERE rule_id = @RuleId AND status = 0
            ORDER BY start_time_utc DESC
            LIMIT 1";

        using var conn = _factory.CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync<SegmentRow>(
            new CommandDefinition(sql, new { RuleId = ruleId }, cancellationToken: ct));
        return row is null ? null : MapToSegment(row);
    }

    public async Task<int> DeleteBeforeAsync(long cutoffUtc, CancellationToken ct)
    {
        const string sql = @"
            DELETE FROM collection_segment
            WHERE created_utc < @CutoffUtc AND status <> 0";

        using var conn = _factory.CreateConnection();
        var affected = await conn.ExecuteAsync(
            new CommandDefinition(sql, new { CutoffUtc = cutoffUtc }, cancellationToken: ct));

        _logger.LogInformation("Deleted {Count} collection segments before {CutoffUtc}", affected, cutoffUtc);
        return affected;
    }

    private static CollectionSegment MapToSegment(SegmentRow row)
    {
        return new CollectionSegment
        {
            Id = row.id,
            RuleId = row.rule_id,
            DeviceId = row.device_id,
            StartTimeUtc = row.start_time_utc,
            EndTimeUtc = row.end_time_utc,
            Status = (SegmentStatus)row.status,
            DataPointCount = row.data_point_count,
            MetadataJson = row.metadata_json,
            CreatedUtc = row.created_utc
        };
    }

    private sealed class SegmentRow
    {
        public long id { get; set; }
        public string rule_id { get; set; } = "";
        public string device_id { get; set; } = "";
        public long start_time_utc { get; set; }
        public long? end_time_utc { get; set; }
        public int status { get; set; }
        public int data_point_count { get; set; }
        public string? metadata_json { get; set; }
        public long created_utc { get; set; }
    }
}
