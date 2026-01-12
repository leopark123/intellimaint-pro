using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.Sqlite;

/// <summary>
/// v62: 标签关联规则仓储 - SQLite 实现
/// </summary>
public sealed class TagCorrelationRepository : ITagCorrelationRepository
{
    private readonly IDbExecutor _db;
    private readonly ILogger<TagCorrelationRepository> _logger;

    public TagCorrelationRepository(IDbExecutor db, ILogger<TagCorrelationRepository> logger)
    {
        _db = db;
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

        return await _db.QueryAsync(sql, MapRule, new { }, ct);
    }

    public async Task<IReadOnlyList<TagCorrelationRule>> ListEnabledAsync(CancellationToken ct)
    {
        const string sql = @"
            SELECT id, name, device_pattern, tag1_pattern, tag2_pattern,
                   correlation_type, threshold, risk_description, penalty_score,
                   enabled, priority, created_utc, updated_utc
            FROM tag_correlation_rule
            WHERE enabled = 1
            ORDER BY priority DESC, id ASC";

        return await _db.QueryAsync(sql, MapRule, new { }, ct);
    }

    public async Task<IReadOnlyList<TagCorrelationRule>> ListByDevicePatternAsync(string deviceId, CancellationToken ct)
    {
        // 获取所有启用的规则，然后在内存中匹配设备模式
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

        var results = await _db.QueryAsync(sql, MapRule, new { Id = id }, ct);
        return results.FirstOrDefault();
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
                 @Threshold, @RiskDescription, @PenaltyScore, @Enabled, @Priority, @CreatedUtc, @UpdatedUtc);
            SELECT last_insert_rowid();";

        var id = await _db.ExecuteScalarAsync<long>(sql, new
        {
            rule.Name,
            rule.DevicePattern,
            rule.Tag1Pattern,
            rule.Tag2Pattern,
            CorrelationType = (int)rule.CorrelationType,
            rule.Threshold,
            rule.RiskDescription,
            rule.PenaltyScore,
            Enabled = rule.Enabled ? 1 : 0,
            rule.Priority,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc
        }, ct);

        _logger.LogInformation("Created tag correlation rule {Id}: {Name}", id, rule.Name);
        return (int)id;
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

        await _db.ExecuteNonQueryAsync(sql, new
        {
            rule.Id,
            rule.Name,
            rule.DevicePattern,
            rule.Tag1Pattern,
            rule.Tag2Pattern,
            CorrelationType = (int)rule.CorrelationType,
            rule.Threshold,
            rule.RiskDescription,
            rule.PenaltyScore,
            Enabled = rule.Enabled ? 1 : 0,
            rule.Priority,
            UpdatedUtc = nowUtc
        }, ct);
        _logger.LogInformation("Updated tag correlation rule {Id}", rule.Id);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        const string sql = "DELETE FROM tag_correlation_rule WHERE id = @Id";
        await _db.ExecuteNonQueryAsync(sql, new { Id = id }, ct);
        _logger.LogInformation("Deleted tag correlation rule {Id}", id);
    }

    public async Task SetEnabledAsync(int id, bool enabled, CancellationToken ct)
    {
        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        const string sql = @"
            UPDATE tag_correlation_rule
            SET enabled = @Enabled, updated_utc = @UpdatedUtc
            WHERE id = @Id";

        await _db.ExecuteNonQueryAsync(sql, new { Id = id, Enabled = enabled ? 1 : 0, UpdatedUtc = nowUtc }, ct);
        _logger.LogInformation("Set tag correlation rule {Id} enabled={Enabled}", id, enabled);
    }

    private static bool MatchDevicePattern(string? pattern, string deviceId)
    {
        if (string.IsNullOrEmpty(pattern) || pattern == "*")
            return true;

        // 简单通配符匹配
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

    private static TagCorrelationRule MapRule(SqliteDataReader reader) => new()
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
        Enabled = reader.GetInt32(reader.GetOrdinal("enabled")) == 1,
        Priority = reader.GetInt32(reader.GetOrdinal("priority")),
        CreatedUtc = reader.GetInt64(reader.GetOrdinal("created_utc")),
        UpdatedUtc = reader.GetInt64(reader.GetOrdinal("updated_utc"))
    };
}
