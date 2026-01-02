using System.Text.Json;
using System.Text.Json.Serialization;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.Sqlite;

public sealed class HealthSnapshotRepository : IHealthSnapshotRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IDbExecutor _db;
    private readonly ILogger<HealthSnapshotRepository> _logger;

    public HealthSnapshotRepository(IDbExecutor db, ILogger<HealthSnapshotRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task SaveAsync(HealthSnapshot snapshot, CancellationToken ct)
    {
        const string sql = @"
INSERT OR REPLACE INTO health_snapshot (
    ts_utc, overall_state, database_state, queue_state, queue_depth,
    dropped_points, write_p95_ms, mqtt_connected, outbox_depth,
    memory_used_mb, collectors_json
) VALUES (
    @TsUtc, @OverallState, @DatabaseState, @QueueState, @QueueDepth,
    @DroppedPoints, @WriteP95Ms, @MqttConnected, @OutboxDepth,
    @MemoryUsedMb, @CollectorsJson
);";

        var tsUtc = snapshot.UtcTime.ToUnixTimeMilliseconds();

        string? collectorsJson = null;
        try
        {
            collectorsJson = JsonSerializer.Serialize(snapshot.Collectors ?? new Dictionary<string, CollectorHealth>(), JsonOptions);
        }
        catch (Exception ex)
        {
            // 序列化失败：不抛异常，降级保存空字典
            _logger.LogWarning(ex, "Failed to serialize collectors_json, fallback to empty dictionary. ts={TsUtc}", tsUtc);
            collectorsJson = JsonSerializer.Serialize(new Dictionary<string, CollectorHealth>(), JsonOptions);
        }

        var parameters = new
        {
            TsUtc = tsUtc,
            OverallState = (int)snapshot.OverallState,
            DatabaseState = (int)snapshot.DatabaseState,
            QueueState = (int)snapshot.QueueState,
            QueueDepth = snapshot.QueueDepth,
            DroppedPoints = snapshot.DroppedPoints,
            WriteP95Ms = snapshot.WriteLatencyMsP95,
            MqttConnected = snapshot.MqttConnected ? 1 : 0,
            OutboxDepth = snapshot.OutboxDepth,
            MemoryUsedMb = snapshot.MemoryUsedMb,
            CollectorsJson = collectorsJson
        };

        await _db.ExecuteNonQueryAsync(sql, parameters, ct);
    }

    public async Task<IReadOnlyList<HealthSnapshot>> GetRecentAsync(int count, CancellationToken ct)
    {
        const string sql = @"
SELECT ts_utc, overall_state, database_state, queue_state, queue_depth,
       dropped_points, write_p95_ms, mqtt_connected, outbox_depth,
       memory_used_mb, collectors_json
FROM health_snapshot
ORDER BY ts_utc DESC
LIMIT @Count;";

        var items = await _db.QueryAsync(sql, MapSnapshot, new { Count = count }, ct);
        return items;
    }

    public async Task<int> DeleteBeforeAsync(long cutoffTs, CancellationToken ct)
    {
        const string sql = "DELETE FROM health_snapshot WHERE ts_utc < @CutoffTs;";
        return await _db.ExecuteNonQueryAsync(sql, new { CutoffTs = cutoffTs }, ct);
    }

    private HealthSnapshot MapSnapshot(SqliteDataReader reader)
    {
        var tsUtc = reader.GetInt64(reader.GetOrdinal("ts_utc"));
        var overallState = reader.GetInt64(reader.GetOrdinal("overall_state"));
        var databaseState = reader.GetInt64(reader.GetOrdinal("database_state"));
        var queueState = reader.GetInt64(reader.GetOrdinal("queue_state"));
        var queueDepth = reader.GetInt64(reader.GetOrdinal("queue_depth"));
        var droppedPoints = reader.GetInt64(reader.GetOrdinal("dropped_points"));
        var writeP95Ms = reader.GetDouble(reader.GetOrdinal("write_p95_ms"));
        var mqttConnected = reader.GetInt64(reader.GetOrdinal("mqtt_connected")) == 1;
        var outboxDepth = reader.GetInt64(reader.GetOrdinal("outbox_depth"));
        var memoryUsedMb = reader.GetInt64(reader.GetOrdinal("memory_used_mb"));

        var collectorsJson = reader.IsDBNull(reader.GetOrdinal("collectors_json"))
            ? null
            : reader.GetString(reader.GetOrdinal("collectors_json"));

        Dictionary<string, CollectorHealth> collectors;

        if (string.IsNullOrWhiteSpace(collectorsJson))
        {
            collectors = new Dictionary<string, CollectorHealth>();
        }
        else
        {
            try
            {
                collectors = JsonSerializer.Deserialize<Dictionary<string, CollectorHealth>>(collectorsJson, JsonOptions)
                    ?? new Dictionary<string, CollectorHealth>();
            }
            catch (Exception ex)
            {
                // 反序列化失败：不抛异常，按空字典返回
                _logger.LogWarning(ex, "Failed to deserialize collectors_json, fallback to empty dictionary. ts={TsUtc}", tsUtc);
                collectors = new Dictionary<string, CollectorHealth>();
            }
        }

        return new HealthSnapshot
        {
            UtcTime = DateTimeOffset.FromUnixTimeMilliseconds(tsUtc),
            OverallState = (HealthState)(int)overallState,
            DatabaseState = (DatabaseState)(int)databaseState,
            QueueState = (QueueState)(int)queueState,
            QueueDepth = queueDepth,
            DroppedPoints = droppedPoints,
            WriteLatencyMsP95 = writeP95Ms,
            Collectors = collectors,
            MqttConnected = mqttConnected,
            OutboxDepth = outboxDepth,
            MemoryUsedMb = memoryUsedMb
        };
    }
}
