using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using IntelliMaint.Infrastructure.Sqlite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace IntelliMaint.Host.Api.Endpoints;

public static class SettingsEndpoints
{
    private static readonly DateTimeOffset StartTime = DateTimeOffset.UtcNow;

    // 预定义设置键与默认值
    private const string KeyRetentionTelemetryDays = "retention.telemetry.days";
    private const string KeyRetentionAlarmDays = "retention.alarm.days";
    private const string KeyRetentionHealthDays = "retention.health.days";

    private const int DefaultTelemetryDays = 30;
    private const int DefaultAlarmDays = 90;
    private const int DefaultHealthDays = 7;

    public static void MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings")
            .WithTags("Settings");

        // 读操作 - 所有已认证用户
        group.MapGet("/info", GetSystemInfoAsync)
            .RequireAuthorization(AuthPolicies.AllAuthenticated);
        group.MapGet("/", GetAllSettingsAsync)
            .RequireAuthorization(AuthPolicies.AllAuthenticated);

        // 写操作 - 仅 Admin
        group.MapPut("/{key}", SetSettingAsync)
            .RequireAuthorization(AuthPolicies.AdminOnly);
        group.MapPost("/cleanup", CleanupDataAsync)
            .RequireAuthorization(AuthPolicies.AdminOnly);
    }

    private static async Task<IResult> GetSystemInfoAsync(
        [FromServices] IDbExecutor db,
        [FromServices] ISqliteConnectionFactory connFactory,
        [FromServices] IConfiguration config,
        CancellationToken ct)
    {
        // 统计数据（注意：不在 ExecuteScalarAsync<long> 返回值上使用 ?? 0）
        var totalTelemetry = await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM telemetry;", null, ct);
        var totalAlarms = await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM alarm;", null, ct);
        var totalDevices = await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM device;", null, ct);
        var totalTags = await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM tag;", null, ct);

        // 数据库大小：PRAGMA page_count * page_size
        var pageCount = await db.ExecuteScalarAsync<long>("PRAGMA page_count;", null, ct);
        var pageSize = await db.ExecuteScalarAsync<long>("PRAGMA page_size;", null, ct);
        var dbSize = pageCount * pageSize;

        var edgeId = config["Edge:EdgeId"] ?? "unknown";
        var dbPath = connFactory.DatabasePath;

        var info = new SystemInfo
        {
            Version = "1.0.0",
            EdgeId = edgeId,
            DatabasePath = Path.GetFullPath(dbPath),
            DatabaseSizeBytes = dbSize,
            UptimeSeconds = (long)(DateTimeOffset.UtcNow - StartTime).TotalSeconds,
            StartTime = StartTime,
            TotalTelemetryPoints = totalTelemetry,
            TotalAlarms = totalAlarms,
            TotalDevices = totalDevices,
            TotalTags = totalTags
        };

        return Results.Ok(ApiResponse<SystemInfo>.Ok(info));
    }

    private static async Task<IResult> GetAllSettingsAsync(
        [FromServices] ISystemSettingRepository repo,
        CancellationToken ct)
    {
        var settings = await repo.GetAllAsync(ct);

        // 确保预定义键在 UI 上一定可见（即使数据库里还没写入）
        var map = settings.ToDictionary(s => s.Key, s => s, StringComparer.OrdinalIgnoreCase);

        EnsureDefault(map, KeyRetentionTelemetryDays, DefaultTelemetryDays.ToString());
        EnsureDefault(map, KeyRetentionAlarmDays, DefaultAlarmDays.ToString());
        EnsureDefault(map, KeyRetentionHealthDays, DefaultHealthDays.ToString());

        var ordered = map.Values.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToList();
        return Results.Ok(ApiResponse<IReadOnlyList<SystemSetting>>.Ok(ordered));
    }

    private static async Task<IResult> SetSettingAsync(
        [FromRoute] string key,
        [FromBody] UpdateSettingRequest request,
        [FromServices] ISystemSettingRepository repo,
        [FromServices] IAuditLogRepository auditRepo,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key))
            return Results.BadRequest(ApiResponse<object>.Fail("key 不能为空"));

        if (request == null || string.IsNullOrWhiteSpace(request.Value))
            return Results.BadRequest(ApiResponse<object>.Fail("value 不能为空"));

        await repo.SetAsync(key, request.Value.Trim(), ct);
        Log.Information("System setting updated: {Key}={Value}", key, request.Value);
        
        await AuditLogHelper.LogAsync(auditRepo, httpContext, "setting.update", "setting",
            key, $"Updated setting: {key} = {request.Value}", ct);

        return Results.Ok(ApiResponse<object>.Ok(null));
    }

    private static async Task<IResult> CleanupDataAsync(
        [FromServices] ISystemSettingRepository settingRepo,
        [FromServices] ITelemetryRepository telemetryRepo,
        [FromServices] IAlarmRepository alarmRepo,
        [FromServices] IHealthSnapshotRepository healthRepo,
        [FromServices] IDbExecutor db,
        [FromServices] IAuditLogRepository auditRepo,
        HttpContext httpContext,
        CancellationToken ct)
    {
        // 读取保留策略（string? 可以用 ?? "默认值"）
        var telemetryDaysStr = await settingRepo.GetAsync(KeyRetentionTelemetryDays, ct) ?? DefaultTelemetryDays.ToString();
        var alarmDaysStr = await settingRepo.GetAsync(KeyRetentionAlarmDays, ct) ?? DefaultAlarmDays.ToString();
        var healthDaysStr = await settingRepo.GetAsync(KeyRetentionHealthDays, ct) ?? DefaultHealthDays.ToString();

        var telemetryDays = TryParsePositiveIntOrDefault(telemetryDaysStr, DefaultTelemetryDays);
        var alarmDays = TryParsePositiveIntOrDefault(alarmDaysStr, DefaultAlarmDays);
        var healthDays = TryParsePositiveIntOrDefault(healthDaysStr, DefaultHealthDays);

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var telemetryCutoff = now - telemetryDays * 24L * 60 * 60 * 1000;
        var alarmCutoff = now - alarmDays * 24L * 60 * 60 * 1000;
        var healthCutoff = now - healthDays * 24L * 60 * 60 * 1000;

        // 清理前大小
        var beforePageCount = await db.ExecuteScalarAsync<long>("PRAGMA page_count;", null, ct);
        var pageSize = await db.ExecuteScalarAsync<long>("PRAGMA page_size;", null, ct);
        var beforeSize = beforePageCount * pageSize;

        // 执行清理
        var deletedTelemetry = await telemetryRepo.DeleteBeforeAsync(telemetryCutoff, ct);
        var deletedAlarms = await alarmRepo.DeleteBeforeAsync(alarmCutoff, ct);
        var deletedHealth = await healthRepo.DeleteBeforeAsync(healthCutoff, ct);

        // VACUUM 释放空间
        await db.ExecuteNonQueryAsync("VACUUM;", null, ct);

        // 清理后大小
        var afterPageCount = await db.ExecuteScalarAsync<long>("PRAGMA page_count;", null, ct);
        var afterSize = afterPageCount * pageSize;

        var result = new CleanupResult
        {
            DeletedTelemetryPoints = deletedTelemetry,
            DeletedAlarms = deletedAlarms,
            DeletedHealthSnapshots = deletedHealth,
            FreedBytes = Math.Max(0, beforeSize - afterSize)
        };

        Log.Information(
            "Cleanup done. telemetry={DeletedTelemetry}, alarms={DeletedAlarms}, health={DeletedHealth}, freedBytes={FreedBytes}",
            result.DeletedTelemetryPoints, result.DeletedAlarms, result.DeletedHealthSnapshots, result.FreedBytes);
        
        await AuditLogHelper.LogAsync(auditRepo, httpContext, "data.cleanup", "system", null,
            $"Cleanup executed. DeletedTelemetry={result.DeletedTelemetryPoints}, DeletedAlarms={result.DeletedAlarms}, DeletedHealth={result.DeletedHealthSnapshots}, FreedBytes={result.FreedBytes}", ct);

        return Results.Ok(ApiResponse<CleanupResult>.Ok(result));
    }

    private static void EnsureDefault(Dictionary<string, SystemSetting> map, string key, string defaultValue)
    {
        if (map.ContainsKey(key)) return;

        map[key] = new SystemSetting
        {
            Key = key,
            Value = defaultValue,
            UpdatedUtc = 0
        };
    }

    private static int TryParsePositiveIntOrDefault(string value, int defaultValue)
    {
        if (int.TryParse(value, out var n) && n > 0) return n;
        return defaultValue;
    }

    // ========= Models =========

    public sealed record UpdateSettingRequest
    {
        public required string Value { get; init; }
    }

    public sealed record SystemInfo
    {
        public string Version { get; init; } = "1.0.0";
        public string EdgeId { get; init; } = "";
        public string DatabasePath { get; init; } = "";
        public long DatabaseSizeBytes { get; init; }
        public long UptimeSeconds { get; init; }
        public DateTimeOffset StartTime { get; init; }
        public long TotalTelemetryPoints { get; init; }
        public long TotalAlarms { get; init; }
        public long TotalDevices { get; init; }
        public long TotalTags { get; init; }
    }

    public sealed record CleanupResult
    {
        public int DeletedTelemetryPoints { get; init; }
        public int DeletedAlarms { get; init; }
        public int DeletedHealthSnapshots { get; init; }
        public long FreedBytes { get; init; }
    }

    // 为了不依赖外部 Models 文件是否存在，这里提供一个本地 ApiResponse
    public sealed record ApiResponse<T>
    {
        public bool Success { get; init; } = true;
        public T? Data { get; init; }
        public string? Error { get; init; }
        public long Timestamp { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        public static ApiResponse<T> Ok(T? data) => new()
        {
            Success = true,
            Data = data,
            Error = null,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        public static ApiResponse<T> Fail(string error) => new()
        {
            Success = false,
            Data = default,
            Error = error,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }
}
