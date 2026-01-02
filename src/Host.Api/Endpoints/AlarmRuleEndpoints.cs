using System;
using System.Collections.Generic;
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

public static class AlarmRuleEndpoints
{
    private static readonly HashSet<string> AllowedConditions = new(StringComparer.OrdinalIgnoreCase)
    {
        "gt", "gte", "lt", "lte", "eq", "ne"
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
        CancellationToken ct)
    {
        var rules = await repo.ListAsync(ct);
        return Results.Ok(new ApiResponse<IReadOnlyList<AlarmRule>> { Success = true, Data = rules });
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

        var rule = new AlarmRule
        {
            RuleId = request.RuleId,
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description,
            TagId = request.TagId.Trim(),
            DeviceId = string.IsNullOrWhiteSpace(request.DeviceId) ? null : request.DeviceId,
            ConditionType = request.ConditionType.Trim().ToLowerInvariant(),
            Threshold = request.Threshold,
            DurationMs = request.DurationMs ?? 0,
            Severity = request.Severity ?? 3,
            MessageTemplate = string.IsNullOrWhiteSpace(request.MessageTemplate) ? null : request.MessageTemplate,
            Enabled = request.Enabled ?? true,
            CreatedUtc = now,
            UpdatedUtc = now
        };

        await repo.UpsertAsync(rule, ct);
        
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

        var updated = existed with
        {
            Name = request.Name != null ? request.Name.Trim() : existed.Name,
            Description = request.Description != null ? request.Description : existed.Description,
            TagId = request.TagId != null ? request.TagId.Trim() : existed.TagId,
            DeviceId = request.DeviceId != null ? (string.IsNullOrWhiteSpace(request.DeviceId) ? null : request.DeviceId) : existed.DeviceId,
            ConditionType = request.ConditionType != null ? request.ConditionType.Trim().ToLowerInvariant() : existed.ConditionType,
            Threshold = request.Threshold.HasValue ? request.Threshold.Value : existed.Threshold,
            DurationMs = request.DurationMs.HasValue ? request.DurationMs.Value : existed.DurationMs,
            Severity = request.Severity.HasValue ? request.Severity.Value : existed.Severity,
            MessageTemplate = request.MessageTemplate != null ? request.MessageTemplate : existed.MessageTemplate,
            Enabled = request.Enabled.HasValue ? request.Enabled.Value : existed.Enabled,
            UpdatedUtc = now
        };

        await repo.UpsertAsync(updated, ct);
        
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
        await AuditLogHelper.LogAsync(auditRepo, httpContext, "alarmrule.delete", "alarmrule",
            ruleId, $"Deleted alarm rule: {existed.Name}", ct);

        Log.Information("Deleted alarm rule {RuleId}", ruleId);
        return Results.Ok(new ApiResponse<object> { Success = true, Data = null });
    }

    private static async Task<IResult> EnableAsync(
        [FromServices] IAlarmRuleRepository repo,
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
        await AuditLogHelper.LogAsync(auditRepo, httpContext, "alarmrule.enable", "alarmrule",
            ruleId, $"Enabled alarm rule: {existed.Name}", ct);
        
        return Results.Ok(new ApiResponse<object> { Success = true, Data = null });
    }

    private static async Task<IResult> DisableAsync(
        [FromServices] IAlarmRuleRepository repo,
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
        await AuditLogHelper.LogAsync(auditRepo, httpContext, "alarmrule.disable", "alarmrule",
            ruleId, $"Disabled alarm rule: {existed.Name}", ct);
        
        return Results.Ok(new ApiResponse<object> { Success = true, Data = null });
    }

    private static string? ValidateCreate(CreateAlarmRuleRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.RuleId)) return "RuleId 不能为空";
        if (string.IsNullOrWhiteSpace(r.Name)) return "Name 不能为空";
        if (string.IsNullOrWhiteSpace(r.TagId)) return "TagId 不能为空";
        if (string.IsNullOrWhiteSpace(r.ConditionType)) return "ConditionType 不能为空";
        if (!AllowedConditions.Contains(r.ConditionType.Trim())) return "ConditionType 非法（gt/gte/lt/lte/eq/ne）";
        if (r.Severity.HasValue && (r.Severity.Value < 1 || r.Severity.Value > 5)) return "Severity 必须在 1-5";
        if (r.DurationMs.HasValue && r.DurationMs.Value < 0) return "DurationMs 不能小于 0";
        return null;
    }

    private static string? ValidateUpdate(UpdateAlarmRuleRequest r)
    {
        if (r.ConditionType != null && !AllowedConditions.Contains(r.ConditionType.Trim()))
            return "ConditionType 非法（gt/gte/lt/lte/eq/ne）";
        if (r.Severity.HasValue && (r.Severity.Value < 1 || r.Severity.Value > 5))
            return "Severity 必须在 1-5";
        if (r.DurationMs.HasValue && r.DurationMs.Value < 0)
            return "DurationMs 不能小于 0";
        if (r.Name != null && string.IsNullOrWhiteSpace(r.Name))
            return "Name 不能为空字符串";
        if (r.TagId != null && string.IsNullOrWhiteSpace(r.TagId))
            return "TagId 不能为空字符串";
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
        public required string ConditionType { get; init; } // gt/gte/lt/lte/eq/ne
        public required double Threshold { get; init; }
        public int? DurationMs { get; init; }
        public int? Severity { get; init; }                 // 1-5
        public string? MessageTemplate { get; init; }
        public bool? Enabled { get; init; }
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
    }
}
