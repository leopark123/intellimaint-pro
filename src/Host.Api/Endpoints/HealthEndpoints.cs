using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using IntelliMaint.Host.Api.Models;
using IntelliMaint.Host.Api.Services;
using IntelliMaint.Infrastructure.Sqlite;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace IntelliMaint.Host.Api.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/health")
            .WithTags("Health")
            .RequireAuthorization(AuthPolicies.AllAuthenticated);  // 所有已认证用户可访问

        group.MapGet("", GetCurrentHealthAsync)
            .WithName("GetCurrentHealth")
            .WithSummary("获取当前健康状态");

        group.MapGet("/history", GetHistoryAsync)
            .WithName("GetHealthHistory")
            .WithSummary("获取历史健康快照");

        group.MapGet("/stats", GetStatsAsync)
            .WithName("GetSystemStats")
            .WithSummary("获取系统统计");

        group.MapPost("/snapshot", SaveSnapshotAsync)
            .WithName("SaveHealthSnapshot")
            .WithSummary("保存健康快照（内部调用）")
            .AllowAnonymous();  // Edge 服务内部调用，无需认证

        // v56.2: 数据库健康检查端点
        group.MapGet("/database", GetDatabaseHealthAsync)
            .WithName("GetDatabaseHealth")
            .WithSummary("获取数据库健康状态");

        group.MapGet("/database/statistics", GetDatabaseStatisticsAsync)
            .WithName("GetDatabaseStatistics")
            .WithSummary("获取数据库统计信息");

        group.MapPost("/database/maintenance", PerformDatabaseMaintenanceAsync)
            .WithName("PerformDatabaseMaintenance")
            .WithSummary("执行数据库维护")
            .RequireAuthorization(AuthPolicies.AdminOnly);
    }

    private static async Task<IResult> GetCurrentHealthAsync(
        [FromServices] IHealthSnapshotRepository repo,
        CancellationToken ct)
    {
        try
        {
            var recent = await repo.GetRecentAsync(1, ct);
            if (recent.Count > 0)
            {
                return Results.Ok(new ApiResponse<HealthSnapshot>
                {
                    Success = true,
                    Data = recent[0]
                });
            }

            var now = DateTimeOffset.UtcNow;
            var snapshot = new HealthSnapshot
            {
                UtcTime = now,
                OverallState = HealthState.Healthy,
                DatabaseState = DatabaseState.Healthy,
                QueueState = QueueState.Normal,
                QueueDepth = 0,
                DroppedPoints = 0,
                WriteLatencyMsP95 = 0,
                Collectors = new Dictionary<string, CollectorHealth>(),
                MqttConnected = false,
                OutboxDepth = 0,
                MemoryUsedMb = GC.GetTotalMemory(false) / (1024 * 1024)
            };

            return Results.Ok(new ApiResponse<HealthSnapshot>
            {
                Success = true,
                Data = snapshot
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get current health.");
            return Results.Problem("Failed to get health.");
        }
    }

    private static async Task<IResult> GetHistoryAsync(
        [FromServices] IHealthSnapshotRepository repo,
        [FromQuery] int? count,
        CancellationToken ct)
    {
        try
        {
            var n = count ?? 60;
            if (n <= 0) n = 60;
            if (n > 1000) n = 1000;

            var items = await repo.GetRecentAsync(n, ct);
            return Results.Ok(new ApiResponse<IReadOnlyList<HealthSnapshot>>
            {
                Success = true,
                Data = items
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get health history.");
            return Results.Problem("Failed to get health history.");
        }
    }

    private static async Task<IResult> SaveSnapshotAsync(
        [FromServices] IHealthSnapshotRepository repo,
        [FromBody] HealthSnapshot snapshot,
        CancellationToken ct)
    {
        try
        {
            await repo.SaveAsync(snapshot, ct);
            return Results.Ok(new ApiResponse<object>
            {
                Success = true,
                Data = null
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save health snapshot.");
            return Results.Problem("Failed to save health snapshot.");
        }
    }

    private static Task<IResult> GetStatsAsync(
        [FromServices] StatsCacheService statsCache,
        CancellationToken ct)
    {
        try
        {
            // v56.1: 使用缓存的统计数据，避免每次请求都执行 COUNT(*) 全表扫描
            var cached = statsCache.GetStats();

            var stats = new SystemStats
            {
                TotalDevices = cached.TotalDevices,
                EnabledDevices = cached.EnabledDevices,
                TotalTags = cached.TotalTags,
                EnabledTags = cached.EnabledTags,
                TotalAlarms = cached.TotalAlarms,
                OpenAlarms = cached.OpenAlarms,
                TotalTelemetryPoints = cached.TotalTelemetryPoints,
                Last24HoursTelemetryPoints = cached.Last24HoursTelemetryPoints,
                DatabaseSizeBytes = cached.DatabaseSizeBytes
            };

            return Task.FromResult(Results.Ok(new ApiResponse<SystemStats>
            {
                Success = true,
                Data = stats
            }));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get system stats.");
            return Task.FromResult(Results.Problem("Failed to get system stats."));
        }
    }

    public sealed record SystemStats
    {
        public long TotalDevices { get; init; }
        public long EnabledDevices { get; init; }
        public long TotalTags { get; init; }
        public long EnabledTags { get; init; }
        public long TotalAlarms { get; init; }
        public long OpenAlarms { get; init; }
        public long TotalTelemetryPoints { get; init; }
        public long Last24HoursTelemetryPoints { get; init; }
        public long DatabaseSizeBytes { get; init; }
    }

    // v56.2: 数据库健康检查端点实现

    private static async Task<IResult> GetDatabaseHealthAsync(
        [FromServices] ITimeSeriesDb timeSeriesDb,
        CancellationToken ct)
    {
        try
        {
            var health = await timeSeriesDb.GetHealthAsync(ct);
            return Results.Ok(new ApiResponse<DbHealthStatus>
            {
                Success = true,
                Data = health
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get database health.");
            return Results.Problem("Failed to get database health.");
        }
    }

    private static async Task<IResult> GetDatabaseStatisticsAsync(
        [FromServices] ITimeSeriesDb timeSeriesDb,
        CancellationToken ct)
    {
        try
        {
            var stats = await timeSeriesDb.GetStatisticsAsync(ct);
            return Results.Ok(new ApiResponse<DbStatistics>
            {
                Success = true,
                Data = stats
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get database statistics.");
            return Results.Problem("Failed to get database statistics.");
        }
    }

    private static async Task<IResult> PerformDatabaseMaintenanceAsync(
        [FromServices] ITimeSeriesDb timeSeriesDb,
        [FromBody] MaintenanceOptions? options,
        CancellationToken ct)
    {
        try
        {
            var opts = options ?? new MaintenanceOptions();
            Log.Information("Starting database maintenance with options: {@Options}", opts);

            var result = await timeSeriesDb.PerformMaintenanceAsync(opts, ct);

            if (result.Success)
            {
                Log.Information(
                    "Database maintenance completed. Duration: {DurationMs}ms, Rows deleted: {RowsDeleted}, Space reclaimed: {SpaceReclaimed}MB",
                    result.DurationMs, result.RowsDeleted, result.SpaceReclaimedBytes / 1024.0 / 1024.0);
            }
            else
            {
                Log.Warning("Database maintenance completed with errors: {Error}", result.Error);
            }

            return Results.Ok(new ApiResponse<MaintenanceResult>
            {
                Success = result.Success,
                Data = result
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Database maintenance failed.");
            return Results.Problem("Database maintenance failed: " + ex.Message);
        }
    }
}
