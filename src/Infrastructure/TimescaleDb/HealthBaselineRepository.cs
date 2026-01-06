using System.Text.Json;
using Dapper;
using IntelliMaint.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.TimescaleDb;

/// <summary>
/// TimescaleDB HealthBaseline repository implementation
/// </summary>
public sealed class HealthBaselineRepository : IHealthBaselineRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly INpgsqlConnectionFactory _factory;
    private readonly ILogger<HealthBaselineRepository> _logger;

    public HealthBaselineRepository(INpgsqlConnectionFactory factory, ILogger<HealthBaselineRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<DeviceBaseline?> GetAsync(string deviceId, CancellationToken ct)
    {
        const string sql = @"
            SELECT device_id, created_utc, updated_utc, sample_count, learning_hours, tag_baselines_json
            FROM health_baseline
            WHERE device_id = @DeviceId";

        using var conn = _factory.CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync<BaselineRow>(
            new CommandDefinition(sql, new { DeviceId = deviceId }, cancellationToken: ct));

        return row is null ? null : MapToBaseline(row);
    }

    public async Task SaveAsync(DeviceBaseline baseline, CancellationToken ct)
    {
        var tagBaselinesJson = JsonSerializer.Serialize(baseline.TagBaselines, JsonOptions);
        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        const string sql = @"
            INSERT INTO health_baseline (device_id, created_utc, updated_utc, sample_count, learning_hours, tag_baselines_json)
            VALUES (@DeviceId, @CreatedUtc, @UpdatedUtc, @SampleCount, @LearningHours, @TagBaselinesJson)
            ON CONFLICT (device_id) DO UPDATE SET
                updated_utc = EXCLUDED.updated_utc,
                sample_count = EXCLUDED.sample_count,
                learning_hours = EXCLUDED.learning_hours,
                tag_baselines_json = EXCLUDED.tag_baselines_json";

        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            DeviceId = baseline.DeviceId,
            CreatedUtc = baseline.CreatedUtc > 0 ? baseline.CreatedUtc : nowUtc,
            UpdatedUtc = nowUtc,
            SampleCount = baseline.SampleCount,
            LearningHours = baseline.LearningHours,
            TagBaselinesJson = tagBaselinesJson
        }, cancellationToken: ct));

        _logger.LogDebug("Saved baseline for device {DeviceId}", baseline.DeviceId);
    }

    public async Task DeleteAsync(string deviceId, CancellationToken ct)
    {
        const string sql = "DELETE FROM health_baseline WHERE device_id = @DeviceId";

        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new { DeviceId = deviceId }, cancellationToken: ct));

        _logger.LogDebug("Deleted baseline for device {DeviceId}", deviceId);
    }

    public async Task<IReadOnlyList<DeviceBaseline>> ListAsync(CancellationToken ct)
    {
        const string sql = @"
            SELECT device_id, created_utc, updated_utc, sample_count, learning_hours, tag_baselines_json
            FROM health_baseline
            ORDER BY updated_utc DESC";

        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<BaselineRow>(new CommandDefinition(sql, cancellationToken: ct));

        return rows.Select(MapToBaseline).ToList();
    }

    private DeviceBaseline MapToBaseline(BaselineRow row)
    {
        Dictionary<string, TagBaseline> tagBaselines;
        if (string.IsNullOrWhiteSpace(row.tag_baselines_json))
        {
            tagBaselines = new Dictionary<string, TagBaseline>();
        }
        else
        {
            try
            {
                tagBaselines = JsonSerializer.Deserialize<Dictionary<string, TagBaseline>>(row.tag_baselines_json, JsonOptions)
                    ?? new Dictionary<string, TagBaseline>();
            }
            catch
            {
                tagBaselines = new Dictionary<string, TagBaseline>();
            }
        }

        return new DeviceBaseline
        {
            DeviceId = row.device_id,
            CreatedUtc = row.created_utc,
            UpdatedUtc = row.updated_utc,
            SampleCount = row.sample_count,
            LearningHours = row.learning_hours,
            TagBaselines = tagBaselines
        };
    }

    private sealed class BaselineRow
    {
        public string device_id { get; set; } = "";
        public long created_utc { get; set; }
        public long updated_utc { get; set; }
        public int sample_count { get; set; }
        public int learning_hours { get; set; }
        public string? tag_baselines_json { get; set; }
    }
}
