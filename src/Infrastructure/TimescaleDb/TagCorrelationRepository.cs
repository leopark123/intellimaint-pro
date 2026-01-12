using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace IntelliMaint.Infrastructure.TimescaleDb;

/// <summary>
/// v62: 标签关联规则仓储 - TimescaleDB 实现
/// </summary>
public sealed class TagCorrelationRepository : ITagCorrelationRepository
{
    private readonly INpgsqlConnectionFactory _factory;
    private readonly ILogger<TagCorrelationRepository> _logger;

    public TagCorrelationRepository(INpgsqlConnectionFactory factory, ILogger<TagCorrelationRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TagCorrelationRule>> ListAsync(CancellationToken ct)
    {
        const string sql = @"
            SELECT id, name, device_pattern, tag1_pattern, tag2_pattern,
                   correlation_type, threshold, risk_description, penalty_score,
                   enabled, priority, created_utc, updated_utc
            FROM tag_correlation_rule
            ORDER BY priority DESC, id ASC";

        using var conn = _factory.CreateConnection();
        using var cmd = new NpgsqlCommand(sql, conn);

        var results = new List<TagCorrelationRule>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(MapRule(reader));
        }
        return results;
    }

    public async Task<IReadOnlyList<TagCorrelationRule>> ListEnabledAsync(CancellationToken ct)
    {
        const string sql = @"
            SELECT id, name, device_pattern, tag1_pattern, tag2_pattern,
                   correlation_type, threshold, risk_description, penalty_score,
                   enabled, priority, created_utc, updated_utc
            FROM tag_correlation_rule
            WHERE enabled = true
            ORDER BY priority DESC, id ASC";

        using var conn = _factory.CreateConnection();
        using var cmd = new NpgsqlCommand(sql, conn);

        var results = new List<TagCorrelationRule>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(MapRule(reader));
        }
        return results;
    }

    public async Task<IReadOnlyList<TagCorrelationRule>> ListByDevicePatternAsync(string deviceId, CancellationToken ct)
    {
        var allRules = await ListEnabledAsync(ct);
        return allRules.Where(r => MatchDevicePattern(r.DevicePattern, deviceId)).ToList();
    }

    public async Task<TagCorrelationRule?> GetAsync(int id, CancellationToken ct)
    {
        const string sql = @"
            SELECT id, name, device_pattern, tag1_pattern, tag2_pattern,
                   correlation_type, threshold, risk_description, penalty_score,
                   enabled, priority, created_utc, updated_utc
            FROM tag_correlation_rule
            WHERE id = @Id";

        using var conn = _factory.CreateConnection();
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", id);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return MapRule(reader);
        }
        return null;
    }

    public async Task<int> CreateAsync(TagCorrelationRule rule, CancellationToken ct)
    {
        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        const string sql = @"
            INSERT INTO tag_correlation_rule
                (name, device_pattern, tag1_pattern, tag2_pattern, correlation_type,
                 threshold, risk_description, penalty_score, enabled, priority, created_utc, updated_utc)
            VALUES
                (@Name, @DevicePattern, @Tag1Pattern, @Tag2Pattern, @CorrelationType,
                 @Threshold, @RiskDescription, @PenaltyScore, @Enabled, @Priority, @CreatedUtc, @UpdatedUtc)
            RETURNING id";

        using var conn = _factory.CreateConnection();
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Name", rule.Name);
        cmd.Parameters.AddWithValue("@DevicePattern", (object?)rule.DevicePattern ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Tag1Pattern", rule.Tag1Pattern);
        cmd.Parameters.AddWithValue("@Tag2Pattern", rule.Tag2Pattern);
        cmd.Parameters.AddWithValue("@CorrelationType", (int)rule.CorrelationType);
        cmd.Parameters.AddWithValue("@Threshold", rule.Threshold);
        cmd.Parameters.AddWithValue("@RiskDescription", (object?)rule.RiskDescription ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@PenaltyScore", rule.PenaltyScore);
        cmd.Parameters.AddWithValue("@Enabled", rule.Enabled);
        cmd.Parameters.AddWithValue("@Priority", rule.Priority);
        cmd.Parameters.AddWithValue("@CreatedUtc", nowUtc);
        cmd.Parameters.AddWithValue("@UpdatedUtc", nowUtc);

        var result = await cmd.ExecuteScalarAsync(ct);
        var id = Convert.ToInt32(result);

        _logger.LogInformation("Created tag correlation rule {Id}: {Name}", id, rule.Name);
        return id;
    }

    public async Task UpdateAsync(TagCorrelationRule rule, CancellationToken ct)
    {
        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        const string sql = @"
            UPDATE tag_correlation_rule
            SET name = @Name,
                device_pattern = @DevicePattern,
                tag1_pattern = @Tag1Pattern,
                tag2_pattern = @Tag2Pattern,
                correlation_type = @CorrelationType,
                threshold = @Threshold,
                risk_description = @RiskDescription,
                penalty_score = @PenaltyScore,
                enabled = @Enabled,
                priority = @Priority,
                updated_utc = @UpdatedUtc
            WHERE id = @Id";

        using var conn = _factory.CreateConnection();
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", rule.Id);
        cmd.Parameters.AddWithValue("@Name", rule.Name);
        cmd.Parameters.AddWithValue("@DevicePattern", (object?)rule.DevicePattern ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Tag1Pattern", rule.Tag1Pattern);
        cmd.Parameters.AddWithValue("@Tag2Pattern", rule.Tag2Pattern);
        cmd.Parameters.AddWithValue("@CorrelationType", (int)rule.CorrelationType);
        cmd.Parameters.AddWithValue("@Threshold", rule.Threshold);
        cmd.Parameters.AddWithValue("@RiskDescription", (object?)rule.RiskDescription ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@PenaltyScore", rule.PenaltyScore);
        cmd.Parameters.AddWithValue("@Enabled", rule.Enabled);
        cmd.Parameters.AddWithValue("@Priority", rule.Priority);
        cmd.Parameters.AddWithValue("@UpdatedUtc", nowUtc);

        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogInformation("Updated tag correlation rule {Id}", rule.Id);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        const string sql = "DELETE FROM tag_correlation_rule WHERE id = @Id";

        using var conn = _factory.CreateConnection();
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", id);

        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogInformation("Deleted tag correlation rule {Id}", id);
    }

    public async Task SetEnabledAsync(int id, bool enabled, CancellationToken ct)
    {
        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        const string sql = @"
            UPDATE tag_correlation_rule
            SET enabled = @Enabled, updated_utc = @UpdatedUtc
            WHERE id = @Id";

        using var conn = _factory.CreateConnection();
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@Enabled", enabled);
        cmd.Parameters.AddWithValue("@UpdatedUtc", nowUtc);

        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogInformation("Set tag correlation rule {Id} enabled={Enabled}", id, enabled);
    }

    private static bool MatchDevicePattern(string? pattern, string deviceId)
    {
        if (string.IsNullOrEmpty(pattern) || pattern == "*")
            return true;

        if (pattern.StartsWith('*') && pattern.EndsWith('*'))
        {
            var middle = pattern[1..^1];
            return deviceId.Contains(middle, StringComparison.OrdinalIgnoreCase);
        }
        if (pattern.StartsWith('*'))
        {
            return deviceId.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase);
        }
        if (pattern.EndsWith('*'))
        {
            return deviceId.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);
        }
        return deviceId.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static TagCorrelationRule MapRule(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetInt32(reader.GetOrdinal("id")),
        Name = reader.GetString(reader.GetOrdinal("name")),
        DevicePattern = reader.IsDBNull(reader.GetOrdinal("device_pattern"))
            ? null
            : reader.GetString(reader.GetOrdinal("device_pattern")),
        Tag1Pattern = reader.GetString(reader.GetOrdinal("tag1_pattern")),
        Tag2Pattern = reader.GetString(reader.GetOrdinal("tag2_pattern")),
        CorrelationType = (CorrelationType)reader.GetInt32(reader.GetOrdinal("correlation_type")),
        Threshold = reader.GetDouble(reader.GetOrdinal("threshold")),
        RiskDescription = reader.IsDBNull(reader.GetOrdinal("risk_description"))
            ? null
            : reader.GetString(reader.GetOrdinal("risk_description")),
        PenaltyScore = reader.GetInt32(reader.GetOrdinal("penalty_score")),
        Enabled = reader.GetBoolean(reader.GetOrdinal("enabled")),
        Priority = reader.GetInt32(reader.GetOrdinal("priority")),
        CreatedUtc = reader.GetInt64(reader.GetOrdinal("created_utc")),
        UpdatedUtc = reader.GetInt64(reader.GetOrdinal("updated_utc"))
    };
}
