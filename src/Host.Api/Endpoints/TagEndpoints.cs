using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using IntelliMaint.Host.Api.Models;
using IntelliMaint.Host.Api.Services;
using IntelliMaint.Host.Api.Validators;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace IntelliMaint.Host.Api.Endpoints;

public static class TagEndpoints
{
    public static void MapTagEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tags")
            .WithTags("Tags");

        // GET /api/tags?deviceId=xxx - 所有已认证用户可读
        group.MapGet("/", ListByDeviceAsync)
            .WithName("ListTagsByDevice")
            .WithSummary("按设备获取标签列表")
            .RequireAuthorization(AuthPolicies.AllAuthenticated);

        // GET /api/tags/{tagId} - 所有已认证用户可读
        group.MapGet("/{tagId}", GetAsync)
            .WithName("GetTag")
            .WithSummary("获取单个标签")
            .RequireAuthorization(AuthPolicies.AllAuthenticated);

        // POST /api/tags - 仅 Admin
        group.MapPost("/", CreateAsync)
            .WithName("CreateTag")
            .WithSummary("创建标签")
            .RequireAuthorization(AuthPolicies.AdminOnly);

        // PUT /api/tags/{tagId} - 仅 Admin
        group.MapPut("/{tagId}", UpdateAsync)
            .WithName("UpdateTag")
            .WithSummary("更新标签")
            .RequireAuthorization(AuthPolicies.AdminOnly);

        // DELETE /api/tags/{tagId} - 仅 Admin
        group.MapDelete("/{tagId}", DeleteAsync)
            .WithName("DeleteTag")
            .WithSummary("删除标签")
            .RequireAuthorization(AuthPolicies.AdminOnly);
    }

    private static async Task<IResult> ListByDeviceAsync(
        [FromServices] ITagRepository repo,
        [FromServices] CacheService cache,
        [FromQuery] string? deviceId,
        CancellationToken ct)
    {
        // v56.1: 使用缓存
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            var allTags = await cache.GetOrCreateAsync(
                CacheService.Keys.TagList,
                () => repo.ListAllAsync(ct),
                TimeSpan.FromMinutes(2));
            return Results.Ok(new ApiResponse<IReadOnlyList<TagDto>> { Data = allTags });
        }

        var tags = await cache.GetOrCreateAsync(
            CacheService.Keys.TagsByDevice(deviceId),
            () => repo.ListByDeviceAsync(deviceId, ct),
            TimeSpan.FromMinutes(2));
        return Results.Ok(new ApiResponse<IReadOnlyList<TagDto>> { Data = tags });
    }

    private static async Task<IResult> GetAsync(
        [FromServices] ITagRepository repo,
        [FromRoute] string tagId,
        CancellationToken ct)
    {
        // P1: 使用 InputValidator 验证
        var idValidation = InputValidator.ValidateIdentifier(tagId, "tagId");
        if (!idValidation.IsValid)
            return Results.BadRequest(new ApiResponse<TagDto> { Success = false, Error = idValidation.Error });

        var tag = await repo.GetAsync(tagId, ct);
        if (tag is null)
            return Results.NotFound(new ApiResponse<TagDto> { Success = false, Error = $"标签不存在: {tagId}" });

        return Results.Ok(new ApiResponse<TagDto> { Data = tag });
    }

    private static async Task<IResult> CreateAsync(
        [FromServices] ITagRepository repo,
        [FromServices] IAuditLogRepository auditRepo,
        [FromServices] IConfigRevisionProvider revisionProvider,
        [FromServices] CacheService cache,
        HttpContext httpContext,
        [FromBody] CreateTagRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return Results.BadRequest(new ApiResponse<TagDto> { Success = false, Error = "请求体不能为空" });

        // P1: 使用 InputValidator 验证
        var tagIdValidation = InputValidator.ValidateIdentifier(request.TagId, "TagId");
        if (!tagIdValidation.IsValid)
            return Results.BadRequest(new ApiResponse<TagDto> { Success = false, Error = tagIdValidation.Error });

        var deviceIdValidation = InputValidator.ValidateIdentifier(request.DeviceId, "DeviceId");
        if (!deviceIdValidation.IsValid)
            return Results.BadRequest(new ApiResponse<TagDto> { Success = false, Error = deviceIdValidation.Error });

        var nameValidation = InputValidator.ValidateOptionalDisplayName(request.Name, "标签名称");
        if (!nameValidation.IsValid)
            return Results.BadRequest(new ApiResponse<TagDto> { Success = false, Error = nameValidation.Error });

        var descValidation = InputValidator.ValidateDescription(request.Description);
        if (!descValidation.IsValid)
            return Results.BadRequest(new ApiResponse<TagDto> { Success = false, Error = descValidation.Error });

        if (!Enum.IsDefined(typeof(TagValueType), request.DataType))
            return Results.BadRequest(new ApiResponse<TagDto> { Success = false, Error = $"DataType 非法: {request.DataType}" });

        var existing = await repo.GetAsync(request.TagId, ct);
        if (existing is not null)
            return Results.BadRequest(new ApiResponse<TagDto> { Success = false, Error = $"TagId 已存在: {request.TagId}" });

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var tag = new TagDto
        {
            TagId = request.TagId,
            DeviceId = request.DeviceId,
            Name = request.Name,
            Description = request.Description,
            Unit = request.Unit,
            DataType = (TagValueType)request.DataType,
            Enabled = request.Enabled,
            Address = request.Address,
            ScanIntervalMs = request.ScanIntervalMs,
            TagGroup = request.TagGroup,
            Metadata = request.Metadata,
            CreatedUtc = now,
            UpdatedUtc = now
        };

        await repo.UpsertAsync(tag, ct);

        // v56.1: 使缓存失效
        cache.InvalidateTags(request.DeviceId);

        Log.Information("Created tag {TagId} for device {DeviceId}", request.TagId, request.DeviceId);

        // 递增配置版本号
        await revisionProvider.IncrementRevisionAsync(ct);

        // 审计日志
        await AuditLogHelper.LogAsync(auditRepo, httpContext, "tag.create", "tag",
            request.TagId, $"Created tag: {request.Name ?? request.TagId}", ct);

        var saved = await repo.GetAsync(request.TagId, ct);
        return Results.Ok(new ApiResponse<TagDto> { Data = saved ?? tag });
    }

    private static async Task<IResult> UpdateAsync(
        [FromServices] ITagRepository repo,
        [FromServices] IAuditLogRepository auditRepo,
        [FromServices] IConfigRevisionProvider revisionProvider,
        [FromServices] CacheService cache,
        HttpContext httpContext,
        [FromRoute] string tagId,
        [FromBody] UpdateTagRequest request,
        CancellationToken ct)
    {
        // P1: 使用 InputValidator 验证
        var idValidation = InputValidator.ValidateIdentifier(tagId, "tagId");
        if (!idValidation.IsValid)
            return Results.BadRequest(new ApiResponse<TagDto> { Success = false, Error = idValidation.Error });

        if (request is null)
            return Results.BadRequest(new ApiResponse<TagDto> { Success = false, Error = "请求体不能为空" });

        var nameValidation = InputValidator.ValidateOptionalDisplayName(request.Name, "标签名称");
        if (!nameValidation.IsValid)
            return Results.BadRequest(new ApiResponse<TagDto> { Success = false, Error = nameValidation.Error });

        var descValidation = InputValidator.ValidateDescription(request.Description);
        if (!descValidation.IsValid)
            return Results.BadRequest(new ApiResponse<TagDto> { Success = false, Error = descValidation.Error });

        var existing = await repo.GetAsync(tagId, ct);
        if (existing is null)
            return Results.NotFound(new ApiResponse<TagDto> { Success = false, Error = $"标签不存在: {tagId}" });

        if (request.DataType.HasValue && !Enum.IsDefined(typeof(TagValueType), request.DataType.Value))
            return Results.BadRequest(new ApiResponse<TagDto> { Success = false, Error = $"DataType 非法: {request.DataType.Value}" });

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var updated = existing with
        {
            Name = request.Name ?? existing.Name,
            Description = request.Description ?? existing.Description,
            Unit = request.Unit ?? existing.Unit,
            DataType = request.DataType.HasValue ? (TagValueType)request.DataType.Value : existing.DataType,
            Enabled = request.Enabled ?? existing.Enabled,
            Address = request.Address ?? existing.Address,
            ScanIntervalMs = request.ScanIntervalMs ?? existing.ScanIntervalMs,
            TagGroup = request.TagGroup ?? existing.TagGroup,
            Metadata = request.Metadata ?? existing.Metadata,
            UpdatedUtc = now
        };

        await repo.UpsertAsync(updated, ct);

        // v56.1: 使缓存失效
        cache.InvalidateTags(updated.DeviceId);

        Log.Information("Updated tag {TagId}", tagId);

        // 递增配置版本号
        await revisionProvider.IncrementRevisionAsync(ct);

        // 审计日志
        await AuditLogHelper.LogAsync(auditRepo, httpContext, "tag.update", "tag",
            tagId, $"Updated tag: {updated.Name ?? tagId}", ct);

        var saved = await repo.GetAsync(tagId, ct);
        return Results.Ok(new ApiResponse<TagDto> { Data = saved ?? updated });
    }

    private static async Task<IResult> DeleteAsync(
        [FromServices] ITagRepository repo,
        [FromServices] IAuditLogRepository auditRepo,
        [FromServices] IConfigRevisionProvider revisionProvider,
        [FromServices] CacheService cache,
        HttpContext httpContext,
        [FromRoute] string tagId,
        CancellationToken ct)
    {
        // P1: 使用 InputValidator 验证
        var idValidation = InputValidator.ValidateIdentifier(tagId, "tagId");
        if (!idValidation.IsValid)
            return Results.BadRequest(new ApiResponse<object> { Success = false, Error = idValidation.Error });

        var existing = await repo.GetAsync(tagId, ct);
        if (existing is null)
            return Results.NotFound(new ApiResponse<object> { Success = false, Error = $"标签不存在: {tagId}" });

        await repo.DeleteAsync(tagId, ct);

        // v56.1: 使缓存失效
        cache.InvalidateTags(existing.DeviceId);

        Log.Information("Deleted tag {TagId}", tagId);

        // 递增配置版本号
        await revisionProvider.IncrementRevisionAsync(ct);

        // 审计日志
        await AuditLogHelper.LogAsync(auditRepo, httpContext, "tag.delete", "tag",
            tagId, "Tag deleted", ct);

        return Results.Ok(new ApiResponse<object> { Data = null });
    }
}

public sealed record CreateTagRequest
{
    public required string TagId { get; init; }
    public required string DeviceId { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Unit { get; init; }
    public required int DataType { get; init; }
    public bool Enabled { get; init; } = true;
    public string? Address { get; init; }
    public int? ScanIntervalMs { get; init; }
    public string? TagGroup { get; init; }
    public Dictionary<string, string>? Metadata { get; init; }
}

public sealed record UpdateTagRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Unit { get; init; }
    public int? DataType { get; init; }
    public bool? Enabled { get; init; }
    public string? Address { get; init; }
    public int? ScanIntervalMs { get; init; }
    public string? TagGroup { get; init; }
    public Dictionary<string, string>? Metadata { get; init; }
}
