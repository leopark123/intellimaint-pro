using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Data.Sqlite;

namespace IntelliMaint.Infrastructure.Sqlite;

public sealed class AlarmRuleRepository : IAlarmRuleRepository
{
    private readonly IDbExecutor _db;

    public AlarmRuleRepository(IDbExecutor db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<AlarmRule>> ListAsync(CancellationToken ct)
    {
        const string sql = @"
SELECT rule_id, name, description, tag_id, device_id, condition_type, threshold,
       duration_ms, severity, message_template, enabled, created_utc, updated_utc,
       roc_window_ms, rule_type
FROM alarm_rule
ORDER BY name, rule_id;";

        return await _db.QueryAsync(sql, MapRule, null, ct);
    }

    public async Task<IReadOnlyList<AlarmRule>> ListEnabledAsync(CancellationToken ct)
    {
        const string sql = @"
SELECT rule_id, name, description, tag_id, device_id, condition_type, threshold,
       duration_ms, severity, message_template, enabled, created_utc, updated_utc,
       roc_window_ms, rule_type
FROM alarm_rule
WHERE enabled = 1
ORDER BY name, rule_id;";

        return await _db.QueryAsync(sql, MapRule, null, ct);
    }

    public async Task<AlarmRule?> GetAsync(string ruleId, CancellationToken ct)
    {
        const string sql = @"
SELECT rule_id, name, description, tag_id, device_id, condition_type, threshold,
       duration_ms, severity, message_template, enabled, created_utc, updated_utc,
       roc_window_ms, rule_type
FROM alarm_rule
WHERE rule_id = @RuleId;";

        var list = await _db.QueryAsync(sql, MapRule, new { RuleId = ruleId }, ct);
        return list.Count > 0 ? list[0] : null;
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
ON CONFLICT(rule_id) DO UPDATE SET
    name = @Name,
    description = @Description,
    tag_id = @TagId,
    device_id = @DeviceId,
    condition_type = @ConditionType,
    threshold = @Threshold,
    duration_ms = @DurationMs,
    severity = @Severity,
    message_template = @MessageTemplate,
    enabled = @Enabled,
    updated_utc = @UpdatedUtc,
    roc_window_ms = @RocWindowMs,
    rule_type = @RuleType;";

        await _db.ExecuteNonQueryAsync(sql, new
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
            Enabled = rule.Enabled ? 1 : 0,
            rule.CreatedUtc,
            rule.UpdatedUtc,
            rule.RocWindowMs,
            rule.RuleType
        }, ct);
    }

    public async Task DeleteAsync(string ruleId, CancellationToken ct)
    {
        const string sql = "DELETE FROM alarm_rule WHERE rule_id = @RuleId;";
        await _db.ExecuteNonQueryAsync(sql, new { RuleId = ruleId }, ct);
    }

    public async Task SetEnabledAsync(string ruleId, bool enabled, CancellationToken ct)
    {
        const string sql = @"
UPDATE alarm_rule
SET enabled = @Enabled,
    updated_utc = @UpdatedUtc
WHERE rule_id = @RuleId;";

        await _db.ExecuteNonQueryAsync(sql, new
        {
            RuleId = ruleId,
            Enabled = enabled ? 1 : 0,
            UpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        }, ct);
    }

    private static AlarmRule MapRule(SqliteDataReader reader)
    {
        return new AlarmRule
        {
            RuleId = reader.GetString(reader.GetOrdinal("rule_id")),
            Name = reader.GetString(reader.GetOrdinal("name")),
            Description = reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString(reader.GetOrdinal("description")),
            TagId = reader.GetString(reader.GetOrdinal("tag_id")),
            DeviceId = reader.IsDBNull(reader.GetOrdinal("device_id")) ? null : reader.GetString(reader.GetOrdinal("device_id")),
            ConditionType = reader.GetString(reader.GetOrdinal("condition_type")),
            Threshold = reader.GetDouble(reader.GetOrdinal("threshold")),
            DurationMs = reader.GetInt32(reader.GetOrdinal("duration_ms")),
            Severity = reader.GetInt32(reader.GetOrdinal("severity")),
            MessageTemplate = reader.IsDBNull(reader.GetOrdinal("message_template")) ? null : reader.GetString(reader.GetOrdinal("message_template")),
            Enabled = reader.GetInt32(reader.GetOrdinal("enabled")) == 1,
            CreatedUtc = reader.GetInt64(reader.GetOrdinal("created_utc")),
            UpdatedUtc = reader.GetInt64(reader.GetOrdinal("updated_utc")),
            // v56 新增字段
            RocWindowMs = reader.GetInt32(reader.GetOrdinal("roc_window_ms")),
            RuleType = reader.GetString(reader.GetOrdinal("rule_type"))
        };
    }
}
