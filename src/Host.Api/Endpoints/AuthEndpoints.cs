using IntelliMaint.Application.Services;
using IntelliMaint.Core.Contracts;
using IntelliMaint.Host.Api.Services;
using IntelliMaint.Host.Api.Validators;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace IntelliMaint.Host.Api.Endpoints;

/// <summary>
/// P1: 认证端点 - 业务逻辑已提取到 IAuthService
/// </summary>
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth")
            .WithTags("Authentication");

        group.MapPost("/login", LoginAsync).AllowAnonymous();
        group.MapPost("/refresh", RefreshAsync).AllowAnonymous();
        group.MapPost("/logout", LogoutAsync).RequireAuthorization();
    }

    private static async Task<IResult> LoginAsync(
        HttpContext httpContext,
        [FromBody] LoginRequest request,
        [FromServices] IAuthService authService,
        [FromServices] AuditService auditService,
        CancellationToken ct)
    {
        // 输入验证
        var usernameValidation = InputValidator.ValidateUsername(request.Username);
        if (!usernameValidation.IsValid)
        {
            return Results.BadRequest(new { success = false, error = usernameValidation.Error });
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.BadRequest(new { success = false, error = "密码不能为空" });
        }

        var clientIp = GetClientIp(httpContext);

        // 调用 AuthService 处理登录业务逻辑
        var result = await authService.LoginAsync(request.Username, request.Password, clientIp, ct);

        // 记录审计日志
        if (result.Success)
        {
            await auditService.LogLoginAsync(result.User!.UserId, result.User.Username, true, null, ct);
            return Results.Ok(new { success = true, data = result.LoginResponse });
        }
        else
        {
            await auditService.LogLoginAsync("anonymous", request.Username, false, result.Error, ct);
            return Results.Json(new { success = false, error = result.Error }, statusCode: result.StatusCode ?? 401);
        }
    }

    private static async Task<IResult> RefreshAsync(
        HttpContext httpContext,
        [FromBody] RefreshTokenRequest request,
        [FromServices] IAuthService authService,
        [FromServices] AuditService auditService,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Results.BadRequest(new { success = false, error = "Refresh Token 不能为空" });
        }

        // 调用 AuthService 处理 Token 刷新
        var result = await authService.RefreshTokenAsync(request.RefreshToken, ct);

        if (result.Success)
        {
            await auditService.LogAsync(AuditActions.TokenRefresh, "Auth", null, "Token 刷新成功", ct);
            return Results.Ok(new { success = true, data = result.LoginResponse });
        }
        else
        {
            return Results.Unauthorized();
        }
    }

    private static async Task<IResult> LogoutAsync(
        [FromServices] IAuthService authService,
        [FromServices] AuditService auditService,
        [FromServices] TokenBlacklistService blacklistService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!string.IsNullOrEmpty(userId))
        {
            // 将用户添加到黑名单，立即使其所有 Token 失效
            blacklistService.BlacklistUser(userId, "User logged out");

            await authService.LogoutAsync(userId, ct);
            await auditService.LogLogoutAsync(ct);
        }

        return Results.Ok(new { success = true, message = "已登出" });
    }

    private static string GetClientIp(HttpContext context)
    {
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',')[0].Trim();
        }
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
