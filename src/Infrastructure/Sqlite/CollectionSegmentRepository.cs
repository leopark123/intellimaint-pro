using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Data.Sqlite;

namespace IntelliMaint.Infrastructure.Sqlite;

/// <summary>
/// 采集片段仓储实现
/// </summary>
public sealed class CollectionSegmentRepository : ICollectionSegmentRepository
{
    private readonly IDbExecutor _db;

    public CollectionSegmentRepository(IDbExecutor db)
    {
        _db = db;
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
);
SELECT last_insert_rowid();";

        var result = await _db.ExecuteScalarAsync<long>(sql, new
        {
            segment.RuleId,
            segment.DeviceId,
            segment.StartTimeUtc,
            segment.EndTimeUtc,
            Status = (int)segment.Status,
            segment.DataPointCount,
            segment.MetadataJson,
            segment.CreatedUtc
        }, ct);

        return result;
    }

    public async Task<CollectionSegment?> GetAsync(long id, CancellationToken ct)
    {
        const string sql = @"
SELECT id, rule_id, device_id, start_time_utc, end_time_utc, status,
       data_point_count, metadata_json, created_utc
FROM collection_segment
WHERE id = @Id;";

        var list = await _db.QueryAsync(sql, MapSegment, new { Id = id }, ct);
        return list.Count > 0 ? list[0] : null;
    }

    public async Task<IReadOnlyList<CollectionSegment>> ListByRuleAsync(string ruleId, int limit, CancellationToken ct)
    {
        const string sql = @"
SELECT id, rule_id, device_id, start_time_utc, end_time_utc, status,
       data_point_count, metadata_json, created_utc
FROM collection_segment
WHERE rule_id = @RuleId
ORDER BY start_time_utc DESC
LIMIT @Limit;";

        return await _db.QueryAsync(sql, MapSegment, new { RuleId = ruleId, Limit = limit }, ct);
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

        if (startTimeUtc.HasValue)
            sb.Append(" AND start_time_utc >= @StartTimeUtc");
        if (endTimeUtc.HasValue)
            sb.Append(" AND start_time_utc <= @EndTimeUtc");

        sb.Append(" ORDER BY start_time_utc DESC LIMIT @Limit;");

        return await _db.QueryAsync(sb.ToString(), MapSegment, new
        {
            DeviceId = deviceId,
            StartTimeUtc = startTimeUtc,
            EndTimeUtc = endTimeUtc,
            Limit = limit
        }, ct);
    }

    public async Task<IReadOnlyList<CollectionSegment>> QueryAsync(CollectionSegmentQuery query, CancellationToken ct)
    {
        var sb = new StringBuilder(@"
SELECT id, rule_id, device_id, start_time_utc, end_time_utc, status,
       data_point_count, metadata_json, created_utc
FROM collection_segment
WHERE 1=1");

        if (!string.IsNullOrEmpty(query.RuleId))
            sb.Append(" AND rule_id = @RuleId");
        if (!string.IsNullOrEmpty(query.DeviceId))
            sb.Append(" AND device_id = @DeviceId");
        if (query.Status.HasValue)
            sb.Append(" AND status = @Status");
        if (query.StartTimeUtc.HasValue)
            sb.Append(" AND start_time_utc >= @StartTimeUtc");
        if (query.EndTimeUtc.HasValue)
            sb.Append(" AND start_time_utc <= @EndTimeUtc");

        sb.Append(" ORDER BY start_time_utc DESC LIMIT @Limit;");

        return await _db.QueryAsync(sb.ToString(), MapSegment, new
        {
            query.RuleId,
            query.DeviceId,
            Status = query.Status.HasValue ? (int)query.Status.Value : (int?)null,
            query.StartTimeUtc,
            query.EndTimeUtc,
            query.Limit
        }, ct);
    }

    public async Task UpdateStatusAsync(long id, SegmentStatus status, int dataPointCount, CancellationToken ct)
    {
        const string sql = @"
UPDATE collection_segment
SET status = @Status,
    data_point_count = @DataPointCount
WHERE id = @Id;";

        await _db.ExecuteNonQueryAsync(sql, new
        {
            Id = id,
            Status = (int)status,
            DataPointCount = dataPointCount
        }, ct);
    }

    public async Task SetEndTimeAsync(long id, long endTimeUtc, CancellationToken ct)
    {
        const string sql = @"
UPDATE collection_segment
SET end_time_utc = @EndTimeUtc
WHERE id = @Id;";

        await _db.ExecuteNonQueryAsync(sql, new
        {
            Id = id,
            EndTimeUtc = endTimeUtc
        }, ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        const string sql = "DELETE FROM collection_segment WHERE id = @Id;";
        await _db.ExecuteNonQueryAsync(sql, new { Id = id }, ct);
    }

    public async Task<CollectionSegment?> GetActiveByRuleAsync(string ruleId, CancellationToken ct)
    {
        const string sql = @"
SELECT id, rule_id, device_id, start_time_utc, end_time_utc, status,
       data_point_count, metadata_json, created_utc
FROM collection_segment
WHERE rule_id = @RuleId AND status = 0
ORDER BY start_time_utc DESC
LIMIT 1;";

        var list = await _db.QueryAsync(sql, MapSegment, new { RuleId = ruleId }, ct);
        return list.Count > 0 ? list[0] : null;
    }

    public async Task<int> DeleteBeforeAsync(long cutoffUtc, CancellationToken ct)
    {
        const string sql = @"
DELETE FROM collection_segment
WHERE created_utc < @CutoffUtc AND status <> 0;";

        return await _db.ExecuteNonQueryAsync(sql, new { CutoffUtc = cutoffUtc }, ct);
    }

    private static CollectionSegment MapSegment(SqliteDataReader reader)
    {
        return new CollectionSegment
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            RuleId = reader.GetString(reader.GetOrdinal("rule_id")),
            DeviceId = reader.GetString(reader.GetOrdinal("device_id")),
            StartTimeUtc = reader.GetInt64(reader.GetOrdinal("start_time_utc")),
            EndTimeUtc = reader.IsDBNull(reader.GetOrdinal("end_time_utc"))
                ? null : reader.GetInt64(reader.GetOrdinal("end_time_utc")),
            Status = (SegmentStatus)reader.GetInt32(reader.GetOrdinal("status")),
            DataPointCount = reader.GetInt32(reader.GetOrdinal("data_point_count")),
            MetadataJson = reader.IsDBNull(reader.GetOrdinal("metadata_json"))
                ? null : reader.GetString(reader.GetOrdinal("metadata_json")),
            CreatedUtc = reader.GetInt64(reader.GetOrdinal("created_utc"))
        };
    }
}
