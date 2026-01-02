using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Data.Sqlite;

namespace IntelliMaint.Infrastructure.Sqlite;

/// <summary>
/// 采集规则仓储实现
/// </summary>
public sealed class CollectionRuleRepository : ICollectionRuleRepository
{
    private readonly IDbExecutor _db;

    public CollectionRuleRepository(IDbExecutor db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<CollectionRule>> ListAsync(CancellationToken ct)
    {
        const string sql = @"
SELECT rule_id, name, description, device_id, enabled,
       start_condition_json, stop_condition_json, collection_config_json, post_actions_json,
       trigger_count, last_trigger_utc, created_utc, updated_utc
FROM collection_rule
ORDER BY name, rule_id;";

        return await _db.QueryAsync(sql, MapRule, null, ct);
    }

    public async Task<IReadOnlyList<CollectionRule>> ListEnabledAsync(CancellationToken ct)
    {
        const string sql = @"
SELECT rule_id, name, description, device_id, enabled,
       start_condition_json, stop_condition_json, collection_config_json, post_actions_json,
       trigger_count, last_trigger_utc, created_utc, updated_utc
FROM collection_rule
WHERE enabled = 1
ORDER BY name, rule_id;";

        return await _db.QueryAsync(sql, MapRule, null, ct);
    }

    public async Task<IReadOnlyList<CollectionRule>> ListByDeviceAsync(string deviceId, CancellationToken ct)
    {
        const string sql = @"
SELECT rule_id, name, description, device_id, enabled,
       start_condition_json, stop_condition_json, collection_config_json, post_actions_json,
       trigger_count, last_trigger_utc, created_utc, updated_utc
FROM collection_rule
WHERE device_id = @DeviceId
ORDER BY name, rule_id;";

        return await _db.QueryAsync(sql, MapRule, new { DeviceId = deviceId }, ct);
    }

    public async Task<CollectionRule?> GetAsync(string ruleId, CancellationToken ct)
    {
        const string sql = @"
SELECT rule_id, name, description, device_id, enabled,
       start_condition_json, stop_condition_json, collection_config_json, post_actions_json,
       trigger_count, last_trigger_utc, created_utc, updated_utc
FROM collection_rule
WHERE rule_id = @RuleId;";

        var list = await _db.QueryAsync(sql, MapRule, new { RuleId = ruleId }, ct);
        return list.Count > 0 ? list[0] : null;
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
ON CONFLICT(rule_id) DO UPDATE SET
    name = @Name,
    description = @Description,
    device_id = @DeviceId,
    enabled = @Enabled,
    start_condition_json = @StartConditionJson,
    stop_condition_json = @StopConditionJson,
    collection_config_json = @CollectionConfigJson,
    post_actions_json = @PostActionsJson,
    updated_utc = @UpdatedUtc;";

        await _db.ExecuteNonQueryAsync(sql, new
        {
            rule.RuleId,
            rule.Name,
            rule.Description,
            rule.DeviceId,
            Enabled = rule.Enabled ? 1 : 0,
            rule.StartConditionJson,
            rule.StopConditionJson,
            rule.CollectionConfigJson,
            rule.PostActionsJson,
            rule.TriggerCount,
            rule.LastTriggerUtc,
            rule.CreatedUtc,
            rule.UpdatedUtc
        }, ct);
    }

    public async Task DeleteAsync(string ruleId, CancellationToken ct)
    {
        // 先删除关联的片段
        const string deleteSegments = "DELETE FROM collection_segment WHERE rule_id = @RuleId;";
        await _db.ExecuteNonQueryAsync(deleteSegments, new { RuleId = ruleId }, ct);
        
        // 再删除规则
        const string deleteRule = "DELETE FROM collection_rule WHERE rule_id = @RuleId;";
        await _db.ExecuteNonQueryAsync(deleteRule, new { RuleId = ruleId }, ct);
    }

    public async Task SetEnabledAsync(string ruleId, bool enabled, CancellationToken ct)
    {
        const string sql = @"
UPDATE collection_rule
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

    public async Task IncrementTriggerCountAsync(string ruleId, CancellationToken ct)
    {
        const string sql = @"
UPDATE collection_rule
SET trigger_count = trigger_count + 1,
    last_trigger_utc = @LastTriggerUtc,
    updated_utc = @UpdatedUtc
WHERE rule_id = @RuleId;";

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.ExecuteNonQueryAsync(sql, new
        {
            RuleId = ruleId,
            LastTriggerUtc = now,
            UpdatedUtc = now
        }, ct);
    }

    private static CollectionRule MapRule(SqliteDataReader reader)
    {
        return new CollectionRule
        {
            RuleId = reader.GetString(reader.GetOrdinal("rule_id")),
            Name = reader.GetString(reader.GetOrdinal("name")),
            Description = reader.IsDBNull(reader.GetOrdinal("description")) 
                ? null : reader.GetString(reader.GetOrdinal("description")),
            DeviceId = reader.GetString(reader.GetOrdinal("device_id")),
            Enabled = reader.GetInt32(reader.GetOrdinal("enabled")) == 1,
            StartConditionJson = reader.GetString(reader.GetOrdinal("start_condition_json")),
            StopConditionJson = reader.GetString(reader.GetOrdinal("stop_condition_json")),
            CollectionConfigJson = reader.GetString(reader.GetOrdinal("collection_config_json")),
            PostActionsJson = reader.IsDBNull(reader.GetOrdinal("post_actions_json")) 
                ? null : reader.GetString(reader.GetOrdinal("post_actions_json")),
            TriggerCount = reader.GetInt32(reader.GetOrdinal("trigger_count")),
            LastTriggerUtc = reader.IsDBNull(reader.GetOrdinal("last_trigger_utc")) 
                ? null : reader.GetInt64(reader.GetOrdinal("last_trigger_utc")),
            CreatedUtc = reader.GetInt64(reader.GetOrdinal("created_utc")),
            UpdatedUtc = reader.GetInt64(reader.GetOrdinal("updated_utc"))
        };
    }
}
