using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Data.Sqlite;

namespace IntelliMaint.Infrastructure.Sqlite;

/// <summary>
/// 周期分析基线仓储实现
/// </summary>
public sealed class CycleDeviceBaselineRepository : ICycleDeviceBaselineRepository
{
    private readonly IDbExecutor _db;

    public CycleDeviceBaselineRepository(IDbExecutor db)
    {
        _db = db;
    }

    public async Task<CycleDeviceBaseline?> GetAsync(string deviceId, string baselineType, CancellationToken ct)
    {
        const string sql = @"
SELECT device_id, baseline_type, sample_count, updated_utc, model_json, stats_json
FROM device_baseline
WHERE device_id = @DeviceId AND baseline_type = @BaselineType;";

        var list = await _db.QueryAsync(sql, MapBaseline, new { DeviceId = deviceId, BaselineType = baselineType }, ct);
        return list.Count > 0 ? list[0] : null;
    }

    public async Task<IReadOnlyList<CycleDeviceBaseline>> GetAllByDeviceAsync(string deviceId, CancellationToken ct)
    {
        const string sql = @"
SELECT device_id, baseline_type, sample_count, updated_utc, model_json, stats_json
FROM device_baseline
WHERE device_id = @DeviceId
ORDER BY baseline_type;";

        return await _db.QueryAsync(sql, MapBaseline, new { DeviceId = deviceId }, ct);
    }

    public async Task UpsertAsync(CycleDeviceBaseline baseline, CancellationToken ct)
    {
        const string sql = @"
INSERT INTO device_baseline (device_id, baseline_type, sample_count, updated_utc, model_json, stats_json)
VALUES (@DeviceId, @BaselineType, @SampleCount, @UpdatedUtc, @ModelJson, @StatsJson)
ON CONFLICT(device_id, baseline_type) DO UPDATE SET
    sample_count = @SampleCount,
    updated_utc = @UpdatedUtc,
    model_json = @ModelJson,
    stats_json = @StatsJson;";

        await _db.ExecuteNonQueryAsync(sql, new
        {
            baseline.DeviceId,
            baseline.BaselineType,
            baseline.SampleCount,
            baseline.UpdatedUtc,
            baseline.ModelJson,
            baseline.StatsJson
        }, ct);
    }

    public async Task DeleteAsync(string deviceId, string baselineType, CancellationToken ct)
    {
        const string sql = "DELETE FROM device_baseline WHERE device_id = @DeviceId AND baseline_type = @BaselineType;";
        await _db.ExecuteNonQueryAsync(sql, new { DeviceId = deviceId, BaselineType = baselineType }, ct);
    }

    private static CycleDeviceBaseline MapBaseline(SqliteDataReader reader)
    {
        return new CycleDeviceBaseline
        {
            DeviceId = reader.GetString(reader.GetOrdinal("device_id")),
            BaselineType = reader.GetString(reader.GetOrdinal("baseline_type")),
            SampleCount = reader.GetInt32(reader.GetOrdinal("sample_count")),
            UpdatedUtc = reader.GetInt64(reader.GetOrdinal("updated_utc")),
            ModelJson = reader.GetString(reader.GetOrdinal("model_json")),
            StatsJson = reader.IsDBNull(reader.GetOrdinal("stats_json")) 
                ? null : reader.GetString(reader.GetOrdinal("stats_json"))
        };
    }
}
