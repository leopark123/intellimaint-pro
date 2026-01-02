using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using IntelliMaint.Host.Api.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace IntelliMaint.Host.Api.Endpoints;

public static class AuditLogEndpoints
{
    public static void MapAuditLogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/audit-logs")
            .WithTags("AuditLog")
            .RequireAuthorization(AuthPolicies.OperatorOrAbove);  // v38: Operator 及以上可查看审计日志

        group.MapGet("/", QueryAsync);
        group.MapGet("/actions", GetActionsAsync);
        group.MapGet("/resource-types", GetResourceTypesAsync);
    }

    private static async Task<IResult> QueryAsync(
        [FromServices] IAuditLogRepository repo,
        [FromQuery] string? action,
        [FromQuery] string? resourceType,
        [FromQuery] string? resourceId,
        [FromQuery] string? userId,
        [FromQuery] long? startTs,
        [FromQuery] long? endTs,
        [FromQuery] int? limit,
        [FromQuery] int? offset,
        CancellationToken ct)
    {
        var q = new AuditLogQuery
        {
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            UserId = userId,
            StartTs = startTs,
            EndTs = endTs,
            Limit = limit ?? 50,
            Offset = offset ?? 0
        };

        var (items, totalCount) = await repo.QueryAsync(q, ct);

        var result = new PagedAuditLogResult
        {
            Items = items,
            TotalCount = totalCount,
            Limit = Math.Min(Math.Max(q.Limit, 1), 200),
            Offset = Math.Max(q.Offset, 0)
        };

        return Results.Ok(new ApiResponse<PagedAuditLogResult> { Success = true, Data = result });
    }

    private static async Task<IResult> GetActionsAsync(
        [FromServices] IAuditLogRepository repo,
        CancellationToken ct)
    {
        var actions = await repo.GetDistinctActionsAsync(ct);
        return Results.Ok(new ApiResponse<IReadOnlyList<string>> { Success = true, Data = actions });
    }

    private static async Task<IResult> GetResourceTypesAsync(
        [FromServices] IAuditLogRepository repo,
        CancellationToken ct)
    {
        var types = await repo.GetDistinctResourceTypesAsync(ct);
        return Results.Ok(new ApiResponse<IReadOnlyList<string>> { Success = true, Data = types });
    }

    public sealed record PagedAuditLogResult
    {
        public IReadOnlyList<AuditLogEntry> Items { get; init; } = Array.Empty<AuditLogEntry>();
        public int TotalCount { get; init; }
        public int Limit { get; init; }
        public int Offset { get; init; }
    }
}

public static class AuditLogHelper
{
    public static async Task LogAsync(
        IAuditLogRepository repo,
        HttpContext httpContext,
        string action,
        string resourceType,
        string? resourceId,
        string? details,
        CancellationToken ct)
    {
        // 从 JWT Claims 提取真实用户信息
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
        var userName = httpContext.User.FindFirstValue(ClaimTypes.Name) ?? "Anonymous";
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();

        var entry = new AuditLogEntry
        {
            Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UserId = userId,
            UserName = userName,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Details = details,
            IpAddress = ipAddress
        };

        await repo.CreateAsync(entry, ct);
    }
}
