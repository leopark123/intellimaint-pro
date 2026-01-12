using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using IntelliMaint.Host.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace IntelliMaint.Host.Api.Endpoints;

public static class AlarmEndpoints
{
    public static void MapAlarmEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/alarms")
            .WithTags("Alarms");

        // 读操作 - 所有已认证用户
        group.MapGet("", QueryAsync)
            .WithName("QueryAlarms")
            .WithSummary("查询告警列表")
            .RequireAuthorization(AuthPolicies.AllAuthenticated);

        group.MapGet("/stats", GetStatsAsync)
            .WithName("GetAlarmStats")
            .WithSummary("获取告警统计")
            .RequireAuthorization(AuthPolicies.AllAuthenticated);

        group.MapGet("/trend", GetTrendAsync)
            .WithName("GetAlarmTrend")
            .WithSummary("获取告警趋势（按时间桶聚合）")
            .RequireAuthorization(AuthPolicies.AllAuthenticated);

        group.MapGet("/{alarmId}", GetAsync)
            .WithName("GetAlarm")
            .WithSummary("获取单个告警")
            .RequireAuthorization(AuthPolicies.AllAuthenticated);

        // 业务操作 - Operator 及以上
        group.MapPost("", CreateAsync)
            .WithName("CreateAlarm")
            .WithSummary("创建告警（测试用）")
            .RequireAuthorization(AuthPolicies.OperatorOrAbove);

        group.MapPost("/{alarmId}/ack", AckAsync)
            .WithName("AckAlarm")
            .WithSummary("确认告警")
            .RequireAuthorization(AuthPolicies.OperatorOrAbove);

        group.MapPost("/{alarmId}/close", CloseAsync)
            .WithName("CloseAlarm")
            .WithSummary("关闭告警")
            .RequireAuthorization(AuthPolicies.OperatorOrAbove);

        // ========== 告警聚合组 API ==========
        group.MapGet("/aggregated", QueryGroupsAsync)
            .WithName("QueryAlarmGroups")
            .WithSummary("查询告警聚合组列表")
            .RequireAuthorization(AuthPolicies.AllAuthenticated);

        group.MapGet("/aggregated/stats", GetGroupStatsAsync)
            .WithName("GetAlarmGroupStats")
            .WithSummary("获取告警聚合组统计")
            .RequireAuthorization(AuthPolicies.AllAuthenticated);

        group.MapGet("/groups/{groupId}", GetGroupDetailAsync)
            .WithName("GetAlarmGroupDetail")
            .WithSummary("获取告警聚合组详情（含子告警）")
            .RequireAuthorization(AuthPolicies.AllAuthenticated);

        group.MapPost("/groups/{groupId}/ack", AckGroupAsync)
            .WithName("AckAlarmGroup")
            .WithSummary("确认告警聚合组")
            .RequireAuthorization(AuthPolicies.OperatorOrAbove);

        group.MapPost("/groups/{groupId}/close", CloseGroupAsync)
            .WithName("CloseAlarmGroup")
            .WithSummary("关闭告警聚合组")
            .RequireAuthorization(AuthPolicies.OperatorOrAbove);
    }

    private static async Task<IResult> QueryAsync(
        [FromServices] IAlarmRepository repo,
        [FromQuery] string? deviceId,
        [FromQuery] int? status,
        [FromQuery] int? minSeverity,
        [FromQuery] long? startTs,
        [FromQuery] long? endTs,
        [FromQuery] int? limit,
        [FromQuery] string? after,
        CancellationToken ct)
    {
        AlarmStatus? parsedStatus = null;
        if (status.HasValue)
        {
            if (!Enum.IsDefined(typeof(AlarmStatus), status.Value))
            {
                return Results.BadRequest(new ApiResponse<string>
                {
                    Success = false,
                    Data = null,
                    Error = "status 参数非法（0=Open, 1=Acknowledged, 2=Closed）"
                });
            }
            parsedStatus = (AlarmStatus)status.Value;
        }

        if (minSeverity.HasValue && (minSeverity.Value < 1 || minSeverity.Value > 4))
        {
            return Results.BadRequest(new ApiResponse<string>
            {
                Success = false,
                Data = null,
                Error = "minSeverity 参数非法（范围 1-4）"
            });
        }

        var token = PageToken.Parse(after);

        var query = new AlarmQuery
        {
            DeviceId = deviceId,
            Status = parsedStatus,
            MinSeverity = minSeverity,
            StartTs = startTs,
            EndTs = endTs,
            Limit = limit ?? 100,
            After = token
        };

        var result = await repo.QueryAsync(query, ct);

        return Results.Ok(new ApiResponse<PagedResult<AlarmRecord>>
        {
            Success = true,
            Data = result,
            Error = null
        });
    }

    private static async Task<IResult> GetAsync(
        [FromServices] IAlarmRepository repo,
        [FromRoute] string alarmId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(alarmId))
        {
            return Results.BadRequest(new ApiResponse<string>
            {
                Success = false,
                Data = null,
                Error = "alarmId 不能为空"
            });
        }

        var alarm = await repo.GetAsync(alarmId, ct);
        if (alarm is null)
        {
            return Results.NotFound(new ApiResponse<string>
            {
                Success = false,
                Data = null,
                Error = "告警不存在"
            });
        }

        return Results.Ok(new ApiResponse<AlarmRecord>
        {
            Success = true,
            Data = alarm,
            Error = null
        });
    }

    private static async Task<IResult> CreateAsync(
        [FromServices] IAlarmRepository repo,
        [FromServices] IAuditLogRepository auditRepo,
        HttpContext httpContext,
        [FromBody] CreateAlarmRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return Results.BadRequest(new ApiResponse<string> { Success = false, Error = "请求体不能为空" });

        if (string.IsNullOrWhiteSpace(request.DeviceId))
            return Results.BadRequest(new ApiResponse<string> { Success = false, Error = "DeviceId 不能为空" });

        if (request.Severity < 1 || request.Severity > 4)
            return Results.BadRequest(new ApiResponse<string> { Success = false, Error = "Severity 范围必须是 1-4" });

        if (string.IsNullOrWhiteSpace(request.Code))
            return Results.BadRequest(new ApiResponse<string> { Success = false, Error = "Code 不能为空" });

        if (string.IsNullOrWhiteSpace(request.Message))
            return Results.BadRequest(new ApiResponse<string> { Success = false, Error = "Message 不能为空" });

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var alarm = new AlarmRecord
        {
            AlarmId = Guid.NewGuid().ToString("N"),
            DeviceId = request.DeviceId,
            TagId = string.IsNullOrWhiteSpace(request.TagId) ? null : request.TagId,
            Ts = now,
            Severity = request.Severity,
            Code = request.Code,
            Message = request.Message,
            Status = AlarmStatus.Open,
            CreatedUtc = now,
            UpdatedUtc = now,
            AckedBy = null,
            AckedUtc = null,
            AckNote = null
        };

        await repo.CreateAsync(alarm, ct);

        // 审计日志
        await AuditLogHelper.LogAsync(auditRepo, httpContext, "alarm.create", "alarm",
            alarm.AlarmId, $"Created alarm: {request.Code} - {request.Message}", ct);

        return Results.Ok(new ApiResponse<AlarmRecord>
        {
            Success = true,
            Data = alarm,
            Error = null
        });
    }

    private static async Task<IResult> AckAsync(
        [FromServices] IAlarmRepository repo,
        [FromServices] IAuditLogRepository auditRepo,
        HttpContext httpContext,
        [FromRoute] string alarmId,
        [FromBody] AckAlarmRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(alarmId))
        {
            return Results.BadRequest(new ApiResponse<string>
            {
                Success = false,
                Data = null,
                Error = "alarmId 不能为空"
            });
        }

        if (request is null)
        {
            return Results.BadRequest(new ApiResponse<string>
            {
                Success = false,
                Data = null,
                Error = "请求体不能为空"
            });
        }

        if (string.IsNullOrWhiteSpace(request.AckedBy))
        {
            return Results.BadRequest(new ApiResponse<string>
            {
                Success = false,
                Data = null,
                Error = "AckedBy 不能为空"
            });
        }

        var existing = await repo.GetAsync(alarmId, ct);
        if (existing is null)
        {
            return Results.NotFound(new ApiResponse<string>
            {
                Success = false,
                Data = null,
                Error = "告警不存在"
            });
        }

        if (existing.Status == AlarmStatus.Closed)
        {
            return Results.BadRequest(new ApiResponse<string>
            {
                Success = false,
                Data = null,
                Error = "已关闭告警不允许确认"
            });
        }

        await repo.AckAsync(new AlarmAckRequest
        {
            AlarmId = alarmId,
            AckedBy = request.AckedBy,
            AckNote = request.AckNote
        }, ct);

        var updated = await repo.GetAsync(alarmId, ct);
        Log.Information("Alarm ack endpoint: {AlarmId} by={AckedBy}", alarmId, request.AckedBy);

        // 审计日志
        await AuditLogHelper.LogAsync(auditRepo, httpContext, "alarm.ack", "alarm",
            alarmId, $"Alarm acknowledged. Note: {request.AckNote ?? "无"}", ct);

        return Results.Ok(new ApiResponse<AlarmRecord>
        {
            Success = true,
            Data = updated!,
            Error = null
        });
    }

    private static async Task<IResult> CloseAsync(
        [FromServices] IAlarmRepository repo,
        [FromServices] IAuditLogRepository auditRepo,
        HttpContext httpContext,
        [FromRoute] string alarmId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(alarmId))
        {
            return Results.BadRequest(new ApiResponse<string>
            {
                Success = false,
                Data = null,
                Error = "alarmId 不能为空"
            });
        }

        var existing = await repo.GetAsync(alarmId, ct);
        if (existing is null)
        {
            return Results.NotFound(new ApiResponse<string>
            {
                Success = false,
                Data = null,
                Error = "告警不存在"
            });
        }

        if (existing.Status == AlarmStatus.Closed)
        {
            return Results.Ok(new ApiResponse<AlarmRecord>
            {
                Success = true,
                Data = existing,
                Error = null
            });
        }

        await repo.CloseAsync(alarmId, ct);

        var updated = await repo.GetAsync(alarmId, ct);
        Log.Information("Alarm close endpoint: {AlarmId}", alarmId);

        // 审计日志
        await AuditLogHelper.LogAsync(auditRepo, httpContext, "alarm.close", "alarm",
            alarmId, "Alarm closed", ct);

        return Results.Ok(new ApiResponse<AlarmRecord>
        {
            Success = true,
            Data = updated!,
            Error = null
        });
    }

    private static async Task<IResult> GetStatsAsync(
        [FromServices] IAlarmRepository repo,
        [FromQuery] string? deviceId,
        CancellationToken ct)
    {
        var openCount = await repo.GetOpenCountAsync(deviceId, ct);

        return Results.Ok(new ApiResponse<AlarmStatsResponse>
        {
            Success = true,
            Data = new AlarmStatsResponse { OpenCount = openCount },
            Error = null
        });
    }

    private static async Task<IResult> GetTrendAsync(
        [FromServices] IAlarmRepository repo,
        [FromQuery] string? deviceId,
        [FromQuery] long? startTs,
        [FromQuery] long? endTs,
        [FromQuery] long? bucketSizeMs,
        [FromQuery] int? limit,
        CancellationToken ct)
    {
        // Default: last 7 days, hourly buckets
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var effectiveStartTs = startTs ?? (now - 604800000); // 7 days ago
        var effectiveEndTs = endTs ?? now;
        var effectiveBucketSize = bucketSizeMs ?? 3600000; // 1 hour

        // Validate bucket size (min 1 minute, max 1 day)
        if (effectiveBucketSize < 60000 || effectiveBucketSize > 86400000)
        {
            return Results.BadRequest(new ApiResponse<string>
            {
                Success = false,
                Data = null,
                Error = "bucketSizeMs must be between 60000 (1 minute) and 86400000 (1 day)"
            });
        }

        var query = new AlarmTrendQuery
        {
            DeviceId = deviceId,
            StartTs = effectiveStartTs,
            EndTs = effectiveEndTs,
            BucketSizeMs = effectiveBucketSize,
            Limit = limit
        };

        var result = await repo.GetTrendAsync(query, ct);

        return Results.Ok(new ApiResponse<IReadOnlyList<AlarmTrendBucket>>
        {
            Success = true,
            Data = result,
            Error = null
        });
    }

    // ========== 告警聚合组处理方法 ==========

    private static async Task<IResult> QueryGroupsAsync(
        [FromServices] IAlarmGroupRepository repo,
        [FromQuery] string? deviceId,
        [FromQuery] int? status,
        [FromQuery] int? minSeverity,
        [FromQuery] long? startTs,
        [FromQuery] long? endTs,
        [FromQuery] int? limit,
        [FromQuery] string? after,
        CancellationToken ct)
    {
        AlarmStatus? parsedStatus = null;
        if (status.HasValue)
        {
            if (!Enum.IsDefined(typeof(AlarmStatus), status.Value))
            {
                return Results.BadRequest(new ApiResponse<string>
                {
                    Success = false,
                    Data = null,
                    Error = "status 参数非法（0=Open, 1=Acknowledged, 2=Closed）"
                });
            }
            parsedStatus = (AlarmStatus)status.Value;
        }

        if (minSeverity.HasValue && (minSeverity.Value < 1 || minSeverity.Value > 4))
        {
            return Results.BadRequest(new ApiResponse<string>
            {
                Success = false,
                Data = null,
                Error = "minSeverity 参数非法（范围 1-4）"
            });
        }

        var token = PageToken.Parse(after);

        var query = new AlarmGroupQuery
        {
            DeviceId = deviceId,
            Status = parsedStatus,
            MinSeverity = minSeverity,
            StartTs = startTs,
            EndTs = endTs,
            Limit = limit ?? 50,
            After = token
        };

        var result = await repo.QueryAsync(query, ct);

        return Results.Ok(new ApiResponse<PagedResult<AlarmGroup>>
        {
            Success = true,
            Data = result,
            Error = null
        });
    }

    private static async Task<IResult> GetGroupStatsAsync(
        [FromServices] IAlarmGroupRepository repo,
        [FromQuery] string? deviceId,
        CancellationToken ct)
    {
        var openCount = await repo.GetOpenGroupCountAsync(deviceId, ct);

        // 简化版统计，只返回 open count（与前端 AlarmGroupStats 兼容）
        return Results.Ok(new ApiResponse<AlarmGroupStatsResponse>
        {
            Success = true,
            Data = new AlarmGroupStatsResponse
            {
                OpenCount = openCount,
                AcknowledgedCount = 0, // TODO: 实现完整统计
                ClosedCount = 0
            },
            Error = null
        });
    }

    private static async Task<IResult> GetGroupDetailAsync(
        [FromServices] IAlarmGroupRepository groupRepo,
        [FromServices] IAlarmRepository alarmRepo,
        [FromRoute] string groupId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(groupId))
        {
            return Results.BadRequest(new ApiResponse<string>
            {
                Success = false,
                Data = null,
                Error = "groupId 不能为空"
            });
        }

        var group = await groupRepo.GetAsync(groupId, ct);
        if (group is null)
        {
            return Results.NotFound(new ApiResponse<string>
            {
                Success = false,
                Data = null,
                Error = "告警聚合组不存在"
            });
        }

        // 获取子告警 ID 列表
        var childAlarmIds = await groupRepo.GetChildAlarmIdsAsync(groupId, ct);

        // 批量获取子告警详情（优化N+1查询）
        var children = await alarmRepo.GetByIdsAsync(childAlarmIds, ct);

        return Results.Ok(new ApiResponse<AlarmGroupDetailResponse>
        {
            Success = true,
            Data = new AlarmGroupDetailResponse
            {
                Group = group,
                Children = children
            },
            Error = null
        });
    }

    private static async Task<IResult> AckGroupAsync(
        [FromServices] IAlarmGroupRepository repo,
        [FromServices] IAuditLogRepository auditRepo,
        HttpContext httpContext,
        [FromRoute] string groupId,
        [FromBody] AckAlarmRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(groupId))
        {
            return Results.BadRequest(new ApiResponse<string>
            {
                Success = false,
                Data = null,
                Error = "groupId 不能为空"
            });
        }

        if (request is null)
        {
            return Results.BadRequest(new ApiResponse<string>
            {
                Success = false,
                Data = null,
                Error = "请求体不能为空"
            });
        }

        if (string.IsNullOrWhiteSpace(request.AckedBy))
        {
            return Results.BadRequest(new ApiResponse<string>
            {
                Success = false,
                Data = null,
                Error = "AckedBy 不能为空"
            });
        }

        var existing = await repo.GetAsync(groupId, ct);
        if (existing is null)
        {
            return Results.NotFound(new ApiResponse<string>
            {
                Success = false,
                Data = null,
                Error = "告警聚合组不存在"
            });
        }

        if (existing.AggregateStatus == AlarmStatus.Closed)
        {
            return Results.BadRequest(new ApiResponse<string>
            {
                Success = false,
                Data = null,
                Error = "已关闭的聚合组不允许确认"
            });
        }

        await repo.AckGroupAsync(groupId, request.AckedBy, request.AckNote, ct);

        var updated = await repo.GetAsync(groupId, ct);
        Log.Information("Alarm group ack endpoint: {GroupId} by={AckedBy}", groupId, request.AckedBy);

        // 审计日志
        await AuditLogHelper.LogAsync(auditRepo, httpContext, "alarm_group.ack", "alarm_group",
            groupId, $"Alarm group acknowledged. Note: {request.AckNote ?? "无"}", ct);

        return Results.Ok(new ApiResponse<AlarmGroup>
        {
            Success = true,
            Data = updated!,
            Error = null
        });
    }

    private static async Task<IResult> CloseGroupAsync(
        [FromServices] IAlarmGroupRepository repo,
        [FromServices] IAuditLogRepository auditRepo,
        HttpContext httpContext,
        [FromRoute] string groupId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(groupId))
        {
            return Results.BadRequest(new ApiResponse<string>
            {
                Success = false,
                Data = null,
                Error = "groupId 不能为空"
            });
        }

        var existing = await repo.GetAsync(groupId, ct);
        if (existing is null)
        {
            return Results.NotFound(new ApiResponse<string>
            {
                Success = false,
                Data = null,
                Error = "告警聚合组不存在"
            });
        }

        if (existing.AggregateStatus == AlarmStatus.Closed)
        {
            return Results.Ok(new ApiResponse<AlarmGroup>
            {
                Success = true,
                Data = existing,
                Error = null
            });
        }

        await repo.CloseGroupAsync(groupId, ct);

        var updated = await repo.GetAsync(groupId, ct);
        Log.Information("Alarm group close endpoint: {GroupId}", groupId);

        // 审计日志
        await AuditLogHelper.LogAsync(auditRepo, httpContext, "alarm_group.close", "alarm_group",
            groupId, "Alarm group closed", ct);

        return Results.Ok(new ApiResponse<AlarmGroup>
        {
            Success = true,
            Data = updated!,
            Error = null
        });
    }

    public sealed record CreateAlarmRequest
    {
        public required string DeviceId { get; init; }
        public string? TagId { get; init; }
        public required int Severity { get; init; }
        public required string Code { get; init; }
        public required string Message { get; init; }
    }

    public sealed record AckAlarmRequest
    {
        public required string AckedBy { get; init; }
        public string? AckNote { get; init; }
    }

    public sealed record AlarmStatsResponse
    {
        public int OpenCount { get; init; }
    }

    public sealed record AlarmGroupStatsResponse
    {
        public int OpenCount { get; init; }
        public int AcknowledgedCount { get; init; }
        public int ClosedCount { get; init; }
    }

    public sealed record AlarmGroupDetailResponse
    {
        public required AlarmGroup Group { get; init; }
        public required IReadOnlyList<AlarmRecord> Children { get; init; }
    }
}
