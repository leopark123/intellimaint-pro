using System.Text.Json;
using IntelliMaint.Core.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.Sqlite;

/// <summary>
/// v45: 健康基线仓储 SQLite 实现
/// </summary>
public sealed class HealthBaselineRepository : IHealthBaselineRepository
{
    private readonly IDbExecutor _db;
    private readonly ILogger<HealthBaselineRepository> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public HealthBaselineRepository(IDbExecutor db, ILogger<HealthBaselineRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DeviceBaseline?> GetAsync(string deviceId, CancellationToken ct)
    {
        const string sql = @"
            SELECT device_id, created_utc, updated_utc, sample_count, learning_hours, tag_baselines_json
            FROM health_baseline
            WHERE device_id = @DeviceId";

        return await _db.QuerySingleAsync(sql, MapBaseline, new { DeviceId = deviceId }, ct);
    }

    /// <inheritdoc />
    public async Task SaveAsync(DeviceBaseline baseline, CancellationToken ct)
    {
        var tagBaselinesJson = JsonSerializer.Serialize(baseline.TagBaselines, JsonOptions);

        const string sql = @"
            INSERT INTO health_baseline (device_id, created_utc, updated_utc, sample_count, learning_hours, tag_baselines_json)
            VALUES (@DeviceId, @CreatedUtc, @UpdatedUtc, @SampleCount, @LearningHours, @TagBaselinesJson)
            ON CONFLICT(device_id) DO UPDATE SET
                updated_utc = @UpdatedUtc,
                sample_count = @SampleCount,
                learning_hours = @LearningHours,
                tag_baselines_json = @TagBaselinesJson";

        await _db.ExecuteNonQueryAsync(sql, new
        {
            DeviceId = baseline.DeviceId,
            CreatedUtc = baseline.CreatedUtc,
            UpdatedUtc = baseline.UpdatedUtc,
            SampleCount = baseline.SampleCount,
            LearningHours = baseline.LearningHours,
            TagBaselinesJson = tagBaselinesJson
        }, ct);

        _logger.LogDebug("Saved baseline for device {DeviceId}", baseline.DeviceId);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string deviceId, CancellationToken ct)
    {
        const string sql = "DELETE FROM health_baseline WHERE device_id = @DeviceId";
        await _db.ExecuteNonQueryAsync(sql, new { DeviceId = deviceId }, ct);
        _logger.LogDebug("Deleted baseline for device {DeviceId}", deviceId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DeviceBaseline>> ListAsync(CancellationToken ct)
    {
        const string sql = @"
            SELECT device_id, created_utc, updated_utc, sample_count, learning_hours, tag_baselines_json
            FROM health_baseline
            ORDER BY updated_utc DESC";

        return await _db.QueryAsync(sql, MapBaseline, null, ct);
    }

    private DeviceBaseline MapBaseline(SqliteDataReader r)
    {
        var tagBaselinesJson = r.GetString(5);
        var tagBaselines = JsonSerializer.Deserialize<Dictionary<string, TagBaseline>>(tagBaselinesJson, JsonOptions)
            ?? new Dictionary<string, TagBaseline>();

        return new DeviceBaseline
        {
            DeviceId = r.GetString(0),
            CreatedUtc = r.GetInt64(1),
            UpdatedUtc = r.GetInt64(2),
            SampleCount = r.GetInt32(3),
            LearningHours = r.GetInt32(4),
            TagBaselines = tagBaselines
        };
    }
}
