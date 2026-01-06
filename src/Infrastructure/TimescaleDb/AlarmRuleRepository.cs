using Dapper;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.TimescaleDb;

/// <summary>
/// TimescaleDB AlarmRule repository implementation
/// </summary>
public sealed class AlarmRuleRepository : IAlarmRuleRepository
{
    private readonly INpgsqlConnectionFactory _factory;
    private readonly ILogger<AlarmRuleRepository> _logger;

    public AlarmRuleRepository(INpgsqlConnectionFactory factory, ILogger<AlarmRuleRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AlarmRule>> ListAsync(CancellationToken ct)
    {
        const string sql = @"
            SELECT rule_id, name, description, tag_id, device_id, condition_type, threshold,
                   duration_ms, severity, message_template, enabled, created_utc, updated_utc,
                   roc_window_ms, rule_type
            FROM alarm_rule
            ORDER BY name, rule_id";

        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<AlarmRuleRow>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.Select(MapToRule).ToList();
    }

    public async Task<IReadOnlyList<AlarmRule>> ListEnabledAsync(CancellationToken ct)
    {
        const string sql = @"
            SELECT rule_id, name, description, tag_id, device_id, condition_type, threshold,
                   duration_ms, severity, message_template, enabled, created_utc, updated_utc,
                   roc_window_ms, rule_type
            FROM alarm_rule
            WHERE enabled = true
            ORDER BY name, rule_id";

        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<AlarmRuleRow>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.Select(MapToRule).ToList();
    }

    public async Task<AlarmRule?> GetAsync(string ruleId, CancellationToken ct)
    {
        const string sql = @"
            SELECT rule_id, name, description, tag_id, device_id, condition_type, threshold,
                   duration_ms, severity, message_template, enabled, created_utc, updated_utc,
                   roc_window_ms, rule_type
            FROM alarm_rule
            WHERE rule_id = @RuleId";

        using var conn = _factory.CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync<AlarmRuleRow>(
            new CommandDefinition(sql, new { RuleId = ruleId }, cancellationToken: ct));
        return row is null ? null : MapToRule(row);
    }

    public async Task UpsertAsync(AlarmRule rule, CancellationToken ct)
    {
        const string sql = @"
            INSERT INTO alarm_rule (
                rule_id, name, description, tag_id, device_id, condition_type, threshold,
                duration_ms, severity, message_template, enabled, created_utc, updated_utc,
                roc_window_ms, rule_type
            ) VALUES (
                @RuleId, @Name, @Description, @TagId, @DeviceId, @ConditionType, @Threshold,
                @DurationMs, @Severity, @MessageTemplate, @Enabled, @CreatedUtc, @UpdatedUtc,
                @RocWindowMs, @RuleType
            )
            ON CONFLICT (rule_id) DO UPDATE SET
                name = EXCLUDED.name,
                description = EXCLUDED.description,
                tag_id = EXCLUDED.tag_id,
                device_id = EXCLUDED.device_id,
                condition_type = EXCLUDED.condition_type,
                threshold = EXCLUDED.threshold,
                duration_ms = EXCLUDED.duration_ms,
                severity = EXCLUDED.severity,
                message_template = EXCLUDED.message_template,
                enabled = EXCLUDED.enabled,
                updated_utc = EXCLUDED.updated_utc,
                roc_window_ms = EXCLUDED.roc_window_ms,
                rule_type = EXCLUDED.rule_type";

        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            rule.RuleId,
            rule.Name,
            rule.Description,
            rule.TagId,
            rule.DeviceId,
            rule.ConditionType,
            rule.Threshold,
            rule.DurationMs,
            rule.Severity,
            rule.MessageTemplate,
            rule.Enabled,
            rule.CreatedUtc,
            rule.UpdatedUtc,
            rule.RocWindowMs,
            rule.RuleType
        }, cancellationToken: ct));

        _logger.LogDebug("Upserted alarm rule {RuleId}", rule.RuleId);
    }

    public async Task DeleteAsync(string ruleId, CancellationToken ct)
    {
        const string sql = "DELETE FROM alarm_rule WHERE rule_id = @RuleId";
        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new { RuleId = ruleId }, cancellationToken: ct));
        _logger.LogInformation("Deleted alarm rule {RuleId}", ruleId);
    }

    public async Task SetEnabledAsync(string ruleId, bool enabled, CancellationToken ct)
    {
        const string sql = @"
            UPDATE alarm_rule
            SET enabled = @Enabled, updated_utc = @UpdatedUtc
            WHERE rule_id = @RuleId";

        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            RuleId = ruleId,
            Enabled = enabled,
            UpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        }, cancellationToken: ct));

        _logger.LogInformation("Set alarm rule {RuleId} enabled={Enabled}", ruleId, enabled);
    }

    private static AlarmRule MapToRule(AlarmRuleRow row)
    {
        return new AlarmRule
        {
            RuleId = row.rule_id,
            Name = row.name,
            Description = row.description,
            TagId = row.tag_id,
            DeviceId = row.device_id,
            ConditionType = row.condition_type,
            Threshold = row.threshold,
            DurationMs = row.duration_ms,
            Severity = row.severity,
            MessageTemplate = row.message_template,
            Enabled = row.enabled,
            CreatedUtc = row.created_utc,
            UpdatedUtc = row.updated_utc,
            RocWindowMs = row.roc_window_ms,
            RuleType = row.rule_type ?? "threshold"
        };
    }

    private sealed class AlarmRuleRow
    {
        public string rule_id { get; set; } = "";
        public string name { get; set; } = "";
        public string? description { get; set; }
        public string tag_id { get; set; } = "";
        public string? device_id { get; set; }
        public string condition_type { get; set; } = "";
        public double threshold { get; set; }
        public int duration_ms { get; set; }
        public int severity { get; set; }
        public string? message_template { get; set; }
        public bool enabled { get; set; }
        public long created_utc { get; set; }
        public long updated_utc { get; set; }
        public int roc_window_ms { get; set; }
        public string? rule_type { get; set; }
    }
}
