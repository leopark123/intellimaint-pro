using System.Text.Json;
using Dapper;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.TimescaleDb;

/// <summary>
/// TimescaleDB CycleDeviceBaseline repository implementation
/// </summary>
public sealed class CycleDeviceBaselineRepository : ICycleDeviceBaselineRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly INpgsqlConnectionFactory _factory;
    private readonly ILogger<CycleDeviceBaselineRepository> _logger;

    public CycleDeviceBaselineRepository(INpgsqlConnectionFactory factory, ILogger<CycleDeviceBaselineRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task UpsertAsync(CycleDeviceBaseline baseline, CancellationToken ct)
    {
        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        const string sql = @"
            INSERT INTO cycle_device_baseline (device_id, baseline_type, sample_count, model_json, stats_json, updated_utc)
            VALUES (@DeviceId, @BaselineType, @SampleCount, @ModelJson, @StatsJson, @UpdatedUtc)
            ON CONFLICT (device_id, baseline_type) DO UPDATE SET
                sample_count = EXCLUDED.sample_count,
                model_json = EXCLUDED.model_json,
                stats_json = EXCLUDED.stats_json,
                updated_utc = EXCLUDED.updated_utc";

        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            baseline.DeviceId,
            baseline.BaselineType,
            baseline.SampleCount,
            baseline.ModelJson,
            baseline.StatsJson,
            UpdatedUtc = nowUtc
        }, cancellationToken: ct));

        _logger.LogDebug("Upserted cycle baseline for device {DeviceId}, type={BaselineType}",
            baseline.DeviceId, baseline.BaselineType);
    }

    public async Task<CycleDeviceBaseline?> GetAsync(string deviceId, string baselineType, CancellationToken ct)
    {
        const string sql = @"
            SELECT device_id, baseline_type, sample_count, model_json, stats_json, updated_utc
            FROM cycle_device_baseline
            WHERE device_id = @DeviceId AND baseline_type = @BaselineType";

        using var conn = _factory.CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync<BaselineRow>(
            new CommandDefinition(sql, new { DeviceId = deviceId, BaselineType = baselineType }, cancellationToken: ct));

        return row is null ? null : MapToBaseline(row);
    }

    public async Task<IReadOnlyList<CycleDeviceBaseline>> GetAllByDeviceAsync(string deviceId, CancellationToken ct)
    {
        const string sql = @"
            SELECT device_id, baseline_type, sample_count, model_json, stats_json, updated_utc
            FROM cycle_device_baseline
            WHERE device_id = @DeviceId
            ORDER BY baseline_type";

        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<BaselineRow>(
            new CommandDefinition(sql, new { DeviceId = deviceId }, cancellationToken: ct));

        return rows.Select(MapToBaseline).ToList();
    }

    public async Task DeleteAsync(string deviceId, string baselineType, CancellationToken ct)
    {
        const string sql = "DELETE FROM cycle_device_baseline WHERE device_id = @DeviceId AND baseline_type = @BaselineType";

        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(
            new CommandDefinition(sql, new { DeviceId = deviceId, BaselineType = baselineType }, cancellationToken: ct));

        _logger.LogInformation("Deleted cycle baseline for device {DeviceId}, type={BaselineType}",
            deviceId, baselineType);
    }

    private static CycleDeviceBaseline MapToBaseline(BaselineRow row) => new()
    {
        DeviceId = row.device_id,
        BaselineType = row.baseline_type,
        SampleCount = row.sample_count,
        ModelJson = row.model_json,
        StatsJson = row.stats_json,
        UpdatedUtc = row.updated_utc
    };

    private sealed class BaselineRow
    {
        public string device_id { get; set; } = "";
        public string baseline_type { get; set; } = "";
        public int sample_count { get; set; }
        public string model_json { get; set; } = "{}";
        public string? stats_json { get; set; }
        public long updated_utc { get; set; }
    }
}
