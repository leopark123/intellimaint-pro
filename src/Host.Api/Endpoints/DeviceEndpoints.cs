using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using IntelliMaint.Host.Api.Models;
using IntelliMaint.Host.Api.Services;
using IntelliMaint.Host.Api.Validators;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace IntelliMaint.Host.Api.Endpoints;

public static class DeviceEndpoints
{
    public static void MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/devices")
            .WithTags("Devices");

        // 读操作 - 所有已认证用户
        group.MapGet("/", ListAsync)
            .WithName("ListDevices")
            .WithSummary("获取所有设备")
            .RequireAuthorization(AuthPolicies.AllAuthenticated);

        group.MapGet("/{deviceId}", GetAsync)
            .WithName("GetDevice")
            .WithSummary("获取单个设备")
            .RequireAuthorization(AuthPolicies.AllAuthenticated);

        // 写操作 - 仅 Admin
        group.MapPost("/", CreateAsync)
            .WithName("CreateDevice")
            .WithSummary("创建设备")
            .RequireAuthorization(AuthPolicies.AdminOnly);

        group.MapPut("/{deviceId}", UpdateAsync)
            .WithName("UpdateDevice")
            .WithSummary("更新设备")
            .RequireAuthorization(AuthPolicies.AdminOnly);

        group.MapDelete("/{deviceId}", DeleteAsync)
            .WithName("DeleteDevice")
            .WithSummary("删除设备")
            .RequireAuthorization(AuthPolicies.AdminOnly);
    }

    private static async Task<IResult> ListAsync(
        [FromServices] IDeviceRepository repo,
        [FromServices] CacheService cache,
        CancellationToken ct)
    {
        // v48: 使用缓存
        var devices = await cache.GetOrCreateAsync(
            CacheService.Keys.DeviceList,
            () => repo.ListAsync(ct),
            TimeSpan.FromMinutes(2));
        
        return Results.Ok(new ApiResponse<IReadOnlyList<DeviceDto>> { Data = devices });
    }

    private static async Task<IResult> GetAsync(
        [FromServices] IDeviceRepository repo,
        [FromServices] CacheService cache,
        [FromRoute] string deviceId,
        CancellationToken ct)
    {
        // P1: 使用 InputValidator 验证
        var idValidation = InputValidator.ValidateIdentifier(deviceId, "deviceId");
        if (!idValidation.IsValid)
            return Results.BadRequest(new ApiResponse<DeviceDto> { Success = false, Error = idValidation.Error });

        // v48: 使用缓存
        var device = await cache.GetOrCreateAsync(
            CacheService.Keys.DeviceById(deviceId),
            () => repo.GetAsync(deviceId, ct),
            TimeSpan.FromMinutes(5));
        
        if (device is null)
            return Results.NotFound(new ApiResponse<DeviceDto> { Success = false, Error = $"设备不存在: {deviceId}" });

        return Results.Ok(new ApiResponse<DeviceDto> { Data = device });
    }

    private static async Task<IResult> CreateAsync(
        [FromServices] IDeviceRepository repo,
        [FromServices] IAuditLogRepository auditRepo,
        [FromServices] IConfigRevisionProvider revisionProvider,
        [FromServices] CacheService cache,
        HttpContext httpContext,
        [FromBody] CreateDeviceRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return Results.BadRequest(new ApiResponse<DeviceDto> { Success = false, Error = "请求体不能为空" });

        // P1: 使用 InputValidator 进行输入验证
        var idValidation = InputValidator.ValidateIdentifier(request.DeviceId, "DeviceId");
        if (!idValidation.IsValid)
            return Results.BadRequest(new ApiResponse<DeviceDto> { Success = false, Error = idValidation.Error });

        var nameValidation = InputValidator.ValidateOptionalDisplayName(request.Name, "设备名称");
        if (!nameValidation.IsValid)
            return Results.BadRequest(new ApiResponse<DeviceDto> { Success = false, Error = nameValidation.Error });

        var existing = await repo.GetAsync(request.DeviceId, ct);
        if (existing is not null)
            return Results.BadRequest(new ApiResponse<DeviceDto> { Success = false, Error = $"DeviceId 已存在: {request.DeviceId}" });

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var device = new DeviceDto
        {
            DeviceId = request.DeviceId,
            Name = request.Name,
            Location = request.Location,
            Model = request.Model,
            Protocol = request.Protocol,
            Host = request.Host,
            Port = request.Port,
            ConnectionString = request.ConnectionString,
            Enabled = request.Enabled,
            CreatedUtc = now,
            UpdatedUtc = now,
            Metadata = request.Metadata
        };

        await repo.UpsertAsync(device, ct);
        
        // v48: 使缓存失效
        cache.InvalidateDevice();

        Log.Information("Created device {DeviceId}", request.DeviceId);
        
        // 递增配置版本号（触发 ConfigChangeWatcher 检测变更）
        await revisionProvider.IncrementRevisionAsync(ct);
        
        await AuditLogHelper.LogAsync(auditRepo, httpContext, "device.create", "device", 
            request.DeviceId, $"Created device: {request.Name}", ct);

        var saved = await repo.GetAsync(request.DeviceId, ct);
        return Results.Ok(new ApiResponse<DeviceDto> { Data = saved ?? device });
    }

    private static async Task<IResult> UpdateAsync(
        [FromServices] IDeviceRepository repo,
        [FromServices] IAuditLogRepository auditRepo,
        [FromServices] IConfigRevisionProvider revisionProvider,
        [FromServices] CacheService cache,
        HttpContext httpContext,
        [FromRoute] string deviceId,
        [FromBody] UpdateDeviceRequest request,
        CancellationToken ct)
    {
        // P1: 使用 InputValidator 验证
        var idValidation = InputValidator.ValidateIdentifier(deviceId, "deviceId");
        if (!idValidation.IsValid)
            return Results.BadRequest(new ApiResponse<DeviceDto> { Success = false, Error = idValidation.Error });

        if (request is null)
            return Results.BadRequest(new ApiResponse<DeviceDto> { Success = false, Error = "请求体不能为空" });

        var nameValidation = InputValidator.ValidateOptionalDisplayName(request.Name, "设备名称");
        if (!nameValidation.IsValid)
            return Results.BadRequest(new ApiResponse<DeviceDto> { Success = false, Error = nameValidation.Error });

        var existing = await repo.GetAsync(deviceId, ct);
        if (existing is null)
            return Results.NotFound(new ApiResponse<DeviceDto> { Success = false, Error = $"设备不存在: {deviceId}" });

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var updated = existing with
        {
            Name = request.Name ?? existing.Name,
            Location = request.Location ?? existing.Location,
            Model = request.Model ?? existing.Model,
            Protocol = request.Protocol ?? existing.Protocol,
            Host = request.Host ?? existing.Host,
            Port = request.Port ?? existing.Port,
            ConnectionString = request.ConnectionString ?? existing.ConnectionString,
            Enabled = request.Enabled ?? existing.Enabled,
            Metadata = request.Metadata ?? existing.Metadata,
            UpdatedUtc = now
        };

        await repo.UpsertAsync(updated, ct);
        
        // v48: 使缓存失效
        cache.InvalidateDevice(deviceId);

        Log.Information("Updated device {DeviceId}", deviceId);
        
        // 递增配置版本号
        await revisionProvider.IncrementRevisionAsync(ct);
        
        await AuditLogHelper.LogAsync(auditRepo, httpContext, "device.update", "device",
            deviceId, $"Updated device: {updated.Name}", ct);

        var saved = await repo.GetAsync(deviceId, ct);
        return Results.Ok(new ApiResponse<DeviceDto> { Data = saved ?? updated });
    }

    private static async Task<IResult> DeleteAsync(
        [FromServices] IDeviceRepository repo,
        [FromServices] IAuditLogRepository auditRepo,
        [FromServices] IConfigRevisionProvider revisionProvider,
        [FromServices] CacheService cache,
        HttpContext httpContext,
        [FromRoute] string deviceId,
        CancellationToken ct)
    {
        // P1: 使用 InputValidator 验证
        var idValidation = InputValidator.ValidateIdentifier(deviceId, "deviceId");
        if (!idValidation.IsValid)
            return Results.BadRequest(new ApiResponse<object> { Success = false, Error = idValidation.Error });

        var existing = await repo.GetAsync(deviceId, ct);
        if (existing is null)
            return Results.NotFound(new ApiResponse<object> { Success = false, Error = $"设备不存在: {deviceId}" });

        await repo.DeleteAsync(deviceId, ct);
        
        // v48: 使缓存失效
        cache.InvalidateDevice(deviceId);

        Log.Information("Deleted device {DeviceId}", deviceId);
        
        // 递增配置版本号
        await revisionProvider.IncrementRevisionAsync(ct);
        
        await AuditLogHelper.LogAsync(auditRepo, httpContext, "device.delete", "device",
            deviceId, "Device deleted", ct);

        return Results.Ok(new ApiResponse<object> { Data = null });
    }
}

/// <summary>
/// Request model for creating a device
/// </summary>
public sealed record CreateDeviceRequest
{
    public required string DeviceId { get; init; }
    public string? Name { get; init; }
    public string? Location { get; init; }
    public string? Model { get; init; }
    public string? Protocol { get; init; }
    public string? Host { get; init; }
    public int? Port { get; init; }
    public string? ConnectionString { get; init; }
    public bool Enabled { get; init; } = true;
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Request model for updating a device
/// </summary>
public sealed record UpdateDeviceRequest
{
    public string? Name { get; init; }
    public string? Location { get; init; }
    public string? Model { get; init; }
    public string? Protocol { get; init; }
    public string? Host { get; init; }
    public int? Port { get; init; }
    public string? ConnectionString { get; init; }
    public bool? Enabled { get; init; }
    public Dictionary<string, string>? Metadata { get; init; }
}
