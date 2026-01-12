using IntelliMaint.Application.Services;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using IntelliMaint.Host.Api.Services;
using IntelliMaint.Host.Api.Validators;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace IntelliMaint.Host.Api.Endpoints;

/// <summary>
/// P1: 用户管理端点 - 业务逻辑已提取到 IUserService
/// </summary>
public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users")
            .WithTags("Users")
            .RequireAuthorization();

        // Admin 操作
        group.MapGet("/", ListUsersAsync).RequireAuthorization(AuthPolicies.AdminOnly);
        group.MapGet("/{userId}", GetUserAsync).RequireAuthorization(AuthPolicies.AdminOnly);
        group.MapPost("/", CreateUserAsync).RequireAuthorization(AuthPolicies.AdminOnly);
        group.MapPut("/{userId}", UpdateUserAsync).RequireAuthorization(AuthPolicies.AdminOnly);
        group.MapDelete("/{userId}", DisableUserAsync).RequireAuthorization(AuthPolicies.AdminOnly);
        group.MapPost("/{userId}/reset-password", ResetPasswordAsync).RequireAuthorization(AuthPolicies.AdminOnly);

        // 用户自己操作
        group.MapPut("/password", ChangePasswordAsync);
        group.MapGet("/me", GetCurrentUserAsync);
    }

    private static async Task<IResult> ListUsersAsync(
        [FromServices] IUserService userService,
        CancellationToken ct)
    {
        var users = await userService.ListAsync(ct);
        return Results.Ok(new { success = true, data = users });
    }

    private static async Task<IResult> GetUserAsync(
        string userId,
        [FromServices] IUserService userService,
        CancellationToken ct)
    {
        var result = await userService.GetByIdAsync(userId, ct);
        return ToResult(result);
    }

    private static async Task<IResult> CreateUserAsync(
        [FromBody] CreateUserRequest request,
        HttpContext httpContext,
        [FromServices] IUserService userService,
        [FromServices] AuditService auditService,
        CancellationToken ct)
    {
        // 输入验证
        var usernameValidation = InputValidator.ValidateUsername(request.Username);
        if (!usernameValidation.IsValid)
        {
            return Results.BadRequest(new { success = false, error = usernameValidation.Error });
        }

        var passwordValidation = InputValidator.ValidatePassword(request.Password);
        if (!passwordValidation.IsValid)
        {
            return Results.BadRequest(new { success = false, error = passwordValidation.Error });
        }

        var displayNameValidation = InputValidator.ValidateOptionalDisplayName(request.DisplayName, "显示名");
        if (!displayNameValidation.IsValid)
        {
            return Results.BadRequest(new { success = false, error = displayNameValidation.Error });
        }

        // 调用服务
        var result = await userService.CreateAsync(request.Username, request.Password, request.Role, request.DisplayName, ct);

        // 审计日志
        if (result.Success)
        {
            await auditService.LogAsync("CreateUser", "User", result.User!.UserId,
                $"创建用户: {result.User.Username}, 角色: {result.User.Role}", ct);
            return Results.Created($"/api/users/{result.User.UserId}", new { success = true, data = result.User });
        }

        return ToResult(result);
    }

    private static async Task<IResult> UpdateUserAsync(
        string userId,
        [FromBody] UpdateUserRequest request,
        [FromServices] IUserService userService,
        [FromServices] AuditService auditService,
        CancellationToken ct)
    {
        var result = await userService.UpdateAsync(userId, request.DisplayName, request.Role, request.Enabled, ct);

        if (result.Success)
        {
            var changes = new List<string>();
            if (request.DisplayName != null) changes.Add($"显示名: {request.DisplayName}");
            if (request.Role != null) changes.Add($"角色: {request.Role}");
            if (request.Enabled.HasValue) changes.Add($"启用: {request.Enabled}");

            await auditService.LogAsync("UpdateUser", "User", userId,
                $"修改用户, 变更: {string.Join(", ", changes)}", ct);
        }

        return ToResult(result);
    }

    private static async Task<IResult> DisableUserAsync(
        string userId,
        HttpContext httpContext,
        [FromServices] IUserService userService,
        [FromServices] AuditService auditService,
        [FromServices] TokenBlacklistService blacklistService,
        CancellationToken ct)
    {
        var operatorId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

        var result = await userService.DisableAsync(userId, operatorId, ct);

        if (result.Success)
        {
            // 将用户添加到黑名单，立即使其所有 Token 失效
            blacklistService.BlacklistUser(userId, "User disabled by admin");

            await auditService.LogAsync("DisableUser", "User", userId, "禁用用户", ct);
            return Results.Ok(new { success = true, message = "用户已禁用" });
        }

        return ToResult(result);
    }

    private static async Task<IResult> ResetPasswordAsync(
        string userId,
        [FromBody] ResetPasswordRequest request,
        [FromServices] IUserService userService,
        [FromServices] AuditService auditService,
        CancellationToken ct)
    {
        // 输入验证
        var passwordValidation = InputValidator.ValidatePassword(request.NewPassword);
        if (!passwordValidation.IsValid)
        {
            return Results.BadRequest(new { success = false, error = passwordValidation.Error });
        }

        var result = await userService.ResetPasswordAsync(userId, request.NewPassword, ct);

        if (result.Success)
        {
            await auditService.LogAsync("ResetPassword", "User", userId, "重置用户密码", ct);
            return Results.Ok(new { success = true, message = "密码已重置" });
        }

        return ToResult(result);
    }

    private static async Task<IResult> ChangePasswordAsync(
        [FromBody] ChangePasswordRequest request,
        HttpContext httpContext,
        [FromServices] IUserService userService,
        [FromServices] AuditService auditService,
        CancellationToken ct)
    {
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
        {
            return Results.BadRequest(new { success = false, error = "当前密码不能为空" });
        }

        var passwordValidation = InputValidator.ValidatePassword(request.NewPassword);
        if (!passwordValidation.IsValid)
        {
            return Results.BadRequest(new { success = false, error = passwordValidation.Error });
        }

        var result = await userService.ChangePasswordAsync(userId, request.CurrentPassword, request.NewPassword, ct);

        if (result.Success)
        {
            await auditService.LogAsync("ChangePassword", "User", userId, "用户修改密码", ct);
            return Results.Ok(new { success = true, message = "密码已修改" });
        }

        return ToResult(result);
    }

    private static async Task<IResult> GetCurrentUserAsync(
        HttpContext httpContext,
        [FromServices] IUserService userService,
        CancellationToken ct)
    {
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        var result = await userService.GetByIdAsync(userId, ct);
        return ToResult(result);
    }

    /// <summary>
    /// 将 UserResult 转换为 IResult
    /// </summary>
    private static IResult ToResult(UserResult result)
    {
        if (result.Success)
        {
            return Results.Ok(new { success = true, data = result.User });
        }

        return result.StatusCode switch
        {
            404 => Results.NotFound(new { success = false, error = result.Error }),
            409 => Results.Conflict(new { success = false, error = result.Error }),
            500 => Results.Problem(result.Error),
            _ => Results.BadRequest(new { success = false, error = result.Error })
        };
    }
}
