using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System.Security.Claims;

namespace IntelliMaint.Host.Api.Endpoints;

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
        [FromServices] IUserRepository userRepo,
        CancellationToken ct)
    {
        var users = await userRepo.ListAsync(ct);
        return Results.Ok(new { success = true, data = users });
    }

    private static async Task<IResult> GetUserAsync(
        string userId,
        [FromServices] IUserRepository userRepo,
        CancellationToken ct)
    {
        var user = await userRepo.GetByIdAsync(userId, ct);
        if (user == null)
        {
            return Results.NotFound(new { success = false, error = "用户不存在" });
        }
        return Results.Ok(new { success = true, data = user });
    }

    private static async Task<IResult> CreateUserAsync(
        [FromBody] CreateUserRequest request,
        HttpContext httpContext,
        [FromServices] IUserRepository userRepo,
        [FromServices] IAuditLogRepository auditRepo,
        CancellationToken ct)
    {
        // 验证输入
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            return Results.BadRequest(new { success = false, error = "用户名不能为空" });
        }
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
        {
            return Results.BadRequest(new { success = false, error = "密码长度至少6位" });
        }
        if (!UserRoles.IsValid(request.Role))
        {
            return Results.BadRequest(new { success = false, error = $"无效角色，可选值: {string.Join(", ", UserRoles.All)}" });
        }

        // 检查用户名是否已存在
        var existing = await userRepo.GetByUsernameAsync(request.Username, ct);
        if (existing != null)
        {
            return Results.Conflict(new { success = false, error = "用户名已存在" });
        }

        var user = await userRepo.CreateAsync(request.Username, request.Password, request.Role, request.DisplayName, ct);
        if (user == null)
        {
            return Results.Problem("创建用户失败");
        }

        Log.Information("User created: {Username} by {Operator}", user.Username, httpContext.User.Identity?.Name);

        // 审计日志
        var operatorId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "system";
        var operatorName = httpContext.User.FindFirst(ClaimTypes.Name)?.Value ?? "system";
        await auditRepo.CreateAsync(new AuditLogEntry
        {
            Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UserId = operatorId,
            UserName = operatorName,
            Action = "CreateUser",
            ResourceType = "User",
            ResourceId = user.UserId,
            Details = $"创建用户: {user.Username}, 角色: {user.Role}"
        }, ct);

        return Results.Created($"/api/users/{user.UserId}", new { success = true, data = user });
    }

    private static async Task<IResult> UpdateUserAsync(
        string userId,
        [FromBody] UpdateUserRequest request,
        HttpContext httpContext,
        [FromServices] IUserRepository userRepo,
        [FromServices] IAuditLogRepository auditRepo,
        CancellationToken ct)
    {
        var existing = await userRepo.GetByIdAsync(userId, ct);
        if (existing == null)
        {
            return Results.NotFound(new { success = false, error = "用户不存在" });
        }

        // 验证角色
        if (request.Role != null && !UserRoles.IsValid(request.Role))
        {
            return Results.BadRequest(new { success = false, error = $"无效角色，可选值: {string.Join(", ", UserRoles.All)}" });
        }

        var user = await userRepo.UpdateAsync(userId, request.DisplayName, request.Role, request.Enabled, ct);

        Log.Information("User updated: {UserId} by {Operator}", userId, httpContext.User.Identity?.Name);

        // 审计日志
        var operatorId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "system";
        var operatorName = httpContext.User.FindFirst(ClaimTypes.Name)?.Value ?? "system";
        
        var changes = new List<string>();
        if (request.DisplayName != null) changes.Add($"显示名: {request.DisplayName}");
        if (request.Role != null) changes.Add($"角色: {request.Role}");
        if (request.Enabled.HasValue) changes.Add($"启用: {request.Enabled}");

        await auditRepo.CreateAsync(new AuditLogEntry
        {
            Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UserId = operatorId,
            UserName = operatorName,
            Action = "UpdateUser",
            ResourceType = "User",
            ResourceId = userId,
            Details = $"修改用户: {existing.Username}, 变更: {string.Join(", ", changes)}"
        }, ct);

        return Results.Ok(new { success = true, data = user });
    }

    private static async Task<IResult> DisableUserAsync(
        string userId,
        HttpContext httpContext,
        [FromServices] IUserRepository userRepo,
        [FromServices] IAuditLogRepository auditRepo,
        CancellationToken ct)
    {
        var currentUserId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        
        // 不能禁用自己
        if (userId == currentUserId)
        {
            return Results.BadRequest(new { success = false, error = "不能禁用自己" });
        }

        var existing = await userRepo.GetByIdAsync(userId, ct);
        if (existing == null)
        {
            return Results.NotFound(new { success = false, error = "用户不存在" });
        }

        var success = await userRepo.DisableAsync(userId, ct);
        if (!success)
        {
            return Results.Problem("禁用用户失败");
        }

        Log.Information("User disabled: {UserId} by {Operator}", userId, httpContext.User.Identity?.Name);

        // 审计日志
        var operatorId = currentUserId ?? "system";
        var operatorName = httpContext.User.FindFirst(ClaimTypes.Name)?.Value ?? "system";
        await auditRepo.CreateAsync(new AuditLogEntry
        {
            Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UserId = operatorId,
            UserName = operatorName,
            Action = "DisableUser",
            ResourceType = "User",
            ResourceId = userId,
            Details = $"禁用用户: {existing.Username}"
        }, ct);

        return Results.Ok(new { success = true, message = "用户已禁用" });
    }

    private static async Task<IResult> ResetPasswordAsync(
        string userId,
        [FromBody] ResetPasswordRequest request,
        HttpContext httpContext,
        [FromServices] IUserRepository userRepo,
        [FromServices] IAuditLogRepository auditRepo,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
        {
            return Results.BadRequest(new { success = false, error = "新密码长度至少6位" });
        }

        var existing = await userRepo.GetByIdAsync(userId, ct);
        if (existing == null)
        {
            return Results.NotFound(new { success = false, error = "用户不存在" });
        }

        var success = await userRepo.ResetPasswordAsync(userId, request.NewPassword, ct);
        if (!success)
        {
            return Results.Problem("重置密码失败");
        }

        Log.Information("Password reset for user: {UserId} by {Operator}", userId, httpContext.User.Identity?.Name);

        // 审计日志
        var operatorId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "system";
        var operatorName = httpContext.User.FindFirst(ClaimTypes.Name)?.Value ?? "system";
        await auditRepo.CreateAsync(new AuditLogEntry
        {
            Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UserId = operatorId,
            UserName = operatorName,
            Action = "ResetPassword",
            ResourceType = "User",
            ResourceId = userId,
            Details = $"重置用户密码: {existing.Username}"
        }, ct);

        return Results.Ok(new { success = true, message = "密码已重置" });
    }

    private static async Task<IResult> ChangePasswordAsync(
        [FromBody] ChangePasswordRequest request,
        HttpContext httpContext,
        [FromServices] IUserRepository userRepo,
        [FromServices] IAuditLogRepository auditRepo,
        CancellationToken ct)
    {
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var userName = httpContext.User.FindFirst(ClaimTypes.Name)?.Value ?? "unknown";

        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
        {
            return Results.BadRequest(new { success = false, error = "当前密码不能为空" });
        }
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
        {
            return Results.BadRequest(new { success = false, error = "新密码长度至少6位" });
        }

        var success = await userRepo.UpdatePasswordAsync(userId, request.CurrentPassword, request.NewPassword, ct);
        if (!success)
        {
            return Results.BadRequest(new { success = false, error = "当前密码错误" });
        }

        Log.Information("Password changed for user: {Username}", userName);

        // 审计日志
        await auditRepo.CreateAsync(new AuditLogEntry
        {
            Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UserId = userId,
            UserName = userName,
            Action = "ChangePassword",
            ResourceType = "User",
            ResourceId = userId,
            Details = "用户修改密码"
        }, ct);

        return Results.Ok(new { success = true, message = "密码已修改" });
    }

    private static async Task<IResult> GetCurrentUserAsync(
        HttpContext httpContext,
        [FromServices] IUserRepository userRepo,
        CancellationToken ct)
    {
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        var user = await userRepo.GetByIdAsync(userId, ct);
        if (user == null)
        {
            return Results.NotFound(new { success = false, error = "用户不存在" });
        }

        return Results.Ok(new { success = true, data = user });
    }
}
