using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using IntelliMaint.Host.Api.Models;
using IntelliMaint.Host.Api.Services;
using IntelliMaint.Host.Api.Validators;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace IntelliMaint.Host.Api.Endpoints;

public static class AlarmRuleEndpoints
{
    private static readonly HashSet<string> AllowedConditions = new(StringComparer.OrdinalIgnoreCase)
    {
        // 阈值告警
        "gt", "gte", "lt", "lte", "eq", "ne",
        // v56: 离线检测
        "offline",
        // v56: 变化率告警
        "roc_percent", "roc_absolute",
        // v58: 波动告警
        "volatility"
    };

    public static void MapAlarmRuleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/alarm-rules")
            .WithTags("AlarmRules");

        // 读操作 - 所有已认证用户
        group.MapGet("/", ListAsync)
            .RequireAuthorization(AuthPolicies.AllAuthenticated);
        group.MapGet("/{ruleId}", GetAsync)
            .RequireAuthorization(AuthPolicies.AllAuthenticated);

        // 写操作 - 仅 Admin
        group.MapPost("/", CreateAsync)
            .RequireAuthorization(AuthPolicies.AdminOnly);
        group.MapPut("/{ruleId}", UpdateAsync)
            .RequireAuthorization(AuthPolicies.AdminOnly);
        group.MapDelete("/{ruleId}", DeleteAsync)
            .RequireAuthorization(AuthPolicies.AdminOnly);

        group.MapPut("/{ruleId}/enable", EnableAsync)
            .RequireAuthorization(AuthPolicies.AdminOnly);
        group.MapPut("/{ruleId}/disable", DisableAsync)
            .RequireAuthorization(AuthPolicies.AdminOnly);
    }

    private static async Task<IResult> ListAsync(
        [FromServices] IAlarmRuleRepository repo,
        [FromServices] CacheService cache,
        CancellationToken ct)
    {
        // v56: 使用缓存（2分钟过期）
        var rules = await cache.GetOrCreateAsync(
            CacheService.Keys.AlarmRuleList,
            () => repo.ListAsync(ct),
            TimeSpan.FromMinutes(2));

        return Results.Ok(new ApiResponse<IReadOnlyList<AlarmRule>> { Success = true, Data = rules ?? Array.Empty<AlarmRule>() });
    }

    private static async Task<IResult> GetAsync(
        [FromServices] IAlarmRuleRepository repo,
        [FromRoute] string ruleId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ruleId))
            return Results.BadRequest(new ApiResponse<AlarmRule> { Success = false, Error = "ruleId 不能为空" });

        var rule = await repo.GetAsync(ruleId, ct);
        if (rule == null)
            return Results.NotFound(new ApiResponse<AlarmRule> { Success = false, Error = "规则不存在" });

        return Results.Ok(new ApiResponse<AlarmRule> { Success = true, Data = rule });
    }

    private static async Task<IResult> CreateAsync(
        [FromServices] IAlarmRuleRepository repo,
        [FromServices] IAuditLogRepository auditRepo,
        [FromServices] IConfigRevisionProvider revisionProvider,
        [FromServices] CacheService cache,
        HttpContext httpContext,
        [FromBody] CreateAlarmRuleRequest request,
        CancellationToken ct)
    {
        var validationError = ValidateCreate(request);
        if (validationError != null)
            return Results.BadRequest(new ApiResponse<AlarmRule> { Success = false, Error = validationError });

        var existed = await repo.GetAsync(request.RuleId, ct);
        if (existed != null)
            return Results.BadRequest(new ApiResponse<AlarmRule> { Success = false, Error = "RuleId 已存在" });

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var condType = request.ConditionType.Trim().ToLowerInvariant();

        // v56/v58: 根据条件类型确定规则类型
        var ruleType = condType switch
        {
            "offline" => "offline",
            "roc_percent" or "roc_absolute" => "roc",
            "volatility" => "volatility",
            _ => "threshold"
        };

        var rule = new AlarmRule
        {
            RuleId = request.RuleId,
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description,
            TagId = request.TagId.Trim(),
            DeviceId = string.IsNullOrWhiteSpace(request.DeviceId) ? null : request.DeviceId,
            ConditionType = condType,
            Threshold = request.Threshold,
            DurationMs = request.DurationMs ?? 0,
            Severity = request.Severity ?? 3,
            MessageTemplate = string.IsNullOrWhiteSpace(request.MessageTemplate) ? null : request.MessageTemplate,
            Enabled = request.Enabled ?? true,
            CreatedUtc = now,
            UpdatedUtc = now,
            // v56: 新增字段
            RocWindowMs = request.RocWindowMs ?? 0,
            RuleType = ruleType
        };

        await repo.UpsertAsync(rule, ct);

        // v56: 使缓存失效
        cache.InvalidateAlarmRules();

        // 递增配置版本号（规则变更也需要通知）
        await revisionProvider.IncrementRevisionAsync(ct);

        // 审计日志
        await AuditLogHelper.LogAsync(auditRepo, httpContext, "alarmrule.create", "alarmrule",
            rule.RuleId, $"Created alarm rule: {rule.Name}", ct);

        Log.Information("Created alarm rule {RuleId}", rule.RuleId);
        return Results.Ok(new ApiResponse<AlarmRule> { Success = true, Data = rule });
    }

    private static async Task<IResult> UpdateAsync(
        [FromServices] IAlarmRuleRepository repo,
        [FromServices] IAuditLogRepository auditRepo,
        [FromServices] IConfigRevisionProvider revisionProvider,
        [FromServices] CacheService cache,
        HttpContext httpContext,
        [FromRoute] string ruleId,
        [FromBody] UpdateAlarmRuleRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ruleId))
            return Results.BadRequest(new ApiResponse<AlarmRule> { Success = false, Error = "ruleId 不能为空" });

        var existed = await repo.GetAsync(ruleId, ct);
        if (existed == null)
            return Results.NotFound(new ApiResponse<AlarmRule> { Success = false, Error = "规则不存在" });

        var validationError = ValidateUpdate(request);
        if (validationError != null)
            return Results.BadRequest(new ApiResponse<AlarmRule> { Success = false, Error = validationError });

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // v56/v58: 如果更新条件类型，重新计算规则类型
        var newCondType = request.ConditionType != null
            ? request.ConditionType.Trim().ToLowerInvariant()
            : existed.ConditionType;

        var newRuleType = newCondType switch
        {
            "offline" => "offline",
            "roc_percent" or "roc_absolute" => "roc",
            "volatility" => "volatility",
            _ => "threshold"
        };

        var updated = existed with
        {
            Name = request.Name != null ? request.Name.Trim() : existed.Name,
            Description = request.Description != null ? request.Description : existed.Description,
            TagId = request.TagId != null ? request.TagId.Trim() : existed.TagId,
            DeviceId = request.DeviceId != null ? (string.IsNullOrWhiteSpace(request.DeviceId) ? null : request.DeviceId) : existed.DeviceId,
            ConditionType = newCondType,
            Threshold = request.Threshold.HasValue ? request.Threshold.Value : existed.Threshold,
            DurationMs = request.DurationMs.HasValue ? request.DurationMs.Value : existed.DurationMs,
            Severity = request.Severity.HasValue ? request.Severity.Value : existed.Severity,
            MessageTemplate = request.MessageTemplate != null ? request.MessageTemplate : existed.MessageTemplate,
            Enabled = request.Enabled.HasValue ? request.Enabled.Value : existed.Enabled,
            UpdatedUtc = now,
            // v56: 新增字段
            RocWindowMs = request.RocWindowMs.HasValue ? request.RocWindowMs.Value : existed.RocWindowMs,
            RuleType = newRuleType
        };

        await repo.UpsertAsync(updated, ct);

        // v56: 使缓存失效
        cache.InvalidateAlarmRules();

        // 递增配置版本号
        await revisionProvider.IncrementRevisionAsync(ct);

        // 审计日志
        await AuditLogHelper.LogAsync(auditRepo, httpContext, "alarmrule.update", "alarmrule",
            ruleId, $"Updated alarm rule: {updated.Name}", ct);

        Log.Information("Updated alarm rule {RuleId}", ruleId);
        return Results.Ok(new ApiResponse<AlarmRule> { Success = true, Data = updated });
    }

    private static async Task<IResult> DeleteAsync(
        [FromServices] IAlarmRuleRepository repo,
        [FromServices] IAuditLogRepository auditRepo,
        [FromServices] IConfigRevisionProvider revisionProvider,
        [FromServices] CacheService cache,
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

        // v56: 使缓存失效
        cache.InvalidateAlarmRules();

        // 递增配置版本号
        await revisionProvider.IncrementRevisionAsync(ct);

        // 审计日志
        await AuditLogHelper.LogAsync(auditRepo, httpContext, "alarmrule.delete", "alarmrule",
            ruleId, $"Deleted alarm rule: {existed.Name}", ct);

        Log.Information("Deleted alarm rule {RuleId}", ruleId);
        return Results.Ok(new ApiResponse<object> { Success = true, Data = null });
    }

    private static async Task<IResult> EnableAsync(
        [FromServices] IAlarmRuleRepository repo,
        [FromServices] IAuditLogRepository auditRepo,
        [FromServices] IConfigRevisionProvider revisionProvider,
        [FromServices] CacheService cache,
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

        // v56: 使缓存失效
        cache.InvalidateAlarmRules();

        // 递增配置版本号
        await revisionProvider.IncrementRevisionAsync(ct);

        // 审计日志
        await AuditLogHelper.LogAsync(auditRepo, httpContext, "alarmrule.enable", "alarmrule",
            ruleId, $"Enabled alarm rule: {existed.Name}", ct);
        
        return Results.Ok(new ApiResponse<object> { Success = true, Data = null });
    }

    private static async Task<IResult> DisableAsync(
        [FromServices] IAlarmRuleRepository repo,
        [FromServices] IAuditLogRepository auditRepo,
        [FromServices] IConfigRevisionProvider revisionProvider,
        [FromServices] CacheService cache,
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

        // v56: 使缓存失效
        cache.InvalidateAlarmRules();

        // 递增配置版本号
        await revisionProvider.IncrementRevisionAsync(ct);

        // 审计日志
        await AuditLogHelper.LogAsync(auditRepo, httpContext, "alarmrule.disable", "alarmrule",
            ruleId, $"Disabled alarm rule: {existed.Name}", ct);
        
        return Results.Ok(new ApiResponse<object> { Success = true, Data = null });
    }

    private static string? ValidateCreate(CreateAlarmRuleRequest r)
    {
        // P1: 使用 InputValidator 进行安全验证
        var ruleIdValidation = InputValidator.ValidateIdentifier(r.RuleId, "RuleId");
        if (!ruleIdValidation.IsValid) return ruleIdValidation.Error;

        var nameValidation = InputValidator.ValidateDisplayName(r.Name, "Name");
        if (!nameValidation.IsValid) return nameValidation.Error;

        var tagIdValidation = InputValidator.ValidateIdentifier(r.TagId, "TagId");
        if (!tagIdValidation.IsValid) return tagIdValidation.Error;

        var descValidation = InputValidator.ValidateDescription(r.Description);
        if (!descValidation.IsValid) return descValidation.Error;

        if (string.IsNullOrWhiteSpace(r.ConditionType)) return "ConditionType 不能为空";
        if (!AllowedConditions.Contains(r.ConditionType.Trim()))
            return "ConditionType 非法（gt/gte/lt/lte/eq/ne/offline/roc_percent/roc_absolute/volatility）";
        if (r.Severity.HasValue && (r.Severity.Value < 1 || r.Severity.Value > 5)) return "Severity 必须在 1-5";
        if (r.DurationMs.HasValue && r.DurationMs.Value < 0) return "DurationMs 不能小于 0";

        // v56: 变化率规则验证
        var condType = r.ConditionType.Trim().ToLowerInvariant();
        if (condType.StartsWith("roc_"))
        {
            if (!r.RocWindowMs.HasValue || r.RocWindowMs.Value <= 0)
                return "变化率规则必须指定 RocWindowMs（时间窗口毫秒数）";
            if (r.RocWindowMs.Value > 3600000)
                return "RocWindowMs 不能超过 3600000（1小时）";
        }

        // v58: 波动告警规则验证
        if (condType == "volatility")
        {
            if (!r.RocWindowMs.HasValue || r.RocWindowMs.Value <= 0)
                return "波动告警规则必须指定 RocWindowMs（时间窗口毫秒数）";
            if (r.RocWindowMs.Value > 3600000)
                return "RocWindowMs 不能超过 3600000（1小时）";
            if (r.Threshold <= 0)
                return "波动告警阈值必须为正数（标准差阈值）";
        }

        // v56: 离线检测规则验证
        if (condType == "offline")
        {
            if (r.Threshold <= 0)
                return "离线检测阈值必须为正数（超时秒数）";
        }

        return null;
    }

    private static string? ValidateUpdate(UpdateAlarmRuleRequest r)
    {
        // P1: 使用 InputValidator 进行安全验证
        if (r.Name != null)
        {
            var nameValidation = InputValidator.ValidateDisplayName(r.Name, "Name");
            if (!nameValidation.IsValid) return nameValidation.Error;
        }

        if (r.TagId != null)
        {
            var tagIdValidation = InputValidator.ValidateIdentifier(r.TagId, "TagId");
            if (!tagIdValidation.IsValid) return tagIdValidation.Error;
        }

        var descValidation = InputValidator.ValidateDescription(r.Description);
        if (!descValidation.IsValid) return descValidation.Error;

        if (r.ConditionType != null && !AllowedConditions.Contains(r.ConditionType.Trim()))
            return "ConditionType 非法（gt/gte/lt/lte/eq/ne/offline/roc_percent/roc_absolute/volatility）";
        if (r.Severity.HasValue && (r.Severity.Value < 1 || r.Severity.Value > 5))
            return "Severity 必须在 1-5";
        if (r.DurationMs.HasValue && r.DurationMs.Value < 0)
            return "DurationMs 不能小于 0";

        // v56/v58: 变化率和波动规则验证
        if (r.ConditionType != null)
        {
            var condType = r.ConditionType.Trim().ToLowerInvariant();
            if (condType.StartsWith("roc_"))
            {
                if (r.RocWindowMs.HasValue && r.RocWindowMs.Value <= 0)
                    return "RocWindowMs 必须为正数";
                if (r.RocWindowMs.HasValue && r.RocWindowMs.Value > 3600000)
                    return "RocWindowMs 不能超过 3600000（1小时）";
            }
            if (condType == "volatility")
            {
                if (r.RocWindowMs.HasValue && r.RocWindowMs.Value <= 0)
                    return "RocWindowMs 必须为正数";
                if (r.RocWindowMs.HasValue && r.RocWindowMs.Value > 3600000)
                    return "RocWindowMs 不能超过 3600000（1小时）";
                if (r.Threshold.HasValue && r.Threshold.Value <= 0)
                    return "波动告警阈值必须为正数（标准差阈值）";
            }
            if (condType == "offline")
            {
                if (r.Threshold.HasValue && r.Threshold.Value <= 0)
                    return "离线检测阈值必须为正数（超时秒数）";
            }
        }

        return null;
    }

    // Request models
    public sealed record CreateAlarmRuleRequest
    {
        public required string RuleId { get; init; }
        public required string Name { get; init; }
        public string? Description { get; init; }
        public required string TagId { get; init; }
        public string? DeviceId { get; init; }
        public required string ConditionType { get; init; } // gt/gte/lt/lte/eq/ne/offline/roc_percent/roc_absolute
        public required double Threshold { get; init; }
        public int? DurationMs { get; init; }
        public int? Severity { get; init; }                 // 1-5
        public string? MessageTemplate { get; init; }
        public bool? Enabled { get; init; }

        // v56: 变化率告警用
        public int? RocWindowMs { get; init; }              // 时间窗口（毫秒）
    }

    public sealed record UpdateAlarmRuleRequest
    {
        public string? Name { get; init; }
        public string? Description { get; init; }
        public string? TagId { get; init; }
        public string? DeviceId { get; init; }
        public string? ConditionType { get; init; }
        public double? Threshold { get; init; }
        public int? DurationMs { get; init; }
        public int? Severity { get; init; }
        public string? MessageTemplate { get; init; }
        public bool? Enabled { get; init; }

        // v56: 变化率告警用
        public int? RocWindowMs { get; init; }              // 时间窗口（毫秒）
    }
}
