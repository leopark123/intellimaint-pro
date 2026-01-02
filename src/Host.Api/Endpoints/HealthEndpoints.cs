using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using IntelliMaint.Host.Api.Models;
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
            .WithSummary("保存健康快照（内部调用）");
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

    private static async Task<IResult> GetStatsAsync(
        [FromServices] IDbExecutor db,
        [FromServices] IAlarmRepository alarmRepo,
        [FromServices] ITelemetryRepository telemetryRepo,
        CancellationToken ct)
    {
        try
        {
            // device / tag 统计：直接 SQL COUNT，避免拉全表
            const string totalDevicesSql = "SELECT COUNT(*) FROM device;";
            const string enabledDevicesSql = "SELECT COUNT(*) FROM device WHERE enabled = 1;";
            const string totalTagsSql = "SELECT COUNT(*) FROM tag;";
            const string enabledTagsSql = "SELECT COUNT(*) FROM tag WHERE enabled = 1;";
            const string totalAlarmsSql = "SELECT COUNT(*) FROM alarm;";

            var totalDevices = await db.ExecuteScalarAsync<long>(totalDevicesSql, null, ct);
            var enabledDevices = await db.ExecuteScalarAsync<long>(enabledDevicesSql, null, ct);
            var totalTags = await db.ExecuteScalarAsync<long>(totalTagsSql, null, ct);
            var enabledTags = await db.ExecuteScalarAsync<long>(enabledTagsSql, null, ct);
            var totalAlarms = await db.ExecuteScalarAsync<long>(totalAlarmsSql, null, ct);

            var openAlarms = await alarmRepo.GetOpenCountAsync(null, ct);

            // telemetry 统计：复用仓储的 stats + 额外的 last24h count
            var telemetryStats = await telemetryRepo.GetStatsAsync(null, ct);
            var totalTelemetryPoints = telemetryStats.TotalCount;

            var since24h = DateTimeOffset.UtcNow.AddHours(-24).ToUnixTimeMilliseconds();
            const string last24hSql = "SELECT COUNT(*) FROM telemetry WHERE ts >= @SinceTs;";
            var last24HoursTelemetryPoints = await db.ExecuteScalarAsync<long>(last24hSql, new { SinceTs = since24h }, ct);

            // 数据库大小：PRAGMA page_count * page_size
            var pageCount = await db.ExecuteScalarAsync<long>("PRAGMA page_count;", null, ct);
            var pageSize = await db.ExecuteScalarAsync<long>("PRAGMA page_size;", null, ct);
            var databaseSizeBytes = checked(pageCount * pageSize);

            var stats = new SystemStats
            {
                TotalDevices = totalDevices,
                EnabledDevices = enabledDevices,
                TotalTags = totalTags,
                EnabledTags = enabledTags,
                TotalAlarms = totalAlarms,
                OpenAlarms = openAlarms,
                TotalTelemetryPoints = totalTelemetryPoints,
                Last24HoursTelemetryPoints = last24HoursTelemetryPoints,
                DatabaseSizeBytes = databaseSizeBytes
            };

            return Results.Ok(new ApiResponse<SystemStats>
            {
                Success = true,
                Data = stats
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get system stats.");
            return Results.Problem("Failed to get system stats.");
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
}
