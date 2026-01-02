using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using IntelliMaint.Host.Api.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace IntelliMaint.Host.Api.Endpoints;

/// <summary>
/// 采集规则 API 端点
/// </summary>
public static class CollectionRuleEndpoints
{
    // JSON 序列化选项 - 使用 camelCase
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly HashSet<string> AllowedOperators = new(StringComparer.OrdinalIgnoreCase)
    {
        "gt", "gte", "lt", "lte", "eq", "ne"
    };

    private static readonly HashSet<string> AllowedLogics = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "or"
    };

    private static readonly HashSet<string> AllowedConditionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "tag", "duration"
    };

    public static void MapCollectionRuleEndpoints(this IEndpointRouteBuilder app)
    {
        // 采集规则端点
        var ruleGroup = app.MapGroup("/api/collection-rules")
            .WithTags("CollectionRules");

        // 读操作 - 所有已认证用户
        ruleGroup.MapGet("/", ListRulesAsync)
            .RequireAuthorization(AuthPolicies.AllAuthenticated);
        ruleGroup.MapGet("/{ruleId}", GetRuleAsync)
            .RequireAuthorization(AuthPolicies.AllAuthenticated);

        // 写操作 - Admin 和 Operator
        ruleGroup.MapPost("/", CreateRuleAsync)
            .RequireAuthorization(AuthPolicies.OperatorOrAbove);
        ruleGroup.MapPut("/{ruleId}", UpdateRuleAsync)
            .RequireAuthorization(AuthPolicies.OperatorOrAbove);
        ruleGroup.MapDelete("/{ruleId}", DeleteRuleAsync)
            .RequireAuthorization(AuthPolicies.AdminOnly);

        ruleGroup.MapPut("/{ruleId}/enable", EnableRuleAsync)
            .RequireAuthorization(AuthPolicies.OperatorOrAbove);
        ruleGroup.MapPut("/{ruleId}/disable", DisableRuleAsync)
            .RequireAuthorization(AuthPolicies.OperatorOrAbove);

        // 测试条件表达式
        ruleGroup.MapPost("/test", TestConditionAsync)
            .RequireAuthorization(AuthPolicies.AllAuthenticated);

        // 采集片段端点
        var segmentGroup = app.MapGroup("/api/collection-segments")
            .WithTags("CollectionSegments");

        segmentGroup.MapGet("/", ListSegmentsAsync)
            .RequireAuthorization(AuthPolicies.AllAuthenticated);
        segmentGroup.MapGet("/{id:long}", GetSegmentAsync)
            .RequireAuthorization(AuthPolicies.AllAuthenticated);
        segmentGroup.MapDelete("/{id:long}", DeleteSegmentAsync)
            .RequireAuthorization(AuthPolicies.AdminOnly);
    }

    #region 规则端点

    private static async Task<IResult> ListRulesAsync(
        [FromServices] ICollectionRuleRepository repo,
        [FromQuery] string? deviceId,
        [FromQuery] bool? enabledOnly,
        CancellationToken ct)
    {
        IReadOnlyList<CollectionRule> rules;

        if (!string.IsNullOrEmpty(deviceId))
        {
            rules = await repo.ListByDeviceAsync(deviceId, ct);
        }
        else if (enabledOnly == true)
        {
            rules = await repo.ListEnabledAsync(ct);
        }
        else
        {
            rules = await repo.ListAsync(ct);
        }

        return Results.Ok(new ApiResponse<IReadOnlyList<CollectionRule>> { Success = true, Data = rules });
    }

    private static async Task<IResult> GetRuleAsync(
        [FromServices] ICollectionRuleRepository repo,
        [FromRoute] string ruleId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ruleId))
            return Results.BadRequest(new ApiResponse<CollectionRule> { Success = false, Error = "ruleId 不能为空" });

        var rule = await repo.GetAsync(ruleId, ct);
        if (rule == null)
            return Results.NotFound(new ApiResponse<CollectionRule> { Success = false, Error = "规则不存在" });

        return Results.Ok(new ApiResponse<CollectionRule> { Success = true, Data = rule });
    }

    private static async Task<IResult> CreateRuleAsync(
        [FromServices] ICollectionRuleRepository repo,
        [FromServices] IAuditLogRepository auditRepo,
        [FromServices] IConfigRevisionProvider revisionProvider,
        HttpContext httpContext,
        [FromBody] CreateCollectionRuleRequest request,
        CancellationToken ct)
    {
        var validationError = ValidateCreate(request);
        if (validationError != null)
            return Results.BadRequest(new ApiResponse<CollectionRule> { Success = false, Error = validationError });

        var existed = await repo.GetAsync(request.RuleId, ct);
        if (existed != null)
            return Results.BadRequest(new ApiResponse<CollectionRule> { Success = false, Error = "RuleId 已存在" });

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var rule = new CollectionRule
        {
            RuleId = request.RuleId,
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description,
            DeviceId = request.DeviceId.Trim(),
            Enabled = request.Enabled ?? true,
            StartConditionJson = JsonSerializer.Serialize(request.StartCondition, JsonOptions),
            StopConditionJson = JsonSerializer.Serialize(request.StopCondition, JsonOptions),
            CollectionConfigJson = JsonSerializer.Serialize(request.CollectionConfig, JsonOptions),
            PostActionsJson = request.PostActions != null ? JsonSerializer.Serialize(request.PostActions, JsonOptions) : null,
            TriggerCount = 0,
            LastTriggerUtc = null,
            CreatedUtc = now,
            UpdatedUtc = now
        };

        await repo.UpsertAsync(rule, ct);

        // 递增配置版本号
        await revisionProvider.IncrementRevisionAsync(ct);

        // 审计日志
        await AuditLogHelper.LogAsync(auditRepo, httpContext, "collectionrule.create", "collectionrule",
            request.RuleId, $"Created collection rule: {request.Name}", ct);

        return Results.Ok(new ApiResponse<CollectionRule> { Success = true, Data = rule });
    }

    private static async Task<IResult> UpdateRuleAsync(
        [FromServices] ICollectionRuleRepository repo,
        [FromServices] IAuditLogRepository auditRepo,
        [FromServices] IConfigRevisionProvider revisionProvider,
        HttpContext httpContext,
        [FromRoute] string ruleId,
        [FromBody] UpdateCollectionRuleRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ruleId))
            return Results.BadRequest(new ApiResponse<CollectionRule> { Success = false, Error = "ruleId 不能为空" });

        var validationError = ValidateUpdate(request);
        if (validationError != null)
            return Results.BadRequest(new ApiResponse<CollectionRule> { Success = false, Error = validationError });

        var existed = await repo.GetAsync(ruleId, ct);
        if (existed == null)
            return Results.NotFound(new ApiResponse<CollectionRule> { Success = false, Error = "规则不存在" });

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var updated = existed with
        {
            Name = request.Name?.Trim() ?? existed.Name,
            Description = request.Description ?? existed.Description,
            DeviceId = request.DeviceId?.Trim() ?? existed.DeviceId,
            Enabled = request.Enabled ?? existed.Enabled,
            StartConditionJson = request.StartCondition != null 
                ? JsonSerializer.Serialize(request.StartCondition, JsonOptions) 
                : existed.StartConditionJson,
            StopConditionJson = request.StopCondition != null 
                ? JsonSerializer.Serialize(request.StopCondition, JsonOptions) 
                : existed.StopConditionJson,
            CollectionConfigJson = request.CollectionConfig != null 
                ? JsonSerializer.Serialize(request.CollectionConfig, JsonOptions) 
                : existed.CollectionConfigJson,
            PostActionsJson = request.PostActions != null 
                ? JsonSerializer.Serialize(request.PostActions, JsonOptions) 
                : existed.PostActionsJson,
            UpdatedUtc = now
        };

        await repo.UpsertAsync(updated, ct);

        // 递增配置版本号
        await revisionProvider.IncrementRevisionAsync(ct);

        // 审计日志
        await AuditLogHelper.LogAsync(auditRepo, httpContext, "collectionrule.update", "collectionrule",
            ruleId, $"Updated collection rule: {updated.Name}", ct);

        return Results.Ok(new ApiResponse<CollectionRule> { Success = true, Data = updated });
    }

    private static async Task<IResult> DeleteRuleAsync(
        [FromServices] ICollectionRuleRepository repo,
        [FromServices] IAuditLogRepository auditRepo,
        [FromServices] IConfigRevisionProvider revisionProvider,
        HttpContext httpContext,
        [FromRoute] string ruleId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ruleId))
            return Results.BadRequest(new ApiResponse<object> { Success = false, Error = "ruleId 不能为空" });

        var existed = await repo.GetAsync(ruleId, ct);
        if (existed == null)
            return Results.NotFound(new ApiResponse<object> { Success = false, Error = "规则不存在" });

        await repo.DeleteAsync(ruleId, ct);

        // 递增配置版本号
        await revisionProvider.IncrementRevisionAsync(ct);

        // 审计日志
        await AuditLogHelper.LogAsync(auditRepo, httpContext, "collectionrule.delete", "collectionrule",
            ruleId, $"Deleted collection rule: {existed.Name}", ct);

        return Results.Ok(new ApiResponse<object> { Success = true, Data = null });
    }

    private static async Task<IResult> EnableRuleAsync(
        [FromServices] ICollectionRuleRepository repo,
        [FromServices] IAuditLogRepository auditRepo,
        [FromServices] IConfigRevisionProvider revisionProvider,
        HttpContext httpContext,
        [FromRoute] string ruleId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ruleId))
            return Results.BadRequest(new ApiResponse<object> { Success = false, Error = "ruleId 不能为空" });

        var existed = await repo.GetAsync(ruleId, ct);
        if (existed == null)
            return Results.NotFound(new ApiResponse<object> { Success = false, Error = "规则不存在" });

        await repo.SetEnabledAsync(ruleId, true, ct);

        // 递增配置版本号
        await revisionProvider.IncrementRevisionAsync(ct);

        // 审计日志
        await AuditLogHelper.LogAsync(auditRepo, httpContext, "collectionrule.enable", "collectionrule",
            ruleId, $"Enabled collection rule: {existed.Name}", ct);

        return Results.Ok(new ApiResponse<object> { Success = true, Data = null });
    }

    private static async Task<IResult> DisableRuleAsync(
        [FromServices] ICollectionRuleRepository repo,
        [FromServices] IAuditLogRepository auditRepo,
        [FromServices] IConfigRevisionProvider revisionProvider,
        HttpContext httpContext,
        [FromRoute] string ruleId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ruleId))
            return Results.BadRequest(new ApiResponse<object> { Success = false, Error = "ruleId 不能为空" });

        var existed = await repo.GetAsync(ruleId, ct);
        if (existed == null)
            return Results.NotFound(new ApiResponse<object> { Success = false, Error = "规则不存在" });

        await repo.SetEnabledAsync(ruleId, false, ct);

        // 递增配置版本号
        await revisionProvider.IncrementRevisionAsync(ct);

        // 审计日志
        await AuditLogHelper.LogAsync(auditRepo, httpContext, "collectionrule.disable", "collectionrule",
            ruleId, $"Disabled collection rule: {existed.Name}", ct);

        return Results.Ok(new ApiResponse<object> { Success = true, Data = null });
    }

    private static Task<IResult> TestConditionAsync(
        [FromBody] TestConditionRequest request,
        CancellationToken ct)
    {
        // 验证条件配置格式
        var validationError = ValidateConditionConfig(request.Condition);
        if (validationError != null)
        {
            return Task.FromResult(Results.BadRequest(new ApiResponse<TestConditionResult>
            {
                Success = false,
                Error = validationError
            }));
        }

        // 如果提供了测试数据，进行评估
        bool? result = null;
        if (request.TestData != null && request.TestData.Count > 0)
        {
            result = EvaluateCondition(request.Condition, request.TestData);
        }

        return Task.FromResult(Results.Ok(new ApiResponse<TestConditionResult>
        {
            Success = true,
            Data = new TestConditionResult
            {
                Valid = true,
                Result = result,
                Message = result.HasValue 
                    ? (result.Value ? "条件满足" : "条件不满足") 
                    : "配置有效，未提供测试数据"
            }
        }));
    }

    #endregion

    #region 片段端点

    private static async Task<IResult> ListSegmentsAsync(
        [FromServices] ICollectionSegmentRepository repo,
        [FromQuery] string? ruleId,
        [FromQuery] string? deviceId,
        [FromQuery] int? status,
        [FromQuery] long? startTime,
        [FromQuery] long? endTime,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        var query = new CollectionSegmentQuery
        {
            RuleId = ruleId,
            DeviceId = deviceId,
            Status = status.HasValue ? (SegmentStatus)status.Value : null,
            StartTimeUtc = startTime,
            EndTimeUtc = endTime,
            Limit = Math.Min(limit, 1000)
        };

        var segments = await repo.QueryAsync(query, ct);
        return Results.Ok(new ApiResponse<IReadOnlyList<CollectionSegment>> { Success = true, Data = segments });
    }

    private static async Task<IResult> GetSegmentAsync(
        [FromServices] ICollectionSegmentRepository repo,
        [FromRoute] long id,
        CancellationToken ct)
    {
        var segment = await repo.GetAsync(id, ct);
        if (segment == null)
            return Results.NotFound(new ApiResponse<CollectionSegment> { Success = false, Error = "片段不存在" });

        return Results.Ok(new ApiResponse<CollectionSegment> { Success = true, Data = segment });
    }

    private static async Task<IResult> DeleteSegmentAsync(
        [FromServices] ICollectionSegmentRepository repo,
        [FromServices] IAuditLogRepository auditRepo,
        HttpContext httpContext,
        [FromRoute] long id,
        CancellationToken ct)
    {
        var segment = await repo.GetAsync(id, ct);
        if (segment == null)
            return Results.NotFound(new ApiResponse<object> { Success = false, Error = "片段不存在" });

        await repo.DeleteAsync(id, ct);

        // 审计日志
        await AuditLogHelper.LogAsync(auditRepo, httpContext, "collectionsegment.delete", "collectionsegment",
            id.ToString(), $"Deleted collection segment for rule: {segment.RuleId}", ct);

        return Results.Ok(new ApiResponse<object> { Success = true, Data = null });
    }

    #endregion

    #region 验证方法

    private static string? ValidateCreate(CreateCollectionRuleRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.RuleId)) return "RuleId 不能为空";
        if (string.IsNullOrWhiteSpace(r.Name)) return "Name 不能为空";
        if (string.IsNullOrWhiteSpace(r.DeviceId)) return "DeviceId 不能为空";
        if (r.StartCondition == null) return "StartCondition 不能为空";
        if (r.StopCondition == null) return "StopCondition 不能为空";
        if (r.CollectionConfig == null) return "CollectionConfig 不能为空";

        var startError = ValidateConditionConfig(r.StartCondition);
        if (startError != null) return $"StartCondition: {startError}";

        var stopError = ValidateConditionConfig(r.StopCondition);
        if (stopError != null) return $"StopCondition: {stopError}";

        var configError = ValidateCollectionConfig(r.CollectionConfig);
        if (configError != null) return $"CollectionConfig: {configError}";

        return null;
    }

    private static string? ValidateUpdate(UpdateCollectionRuleRequest r)
    {
        if (r.Name != null && string.IsNullOrWhiteSpace(r.Name))
            return "Name 不能为空字符串";
        if (r.DeviceId != null && string.IsNullOrWhiteSpace(r.DeviceId))
            return "DeviceId 不能为空字符串";

        if (r.StartCondition != null)
        {
            var startError = ValidateConditionConfig(r.StartCondition);
            if (startError != null) return $"StartCondition: {startError}";
        }

        if (r.StopCondition != null)
        {
            var stopError = ValidateConditionConfig(r.StopCondition);
            if (stopError != null) return $"StopCondition: {stopError}";
        }

        if (r.CollectionConfig != null)
        {
            var configError = ValidateCollectionConfig(r.CollectionConfig);
            if (configError != null) return $"CollectionConfig: {configError}";
        }

        return null;
    }

    private static string? ValidateConditionConfig(ConditionConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Logic))
            return "Logic 不能为空";
        if (!AllowedLogics.Contains(config.Logic.Trim()))
            return "Logic 必须是 AND 或 OR";
        if (config.Conditions == null || config.Conditions.Count == 0)
            return "Conditions 不能为空";

        foreach (var cond in config.Conditions)
        {
            if (string.IsNullOrWhiteSpace(cond.Type))
                return "Condition.Type 不能为空";
            if (!AllowedConditionTypes.Contains(cond.Type.Trim()))
                return "Condition.Type 必须是 tag 或 duration";

            if (cond.Type.Equals("tag", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(cond.TagId))
                    return "tag 类型条件的 TagId 不能为空";
                if (string.IsNullOrWhiteSpace(cond.Operator))
                    return "tag 类型条件的 Operator 不能为空";
                if (!AllowedOperators.Contains(cond.Operator.Trim()))
                    return "Operator 必须是 gt/gte/lt/lte/eq/ne";
                if (!cond.Value.HasValue)
                    return "tag 类型条件的 Value 不能为空";
            }
            else if (cond.Type.Equals("duration", StringComparison.OrdinalIgnoreCase))
            {
                if (!cond.Seconds.HasValue || cond.Seconds.Value <= 0)
                    return "duration 类型条件的 Seconds 必须大于 0";
            }
        }

        return null;
    }

    private static string? ValidateCollectionConfig(CollectionConfig config)
    {
        if (config.TagIds == null || config.TagIds.Count == 0)
            return "TagIds 不能为空";
        if (config.PreBufferSeconds < 0)
            return "PreBufferSeconds 不能小于 0";
        if (config.PostBufferSeconds < 0)
            return "PostBufferSeconds 不能小于 0";
        return null;
    }

    private static bool EvaluateCondition(ConditionConfig config, Dictionary<string, double> testData)
    {
        var logic = config.Logic.ToUpperInvariant();
        var results = new List<bool>();

        foreach (var cond in config.Conditions)
        {
            if (cond.Type.Equals("tag", StringComparison.OrdinalIgnoreCase))
            {
                if (!testData.TryGetValue(cond.TagId!, out var value))
                {
                    results.Add(false);
                    continue;
                }

                var threshold = cond.Value!.Value;
                var result = cond.Operator!.ToLowerInvariant() switch
                {
                    "gt" => value > threshold,
                    "gte" => value >= threshold,
                    "lt" => value < threshold,
                    "lte" => value <= threshold,
                    "eq" => Math.Abs(value - threshold) < 0.0001,
                    "ne" => Math.Abs(value - threshold) >= 0.0001,
                    _ => false
                };
                results.Add(result);
            }
            else if (cond.Type.Equals("duration", StringComparison.OrdinalIgnoreCase))
            {
                // Duration 条件在测试中总是返回 true（需要实时评估）
                results.Add(true);
            }
        }

        return logic == "AND" 
            ? results.TrueForAll(r => r) 
            : results.Exists(r => r);
    }

    #endregion

    #region Request/Response Models

    public sealed record CreateCollectionRuleRequest
    {
        public required string RuleId { get; init; }
        public required string Name { get; init; }
        public string? Description { get; init; }
        public required string DeviceId { get; init; }
        public bool? Enabled { get; init; }
        public required ConditionConfig StartCondition { get; init; }
        public required ConditionConfig StopCondition { get; init; }
        public required CollectionConfig CollectionConfig { get; init; }
        public List<PostAction>? PostActions { get; init; }
    }

    public sealed record UpdateCollectionRuleRequest
    {
        public string? Name { get; init; }
        public string? Description { get; init; }
        public string? DeviceId { get; init; }
        public bool? Enabled { get; init; }
        public ConditionConfig? StartCondition { get; init; }
        public ConditionConfig? StopCondition { get; init; }
        public CollectionConfig? CollectionConfig { get; init; }
        public List<PostAction>? PostActions { get; init; }
    }

    public sealed record TestConditionRequest
    {
        public required ConditionConfig Condition { get; init; }
        public Dictionary<string, double>? TestData { get; init; }
    }

    public sealed record TestConditionResult
    {
        public bool Valid { get; init; }
        public bool? Result { get; init; }
        public string? Message { get; init; }
    }

    #endregion
}
