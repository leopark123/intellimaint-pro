using IntelliMaint.Core.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.Sqlite;

/// <summary>
/// v60: 设备健康快照仓储 - SQLite 实现
/// </summary>
public sealed class DeviceHealthSnapshotRepository : IDeviceHealthSnapshotRepository
{
    private readonly IDbExecutor _db;
    private readonly ILogger<DeviceHealthSnapshotRepository> _logger;

    public DeviceHealthSnapshotRepository(IDbExecutor db, ILogger<DeviceHealthSnapshotRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task SaveAsync(DeviceHealthSnapshot snapshot, CancellationToken ct)
    {
        const string sql = @"
INSERT OR REPLACE INTO device_health_snapshot
    (device_id, ts, index_score, level, deviation_score, trend_score, stability_score, alarm_score)
VALUES
    (@DeviceId, @Timestamp, @Index, @Level, @DeviationScore, @TrendScore, @StabilityScore, @AlarmScore)";

        await _db.ExecuteNonQueryAsync(sql, new
        {
            snapshot.DeviceId,
            snapshot.Timestamp,
            snapshot.Index,
            Level = (int)snapshot.Level,
            snapshot.DeviationScore,
            snapshot.TrendScore,
            snapshot.StabilityScore,
            snapshot.AlarmScore
        }, ct);
    }

    public async Task SaveBatchAsync(IEnumerable<DeviceHealthSnapshot> snapshots, CancellationToken ct)
    {
        var list = snapshots.ToList();
        if (list.Count == 0) return;

        const string sql = @"
INSERT OR REPLACE INTO device_health_snapshot
    (device_id, ts, index_score, level, deviation_score, trend_score, stability_score, alarm_score)
VALUES
    (@DeviceId, @Timestamp, @Index, @Level, @DeviationScore, @TrendScore, @StabilityScore, @AlarmScore)";

        // 使用 DbExecutor 批量执行
        foreach (var snapshot in list)
        {
            await _db.ExecuteNonQueryAsync(sql, new
            {
                snapshot.DeviceId,
                snapshot.Timestamp,
                snapshot.Index,
                Level = (int)snapshot.Level,
                snapshot.DeviationScore,
                snapshot.TrendScore,
                snapshot.StabilityScore,
                snapshot.AlarmScore
            }, ct);
        }

        _logger.LogDebug("Saved {Count} health snapshots", list.Count);
    }

    public async Task<IReadOnlyList<DeviceHealthSnapshot>> GetHistoryAsync(
        string deviceId, long startTs, long endTs, CancellationToken ct)
    {
        const string sql = @"
SELECT device_id, ts, index_score, level, deviation_score, trend_score, stability_score, alarm_score
FROM device_health_snapshot
WHERE device_id = @DeviceId
  AND ts >= @StartTs
  AND ts <= @EndTs
ORDER BY ts ASC";

        return await _db.QueryAsync(sql, MapSnapshot, new { DeviceId = deviceId, StartTs = startTs, EndTs = endTs }, ct);
    }

    public async Task<IReadOnlyList<DeviceHealthSnapshot>> GetLatestAllAsync(CancellationToken ct)
    {
        // 获取每个设备的最新快照
        const string sql = @"
SELECT s.device_id, s.ts, s.index_score, s.level, s.deviation_score, s.trend_score, s.stability_score, s.alarm_score
FROM device_health_snapshot s
INNER JOIN (
    SELECT device_id, MAX(ts) AS max_ts
    FROM device_health_snapshot
    GROUP BY device_id
) latest ON s.device_id = latest.device_id AND s.ts = latest.max_ts";

        return await _db.QueryAsync(sql, MapSnapshot, new { }, ct);
    }

    public async Task<int> DeleteBeforeAsync(long cutoffTs, CancellationToken ct)
    {
        const string sql = "DELETE FROM device_health_snapshot WHERE ts < @CutoffTs";
        return await _db.ExecuteNonQueryAsync(sql, new { CutoffTs = cutoffTs }, ct);
    }

    private static DeviceHealthSnapshot MapSnapshot(SqliteDataReader reader) => new()
    {
        DeviceId = reader.GetString(reader.GetOrdinal("device_id")),
        Timestamp = reader.GetInt64(reader.GetOrdinal("ts")),
        Index = reader.GetInt32(reader.GetOrdinal("index_score")),
        Level = (HealthLevel)reader.GetInt32(reader.GetOrdinal("level")),
        DeviationScore = reader.GetInt32(reader.GetOrdinal("deviation_score")),
        TrendScore = reader.GetInt32(reader.GetOrdinal("trend_score")),
        StabilityScore = reader.GetInt32(reader.GetOrdinal("stability_score")),
        AlarmScore = reader.GetInt32(reader.GetOrdinal("alarm_score"))
    };
}
