using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.AspNetCore.Mvc;
using static IntelliMaint.Core.Contracts.AuthPolicies;

namespace IntelliMaint.Host.Api.Endpoints;

/// <summary>
/// v65: Edge 配置管理 API 端点
/// </summary>
public static class EdgeConfigEndpoints
{
    public static void MapEdgeConfigEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/edge-config")
            .WithTags("Edge配置管理");

        // 获取所有 Edge 列表
        group.MapGet("/", ListEdges)
            .WithName("ListEdges")
            .WithSummary("获取所有Edge节点")
            .Produces<ApiResponse<IReadOnlyList<EdgeSummaryDto>>>();

        // 获取 Edge 配置
        group.MapGet("/{edgeId}", GetEdgeConfig)
            .WithName("GetEdgeConfig")
            .WithSummary("获取Edge节点配置")
            .Produces<ApiResponse<EdgeConfigDto>>();

        // 更新 Edge 配置
        group.MapPut("/{edgeId}", UpdateEdgeConfig)
            .WithName("UpdateEdgeConfig")
            .WithSummary("更新Edge节点配置")
            .RequireAuthorization(OperatorOrAbove)
            .Produces<ApiResponse<EdgeConfigDto>>();

        // 获取标签配置列表
        group.MapGet("/{edgeId}/tags", GetTagConfigs)
            .WithName("GetTagProcessingConfigs")
            .WithSummary("获取标签级预处理配置")
            .Produces<ApiResponse<PagedTagConfigResult>>();

        // 批量更新标签配置
        group.MapPut("/{edgeId}/tags", BatchUpdateTagConfigs)
            .WithName("BatchUpdateTagConfigs")
            .WithSummary("批量更新标签预处理配置")
            .RequireAuthorization(OperatorOrAbove)
            .Produces<ApiResponse<object>>();

        // 删除标签配置
        group.MapDelete("/{edgeId}/tags/{tagId}", DeleteTagConfig)
            .WithName("DeleteTagConfig")
            .WithSummary("删除标签预处理配置")
            .RequireAuthorization(OperatorOrAbove)
            .Produces(StatusCodes.Status204NoContent);

        // 获取 Edge 状态
        group.MapGet("/{edgeId}/status", GetEdgeStatus)
            .WithName("GetEdgeStatus")
            .WithSummary("获取Edge运行状态")
            .Produces<ApiResponse<EdgeStatusDto>>();

        // 更新 Edge 状态（心跳）
        group.MapPost("/{edgeId}/heartbeat", UpdateEdgeHeartbeat)
            .WithName("UpdateEdgeHeartbeat")
            .WithSummary("Edge心跳上报")
            .Produces<ApiResponse<object>>();

        // 通知 Edge 同步配置
        group.MapPost("/{edgeId}/sync", NotifyConfigSync)
            .WithName("NotifyEdgeConfigSync")
            .WithSummary("通知Edge同步配置")
            .RequireAuthorization(OperatorOrAbove)
            .Produces<ApiResponse<object>>();
    }

    private static async Task<IResult> ListEdges(
        [FromServices] IEdgeConfigRepository repo,
        CancellationToken ct)
    {
        var edges = await repo.ListAllAsync(ct);
        return Results.Ok(new ApiResponse<IReadOnlyList<EdgeSummaryDto>>
        {
            Success = true,
            Data = edges
        });
    }

    private static async Task<IResult> GetEdgeConfig(
        string edgeId,
        [FromServices] IEdgeConfigRepository repo,
        CancellationToken ct)
    {
        var config = await repo.GetAsync(edgeId, ct);
        if (config == null)
        {
            // 返回默认配置
            config = new EdgeConfigDto
            {
                EdgeId = edgeId,
                Name = edgeId,
                CreatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }
        return Results.Ok(new ApiResponse<EdgeConfigDto>
        {
            Success = true,
            Data = config
        });
    }

    private static async Task<IResult> UpdateEdgeConfig(
        string edgeId,
        [FromBody] EdgeConfigDto config,
        [FromServices] IEdgeConfigRepository repo,
        [FromServices] IEdgeNotificationService notifier,
        HttpContext http,
        CancellationToken ct)
    {
        var userId = http.User.Identity?.Name ?? "system";
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 检查是否已存在配置
        var existing = await repo.GetAsync(edgeId, ct);

        var updated = config with
        {
            EdgeId = edgeId,
            CreatedUtc = existing?.CreatedUtc ?? now,
            UpdatedUtc = now,
            UpdatedBy = userId
        };

        await repo.UpsertAsync(updated, ct);

        // 通知 Edge 配置已变更
        await notifier.NotifyConfigChangedAsync(edgeId, ct);

        return Results.Ok(new ApiResponse<EdgeConfigDto>
        {
            Success = true,
            Data = updated
        });
    }

    private static async Task<IResult> GetTagConfigs(
        string edgeId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? search,
        [FromServices] ITagProcessingConfigRepository repo,
        CancellationToken ct)
    {
        var result = await repo.ListByEdgeAsync(edgeId, page ?? 1, pageSize ?? 50, search, ct);
        return Results.Ok(new ApiResponse<PagedTagConfigResult>
        {
            Success = true,
            Data = result
        });
    }

    private static async Task<IResult> BatchUpdateTagConfigs(
        string edgeId,
        [FromBody] BatchUpdateTagConfigRequest request,
        [FromServices] ITagProcessingConfigRepository repo,
        [FromServices] IEdgeNotificationService notifier,
        CancellationToken ct)
    {
        await repo.BatchUpsertAsync(edgeId, request.Tags, ct);
        await notifier.NotifyConfigChangedAsync(edgeId, ct);

        return Results.Ok(new ApiResponse<object>
        {
            Success = true,
            Message = $"Updated {request.Tags.Count} tag configs"
        });
    }

    private static async Task<IResult> DeleteTagConfig(
        string edgeId,
        string tagId,
        [FromServices] ITagProcessingConfigRepository repo,
        CancellationToken ct)
    {
        await repo.DeleteAsync(edgeId, tagId, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> GetEdgeStatus(
        string edgeId,
        [FromServices] IEdgeStatusRepository repo,
        CancellationToken ct)
    {
        var status = await repo.GetAsync(edgeId, ct);
        return Results.Ok(new ApiResponse<EdgeStatusDto?>
        {
            Success = true,
            Data = status
        });
    }

    private static async Task<IResult> UpdateEdgeHeartbeat(
        string edgeId,
        [FromBody] EdgeStatusDto status,
        [FromServices] IEdgeStatusRepository repo,
        CancellationToken ct)
    {
        var updated = status with
        {
            EdgeId = edgeId,
            LastHeartbeatUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await repo.UpdateAsync(updated, ct);

        return Results.Ok(new ApiResponse<object>
        {
            Success = true,
            Message = "Heartbeat received"
        });
    }

    private static async Task<IResult> NotifyConfigSync(
        string edgeId,
        [FromServices] IEdgeNotificationService notifier,
        CancellationToken ct)
    {
        await notifier.NotifyConfigChangedAsync(edgeId, ct);
        return Results.Ok(new ApiResponse<object>
        {
            Success = true,
            Message = "Sync notification sent"
        });
    }

    // API 响应包装类
    private record ApiResponse<T>
    {
        public bool Success { get; init; }
        public T? Data { get; init; }
        public string? Message { get; init; }
    }
}
