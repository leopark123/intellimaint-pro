using Dapper;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.TimescaleDb;

/// <summary>
/// TimescaleDB 采集规则仓储实现
/// </summary>
public sealed class CollectionRuleRepository : ICollectionRuleRepository
{
    private readonly INpgsqlConnectionFactory _factory;
    private readonly ILogger<CollectionRuleRepository> _logger;

    public CollectionRuleRepository(INpgsqlConnectionFactory factory, ILogger<CollectionRuleRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CollectionRule>> ListAsync(CancellationToken ct)
    {
        const string sql = @"
            SELECT rule_id, name, description, device_id, enabled,
                   start_condition_json, stop_condition_json, collection_config_json, post_actions_json,
                   trigger_count, last_trigger_utc, created_utc, updated_utc
            FROM collection_rule
            ORDER BY name, rule_id";

        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<CollectionRuleRow>(
            new CommandDefinition(sql, cancellationToken: ct));
        return rows.Select(MapToRule).ToList();
    }

    public async Task<IReadOnlyList<CollectionRule>> ListEnabledAsync(CancellationToken ct)
    {
        const string sql = @"
            SELECT rule_id, name, description, device_id, enabled,
                   start_condition_json, stop_condition_json, collection_config_json, post_actions_json,
                   trigger_count, last_trigger_utc, created_utc, updated_utc
            FROM collection_rule
            WHERE enabled = true
            ORDER BY name, rule_id";

        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<CollectionRuleRow>(
            new CommandDefinition(sql, cancellationToken: ct));
        return rows.Select(MapToRule).ToList();
    }

    public async Task<IReadOnlyList<CollectionRule>> ListByDeviceAsync(string deviceId, CancellationToken ct)
    {
        const string sql = @"
            SELECT rule_id, name, description, device_id, enabled,
                   start_condition_json, stop_condition_json, collection_config_json, post_actions_json,
                   trigger_count, last_trigger_utc, created_utc, updated_utc
            FROM collection_rule
            WHERE device_id = @DeviceId
            ORDER BY name, rule_id";

        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<CollectionRuleRow>(
            new CommandDefinition(sql, new { DeviceId = deviceId }, cancellationToken: ct));
        return rows.Select(MapToRule).ToList();
    }

    public async Task<CollectionRule?> GetAsync(string ruleId, CancellationToken ct)
    {
        const string sql = @"
            SELECT rule_id, name, description, device_id, enabled,
                   start_condition_json, stop_condition_json, collection_config_json, post_actions_json,
                   trigger_count, last_trigger_utc, created_utc, updated_utc
            FROM collection_rule
            WHERE rule_id = @RuleId";

        using var conn = _factory.CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync<CollectionRuleRow>(
            new CommandDefinition(sql, new { RuleId = ruleId }, cancellationToken: ct));
        return row is null ? null : MapToRule(row);
    }

    public async Task UpsertAsync(CollectionRule rule, CancellationToken ct)
    {
        const string sql = @"
            INSERT INTO collection_rule (
                rule_id, name, description, device_id, enabled,
                start_condition_json, stop_condition_json, collection_config_json, post_actions_json,
                trigger_count, last_trigger_utc, created_utc, updated_utc
            ) VALUES (
                @RuleId, @Name, @Description, @DeviceId, @Enabled,
                @StartConditionJson, @StopConditionJson, @CollectionConfigJson, @PostActionsJson,
                @TriggerCount, @LastTriggerUtc, @CreatedUtc, @UpdatedUtc
            )
            ON CONFLICT (rule_id) DO UPDATE SET
                name = EXCLUDED.name,
                description = EXCLUDED.description,
                device_id = EXCLUDED.device_id,
                enabled = EXCLUDED.enabled,
                start_condition_json = EXCLUDED.start_condition_json,
                stop_condition_json = EXCLUDED.stop_condition_json,
                collection_config_json = EXCLUDED.collection_config_json,
                post_actions_json = EXCLUDED.post_actions_json,
                updated_utc = EXCLUDED.updated_utc";

        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            rule.RuleId,
            rule.Name,
            rule.Description,
            rule.DeviceId,
            rule.Enabled,
            rule.StartConditionJson,
            rule.StopConditionJson,
            rule.CollectionConfigJson,
            rule.PostActionsJson,
            rule.TriggerCount,
            rule.LastTriggerUtc,
            rule.CreatedUtc,
            rule.UpdatedUtc
        }, cancellationToken: ct));

        _logger.LogDebug("Upserted collection rule {RuleId}", rule.RuleId);
    }

    public async Task DeleteAsync(string ruleId, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();

        // 先删除关联的片段
        const string deleteSegments = "DELETE FROM collection_segment WHERE rule_id = @RuleId";
        await conn.ExecuteAsync(new CommandDefinition(deleteSegments, new { RuleId = ruleId }, cancellationToken: ct));

        // 再删除规则
        const string deleteRule = "DELETE FROM collection_rule WHERE rule_id = @RuleId";
        await conn.ExecuteAsync(new CommandDefinition(deleteRule, new { RuleId = ruleId }, cancellationToken: ct));

        _logger.LogInformation("Deleted collection rule {RuleId}", ruleId);
    }

    public async Task SetEnabledAsync(string ruleId, bool enabled, CancellationToken ct)
    {
        const string sql = @"
            UPDATE collection_rule
            SET enabled = @Enabled, updated_utc = @UpdatedUtc
            WHERE rule_id = @RuleId";

        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            RuleId = ruleId,
            Enabled = enabled,
            UpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        }, cancellationToken: ct));
    }

    public async Task IncrementTriggerCountAsync(string ruleId, CancellationToken ct)
    {
        const string sql = @"
            UPDATE collection_rule
            SET trigger_count = trigger_count + 1,
                last_trigger_utc = @LastTriggerUtc,
                updated_utc = @UpdatedUtc
            WHERE rule_id = @RuleId";

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            RuleId = ruleId,
            LastTriggerUtc = now,
            UpdatedUtc = now
        }, cancellationToken: ct));
    }

    private static CollectionRule MapToRule(CollectionRuleRow row)
    {
        return new CollectionRule
        {
            RuleId = row.rule_id,
            Name = row.name,
            Description = row.description,
            DeviceId = row.device_id,
            Enabled = row.enabled,
            StartConditionJson = row.start_condition_json,
            StopConditionJson = row.stop_condition_json,
            CollectionConfigJson = row.collection_config_json,
            PostActionsJson = row.post_actions_json,
            TriggerCount = row.trigger_count,
            LastTriggerUtc = row.last_trigger_utc,
            CreatedUtc = row.created_utc,
            UpdatedUtc = row.updated_utc
        };
    }

    private sealed class CollectionRuleRow
    {
        public string rule_id { get; set; } = "";
        public string name { get; set; } = "";
        public string? description { get; set; }
        public string device_id { get; set; } = "";
        public bool enabled { get; set; }
        public string start_condition_json { get; set; } = "{}";
        public string stop_condition_json { get; set; } = "{}";
        public string collection_config_json { get; set; } = "{}";
        public string? post_actions_json { get; set; }
        public int trigger_count { get; set; }
        public long? last_trigger_utc { get; set; }
        public long created_utc { get; set; }
        public long updated_utc { get; set; }
    }
}
