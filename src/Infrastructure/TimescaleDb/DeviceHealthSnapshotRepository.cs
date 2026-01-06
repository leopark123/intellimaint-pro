using Dapper;
using IntelliMaint.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.TimescaleDb;

/// <summary>
/// TimescaleDB DeviceHealthSnapshot repository implementation
/// </summary>
public sealed class DeviceHealthSnapshotRepository : IDeviceHealthSnapshotRepository
{
    private readonly INpgsqlConnectionFactory _factory;
    private readonly ILogger<DeviceHealthSnapshotRepository> _logger;

    public DeviceHealthSnapshotRepository(INpgsqlConnectionFactory factory, ILogger<DeviceHealthSnapshotRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task SaveAsync(DeviceHealthSnapshot snapshot, CancellationToken ct)
    {
        const string sql = @"
            INSERT INTO device_health_snapshot (device_id, ts, index_score, level, deviation_score, trend_score, stability_score, alarm_score)
            VALUES (@DeviceId, @Timestamp, @Index, @Level, @DeviationScore, @TrendScore, @StabilityScore, @AlarmScore)
            ON CONFLICT (device_id, ts) DO UPDATE SET
                index_score = EXCLUDED.index_score,
                level = EXCLUDED.level,
                deviation_score = EXCLUDED.deviation_score,
                trend_score = EXCLUDED.trend_score,
                stability_score = EXCLUDED.stability_score,
                alarm_score = EXCLUDED.alarm_score";

        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            snapshot.DeviceId,
            snapshot.Timestamp,
            snapshot.Index,
            Level = (int)snapshot.Level,
            snapshot.DeviationScore,
            snapshot.TrendScore,
            snapshot.StabilityScore,
            snapshot.AlarmScore
        }, cancellationToken: ct));
    }

    public async Task SaveBatchAsync(IEnumerable<DeviceHealthSnapshot> snapshots, CancellationToken ct)
    {
        var list = snapshots.ToList();
        if (list.Count == 0) return;

        const string sql = @"
            INSERT INTO device_health_snapshot (device_id, ts, index_score, level, deviation_score, trend_score, stability_score, alarm_score)
            VALUES (@DeviceId, @Timestamp, @Index, @Level, @DeviationScore, @TrendScore, @StabilityScore, @AlarmScore)
            ON CONFLICT (device_id, ts) DO UPDATE SET
                index_score = EXCLUDED.index_score,
                level = EXCLUDED.level,
                deviation_score = EXCLUDED.deviation_score,
                trend_score = EXCLUDED.trend_score,
                stability_score = EXCLUDED.stability_score,
                alarm_score = EXCLUDED.alarm_score";

        using var conn = _factory.CreateConnection();
        foreach (var snapshot in list)
        {
            await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                snapshot.DeviceId,
                snapshot.Timestamp,
                snapshot.Index,
                Level = (int)snapshot.Level,
                snapshot.DeviationScore,
                snapshot.TrendScore,
                snapshot.StabilityScore,
                snapshot.AlarmScore
            }, cancellationToken: ct));
        }

        _logger.LogDebug("Saved {Count} health snapshots", list.Count);
    }

    public async Task<IReadOnlyList<DeviceHealthSnapshot>> GetHistoryAsync(
        string deviceId, long startTs, long endTs, CancellationToken ct)
    {
        const string sql = @"
            SELECT device_id, ts, index_score, level, deviation_score, trend_score, stability_score, alarm_score
            FROM device_health_snapshot
            WHERE device_id = @DeviceId AND ts >= @StartTs AND ts <= @EndTs
            ORDER BY ts ASC";

        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<SnapshotRow>(new CommandDefinition(sql, new
        {
            DeviceId = deviceId,
            StartTs = startTs,
            EndTs = endTs
        }, cancellationToken: ct));

        return rows.Select(MapSnapshot).ToList();
    }

    public async Task<IReadOnlyList<DeviceHealthSnapshot>> GetLatestAllAsync(CancellationToken ct)
    {
        // PostgreSQL DISTINCT ON is more efficient than subquery for this
        const string sql = @"
            SELECT DISTINCT ON (device_id) device_id, ts, index_score, level, deviation_score, trend_score, stability_score, alarm_score
            FROM device_health_snapshot
            ORDER BY device_id, ts DESC";

        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<SnapshotRow>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.Select(MapSnapshot).ToList();
    }

    public async Task<int> DeleteBeforeAsync(long cutoffTs, CancellationToken ct)
    {
        const string sql = "DELETE FROM device_health_snapshot WHERE ts < @CutoffTs";

        using var conn = _factory.CreateConnection();
        var affected = await conn.ExecuteAsync(
            new CommandDefinition(sql, new { CutoffTs = cutoffTs }, cancellationToken: ct));

        _logger.LogInformation("Deleted {Count} health snapshots before {CutoffTs}", affected, cutoffTs);
        return affected;
    }

    private static DeviceHealthSnapshot MapSnapshot(SnapshotRow row) => new()
    {
        DeviceId = row.device_id,
        Timestamp = row.ts,
        Index = row.index_score,
        Level = (HealthLevel)row.level,
        DeviationScore = row.deviation_score,
        TrendScore = row.trend_score,
        StabilityScore = row.stability_score,
        AlarmScore = row.alarm_score
    };

    private sealed class SnapshotRow
    {
        public string device_id { get; set; } = "";
        public long ts { get; set; }
        public int index_score { get; set; }
        public int level { get; set; }
        public int deviation_score { get; set; }
        public int trend_score { get; set; }
        public int stability_score { get; set; }
        public int alarm_score { get; set; }
    }
}
