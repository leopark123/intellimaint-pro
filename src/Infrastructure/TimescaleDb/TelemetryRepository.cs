using Dapper;
using Microsoft.Extensions.Logging;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;

namespace IntelliMaint.Infrastructure.TimescaleDb;

/// <summary>
/// TimescaleDB 遥测数据仓储实现
/// </summary>
public sealed class TelemetryRepository : ITelemetryRepository
{
    private readonly INpgsqlConnectionFactory _factory;
    private readonly ILogger<TelemetryRepository> _logger;

    public TelemetryRepository(INpgsqlConnectionFactory factory, ILogger<TelemetryRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<int> AppendBatchAsync(IReadOnlyList<TelemetryPoint> batch, CancellationToken ct)
    {
        if (batch.Count == 0) return 0;

        // PostgreSQL: INSERT ... ON CONFLICT DO NOTHING
        const string sql = @"
            INSERT INTO telemetry (
                device_id, tag_id, ts, seq, value_type,
                bool_value, int32_value, int64_value, float32_value, float64_value,
                string_value, quality
            ) VALUES (
                @DeviceId, @TagId, @Ts, @Seq, @ValueType,
                @BoolValue, @Int32Value, @Int64Value, @Float32Value, @Float64Value,
                @StringValue, @Quality
            )
            ON CONFLICT (device_id, tag_id, ts, seq) DO NOTHING";

        var parametersList = batch.Select(p => new
        {
            p.DeviceId,
            p.TagId,
            p.Ts,
            p.Seq,
            ValueType = (int)p.ValueType,
            p.BoolValue,
            p.Int32Value,
            p.Int64Value,
            p.Float32Value,
            p.Float64Value,
            p.StringValue,
            p.Quality
        });

        try
        {
            using var conn = _factory.CreateConnection();
            var affected = await conn.ExecuteAsync(new CommandDefinition(sql, parametersList, cancellationToken: ct));
            _logger.LogDebug("Appended {Count} telemetry points", affected);
            return affected;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Append cancelled for {Count} telemetry points", batch.Count);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to append {Count} telemetry points", batch.Count);
            throw;
        }
    }

    public async Task<PagedResult<TelemetryPoint>> QueryAsync(HistoryQuery query, CancellationToken ct)
    {
        var orderDir = query.Sort == SortDirection.Asc ? "ASC" : "DESC";
        var compareOp = query.Sort == SortDirection.Asc ? ">" : "<";

        var sql = @"
            SELECT device_id, tag_id, ts, seq, value_type,
                   bool_value, int32_value, int64_value, float32_value, float64_value,
                   string_value, quality
            FROM telemetry
            WHERE device_id = @DeviceId
              AND ts BETWEEN @StartTs AND @EndTs";

        if (!string.IsNullOrEmpty(query.TagId))
            sql += " AND tag_id = @TagId";

        if (query.Filter?.QualityEquals.HasValue == true)
            sql += " AND quality = @QualityEquals";
        if (query.Filter?.QualityNotEquals.HasValue == true)
            sql += " AND quality <> @QualityNotEquals";

        if (query.After != null)
            sql += $" AND (ts {compareOp} @LastTs OR (ts = @LastTs AND seq {compareOp} @LastSeq))";

        sql += $" ORDER BY ts {orderDir}, seq {orderDir} LIMIT @LimitPlusOne";

        var parameters = new
        {
            query.DeviceId,
            query.TagId,
            query.StartTs,
            query.EndTs,
            LimitPlusOne = query.Limit + 1,
            LastTs = query.After?.LastTs,
            LastSeq = query.After?.LastSeq,
            query.Filter?.QualityEquals,
            query.Filter?.QualityNotEquals
        };

        using var conn = _factory.CreateConnection();
        var items = (await conn.QueryAsync<TelemetryRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct))).ToList();

        var hasMore = items.Count > query.Limit;
        if (hasMore) items = items.Take(query.Limit).ToList();

        PageToken? nextToken = null;
        if (hasMore && items.Count > 0)
        {
            var last = items[^1];
            nextToken = new PageToken(last.ts, (int)last.seq);
        }

        return new PagedResult<TelemetryPoint>
        {
            Items = items.Select(MapToTelemetryPoint).ToList(),
            NextToken = nextToken,
            HasMore = hasMore
        };
    }

    public async Task<int> DeleteBeforeAsync(long cutoffTs, CancellationToken ct)
    {
        const string sql = "DELETE FROM telemetry WHERE ts < @CutoffTs";
        using var conn = _factory.CreateConnection();
        var affected = await conn.ExecuteAsync(new CommandDefinition(sql, new { CutoffTs = cutoffTs }, cancellationToken: ct));
        _logger.LogInformation("Deleted {Count} telemetry points before {CutoffTs}", affected, cutoffTs);
        return affected;
    }

    public async Task<TelemetryStats> GetStatsAsync(string? deviceId, CancellationToken ct)
    {
        string sql;
        object? parameters = null;

        if (string.IsNullOrEmpty(deviceId))
        {
            sql = @"
                SELECT
                    COUNT(*) as TotalCount,
                    MIN(ts) as OldestTs,
                    MAX(ts) as NewestTs,
                    COUNT(DISTINCT device_id) as DeviceCount,
                    COUNT(DISTINCT tag_id) as TagCount
                FROM telemetry";
        }
        else
        {
            sql = @"
                SELECT
                    COUNT(*) as TotalCount,
                    MIN(ts) as OldestTs,
                    MAX(ts) as NewestTs,
                    1 as DeviceCount,
                    COUNT(DISTINCT tag_id) as TagCount
                FROM telemetry
                WHERE device_id = @DeviceId";
            parameters = new { DeviceId = deviceId };
        }

        using var conn = _factory.CreateConnection();
        var result = await conn.QueryFirstOrDefaultAsync<TelemetryStats>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));
        return result ?? new TelemetryStats();
    }

    public async Task<IReadOnlyList<TelemetryPoint>> QuerySimpleAsync(
        string? deviceId, string? tagId, long? startTs, long? endTs, int limit, CancellationToken ct)
    {
        var (data, _) = await QueryWithCursorAsync(deviceId, tagId, startTs, endTs, limit, null, null, ct);
        return data;
    }

    public async Task<(IReadOnlyList<TelemetryPoint> Data, bool HasMore)> QueryWithCursorAsync(
        string? deviceId, string? tagId, long? startTs, long? endTs, int limit,
        long? cursorTs, int? cursorSeq, CancellationToken ct)
    {
        var sql = new System.Text.StringBuilder(@"
            SELECT device_id, tag_id, ts, seq, value_type,
                   bool_value, int32_value, int64_value, float32_value, float64_value,
                   string_value, quality
            FROM telemetry WHERE 1=1");

        var p = new DynamicParameters();

        var hasFilter = !string.IsNullOrWhiteSpace(deviceId) || !string.IsNullOrWhiteSpace(tagId);
        var effectiveStartTs = startTs;
        if (!hasFilter && !startTs.HasValue)
        {
            effectiveStartTs = DateTimeOffset.UtcNow.AddHours(-24).ToUnixTimeMilliseconds();
        }

        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            sql.Append(" AND device_id = @DeviceId");
            p.Add("DeviceId", deviceId);
        }

        if (!string.IsNullOrWhiteSpace(tagId))
        {
            sql.Append(" AND tag_id = @TagId");
            p.Add("TagId", tagId);
        }

        if (effectiveStartTs.HasValue)
        {
            sql.Append(" AND ts >= @StartTs");
            p.Add("StartTs", effectiveStartTs.Value);
        }

        if (endTs.HasValue)
        {
            sql.Append(" AND ts <= @EndTs");
            p.Add("EndTs", endTs.Value);
        }

        if (cursorTs.HasValue)
        {
            sql.Append(" AND (ts < @CursorTs OR (ts = @CursorTs AND seq < @CursorSeq))");
            p.Add("CursorTs", cursorTs.Value);
            p.Add("CursorSeq", cursorSeq ?? 0);
        }

        var safeLimit = Math.Clamp(limit, 1, 10_000);
        sql.Append(" ORDER BY ts DESC, seq DESC LIMIT @Limit");
        p.Add("Limit", safeLimit + 1);

        using var conn = _factory.CreateConnection();
        var rows = (await conn.QueryAsync<TelemetryRow>(
            new CommandDefinition(sql.ToString(), p, cancellationToken: ct))).ToList();

        var hasMore = rows.Count > safeLimit;
        var data = hasMore ? rows.Take(safeLimit).Select(MapToTelemetryPoint).ToList()
                          : rows.Select(MapToTelemetryPoint).ToList();

        return (data, hasMore);
    }

    public async Task<IReadOnlyList<TelemetryPoint>> GetLatestAsync(string? deviceId, string? tagId, CancellationToken ct)
    {
        var cutoffTs = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeMilliseconds();

        var sql = new System.Text.StringBuilder(@"
            SELECT DISTINCT ON (device_id, tag_id)
                device_id, tag_id, ts, seq, value_type,
                bool_value, int32_value, int64_value, float32_value, float64_value,
                string_value, quality
            FROM telemetry
            WHERE ts >= @CutoffTs");

        var p = new DynamicParameters();
        p.Add("CutoffTs", cutoffTs);

        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            sql.Append(" AND device_id = @DeviceId");
            p.Add("DeviceId", deviceId);
        }

        if (!string.IsNullOrWhiteSpace(tagId))
        {
            sql.Append(" AND tag_id = @TagId");
            p.Add("TagId", tagId);
        }

        sql.Append(" ORDER BY device_id, tag_id, ts DESC, seq DESC");

        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<TelemetryRow>(
            new CommandDefinition(sql.ToString(), p, cancellationToken: ct));

        return rows.Select(MapToTelemetryPoint).ToList();
    }

    public async Task<IReadOnlyList<TagInfo>> GetTagsAsync(CancellationToken ct)
    {
        var cutoffTs = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds();

        const string sql = @"
            SELECT
                t.device_id,
                t.tag_id,
                CASE t.data_type
                    WHEN 'Float32' THEN 10
                    WHEN 'Float64' THEN 11
                    WHEN 'Int32' THEN 6
                    WHEN 'Int16' THEN 4
                    WHEN 'Bool' THEN 1
                    WHEN 'String' THEN 12
                    ELSE 10
                END as value_type,
                t.unit,
                (SELECT MAX(ts) FROM telemetry tel
                 WHERE tel.device_id = t.device_id AND tel.tag_id = t.tag_id
                 AND tel.ts >= @CutoffTs) as last_ts
            FROM tag t
            WHERE t.enabled = true
            ORDER BY t.device_id, t.tag_id";

        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<TagInfoRow>(
            new CommandDefinition(sql, new { CutoffTs = cutoffTs }, cancellationToken: ct));

        return rows.Select(r => new TagInfo
        {
            DeviceId = r.device_id,
            TagId = r.tag_id,
            ValueType = (TagValueType)r.value_type,
            Unit = r.unit,
            LastTs = r.last_ts,
            PointCount = 0
        }).ToList();
    }

    public async Task<IReadOnlyList<AggregateResult>> AggregateAsync(
        string deviceId, string tagId, long startTs, long endTs, int intervalMs,
        AggregateFunction func, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) throw new ArgumentException("deviceId is required");
        if (string.IsNullOrWhiteSpace(tagId)) throw new ArgumentException("tagId is required");
        if (endTs <= startTs) throw new ArgumentException("endTs must be greater than startTs");
        if (intervalMs <= 0) throw new ArgumentException("intervalMs must be > 0");

        const string numericExpr = "COALESCE(float64_value, float32_value, int64_value::double precision, int32_value::double precision, bool_value::int::double precision)";

        string aggExpr = func switch
        {
            AggregateFunction.Avg => $"AVG({numericExpr})",
            AggregateFunction.Min => $"MIN({numericExpr})",
            AggregateFunction.Max => $"MAX({numericExpr})",
            AggregateFunction.Sum => $"SUM({numericExpr})",
            AggregateFunction.Count => "COUNT(*)",
            AggregateFunction.First => $"(array_agg({numericExpr} ORDER BY ts ASC))[1]",
            AggregateFunction.Last => $"(array_agg({numericExpr} ORDER BY ts DESC))[1]",
            _ => $"AVG({numericExpr})"
        };

        var sql = $@"
            SELECT
                (ts / @IntervalMs) * @IntervalMs AS ts,
                {aggExpr} AS value,
                COUNT(*) AS count
            FROM telemetry
            WHERE device_id = @DeviceId
              AND tag_id = @TagId
              AND ts >= @StartTs
              AND ts < @EndTs
            GROUP BY (ts / @IntervalMs) * @IntervalMs
            ORDER BY ts";

        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<AggregateResult>(
            new CommandDefinition(sql, new { DeviceId = deviceId, TagId = tagId, StartTs = startTs, EndTs = endTs, IntervalMs = intervalMs }, cancellationToken: ct));

        return rows.ToList();
    }

    private static TelemetryPoint MapToTelemetryPoint(TelemetryRow row) => new()
    {
        DeviceId = row.device_id,
        TagId = row.tag_id,
        Ts = row.ts,
        Seq = row.seq,
        ValueType = (TagValueType)row.value_type,
        BoolValue = row.bool_value,
        Int32Value = row.int32_value,
        Int64Value = row.int64_value,
        Float32Value = row.float32_value,
        Float64Value = row.float64_value,
        StringValue = row.string_value,
        Quality = row.quality
    };

    // Dapper mapping classes - using class with properties for proper column-name mapping
    private sealed class TelemetryRow
    {
        public string device_id { get; set; } = "";
        public string tag_id { get; set; } = "";
        public long ts { get; set; }
        public long seq { get; set; }
        public int value_type { get; set; }
        public bool? bool_value { get; set; }
        public int? int32_value { get; set; }
        public long? int64_value { get; set; }
        public float? float32_value { get; set; }
        public double? float64_value { get; set; }
        public string? string_value { get; set; }
        public int quality { get; set; }
    }

    private sealed class TagInfoRow
    {
        public string device_id { get; set; } = "";
        public string tag_id { get; set; } = "";
        public int value_type { get; set; }
        public string? unit { get; set; }
        public long? last_ts { get; set; }
    }
}
