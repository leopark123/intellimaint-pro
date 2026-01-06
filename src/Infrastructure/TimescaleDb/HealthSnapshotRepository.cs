using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.TimescaleDb;

/// <summary>
/// TimescaleDB HealthSnapshot repository implementation
/// </summary>
public sealed class HealthSnapshotRepository : IHealthSnapshotRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly INpgsqlConnectionFactory _factory;
    private readonly ILogger<HealthSnapshotRepository> _logger;

    public HealthSnapshotRepository(INpgsqlConnectionFactory factory, ILogger<HealthSnapshotRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task SaveAsync(HealthSnapshot snapshot, CancellationToken ct)
    {
        var tsUtc = snapshot.UtcTime.ToUnixTimeMilliseconds();

        string collectorsJson;
        try
        {
            collectorsJson = JsonSerializer.Serialize(snapshot.Collectors ?? new Dictionary<string, CollectorHealth>(), JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to serialize collectors_json, fallback to empty dictionary. ts={TsUtc}", tsUtc);
            collectorsJson = JsonSerializer.Serialize(new Dictionary<string, CollectorHealth>(), JsonOptions);
        }

        const string sql = @"
            INSERT INTO health_snapshot (
                ts_utc, overall_state, database_state, queue_state, queue_depth,
                dropped_points, write_p95_ms, mqtt_connected, outbox_depth,
                memory_used_mb, collectors_json
            ) VALUES (
                @TsUtc, @OverallState, @DatabaseState, @QueueState, @QueueDepth,
                @DroppedPoints, @WriteP95Ms, @MqttConnected, @OutboxDepth,
                @MemoryUsedMb, @CollectorsJson
            )
            ON CONFLICT (ts_utc) DO UPDATE SET
                overall_state = EXCLUDED.overall_state,
                database_state = EXCLUDED.database_state,
                queue_state = EXCLUDED.queue_state,
                queue_depth = EXCLUDED.queue_depth,
                dropped_points = EXCLUDED.dropped_points,
                write_p95_ms = EXCLUDED.write_p95_ms,
                mqtt_connected = EXCLUDED.mqtt_connected,
                outbox_depth = EXCLUDED.outbox_depth,
                memory_used_mb = EXCLUDED.memory_used_mb,
                collectors_json = EXCLUDED.collectors_json";

        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            TsUtc = tsUtc,
            OverallState = (int)snapshot.OverallState,
            DatabaseState = (int)snapshot.DatabaseState,
            QueueState = (int)snapshot.QueueState,
            QueueDepth = snapshot.QueueDepth,
            DroppedPoints = snapshot.DroppedPoints,
            WriteP95Ms = snapshot.WriteLatencyMsP95,
            MqttConnected = snapshot.MqttConnected,
            OutboxDepth = snapshot.OutboxDepth,
            MemoryUsedMb = snapshot.MemoryUsedMb,
            CollectorsJson = collectorsJson
        }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<HealthSnapshot>> GetRecentAsync(int count, CancellationToken ct)
    {
        const string sql = @"
            SELECT ts_utc, overall_state, database_state, queue_state, queue_depth,
                   dropped_points, write_p95_ms, mqtt_connected, outbox_depth,
                   memory_used_mb, collectors_json
            FROM health_snapshot
            ORDER BY ts_utc DESC
            LIMIT @Count";

        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<SnapshotRow>(new CommandDefinition(sql, new { Count = count }, cancellationToken: ct));

        return rows.Select(MapToSnapshot).ToList();
    }

    public async Task<int> DeleteBeforeAsync(long cutoffTs, CancellationToken ct)
    {
        const string sql = "DELETE FROM health_snapshot WHERE ts_utc < @CutoffTs";

        using var conn = _factory.CreateConnection();
        var affected = await conn.ExecuteAsync(new CommandDefinition(sql, new { CutoffTs = cutoffTs }, cancellationToken: ct));

        _logger.LogInformation("Deleted {Count} health snapshots before {CutoffTs}", affected, cutoffTs);
        return affected;
    }

    private HealthSnapshot MapToSnapshot(SnapshotRow row)
    {
        Dictionary<string, CollectorHealth> collectors;
        if (string.IsNullOrWhiteSpace(row.collectors_json))
        {
            collectors = new Dictionary<string, CollectorHealth>();
        }
        else
        {
            try
            {
                collectors = JsonSerializer.Deserialize<Dictionary<string, CollectorHealth>>(row.collectors_json, JsonOptions)
                    ?? new Dictionary<string, CollectorHealth>();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize collectors_json, fallback to empty dictionary. ts={TsUtc}", row.ts_utc);
                collectors = new Dictionary<string, CollectorHealth>();
            }
        }

        return new HealthSnapshot
        {
            UtcTime = DateTimeOffset.FromUnixTimeMilliseconds(row.ts_utc),
            OverallState = (HealthState)row.overall_state,
            DatabaseState = (DatabaseState)row.database_state,
            QueueState = (QueueState)row.queue_state,
            QueueDepth = row.queue_depth,
            DroppedPoints = row.dropped_points,
            WriteLatencyMsP95 = row.write_p95_ms,
            Collectors = collectors,
            MqttConnected = row.mqtt_connected,
            OutboxDepth = row.outbox_depth,
            MemoryUsedMb = row.memory_used_mb
        };
    }

    private sealed class SnapshotRow
    {
        public long ts_utc { get; set; }
        public int overall_state { get; set; }
        public int database_state { get; set; }
        public int queue_state { get; set; }
        public long queue_depth { get; set; }
        public long dropped_points { get; set; }
        public double write_p95_ms { get; set; }
        public bool mqtt_connected { get; set; }
        public long outbox_depth { get; set; }
        public long memory_used_mb { get; set; }
        public string? collectors_json { get; set; }
    }
}
