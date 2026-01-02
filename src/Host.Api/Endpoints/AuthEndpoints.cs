using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using IntelliMaint.Host.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System.Security.Claims;

namespace IntelliMaint.Host.Api.Endpoints;

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
        [FromServices] IUserRepository userRepo,
        [FromServices] JwtService jwtService,
        [FromServices] AuditService auditService,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.BadRequest(new { success = false, error = "用户名和密码不能为空" });
        }

        var clientIp = GetClientIp(httpContext);
        
        // P0-2: 检查账号是否被锁定
        var (isLocked, remainingMinutes) = await userRepo.CheckLockoutAsync(request.Username, ct);
        if (isLocked)
        {
            Log.Warning("Login blocked for locked account: {Username} from IP: {ClientIp}", request.Username, clientIp);
            await auditService.LogLoginAsync("anonymous", request.Username, false, $"账号已锁定，剩余 {remainingMinutes} 分钟", ct);
            return Results.Json(new { success = false, error = $"账号已锁定，请 {remainingMinutes} 分钟后重试" }, statusCode: 429);
        }
        
        var user = await userRepo.ValidateCredentialsAsync(request.Username, request.Password, ct);

        if (user == null)
        {
            // 获取失败次数用于提示
            var failedCount = await userRepo.GetFailedLoginCountAsync(request.Username, ct);
            var remainingAttempts = Math.Max(0, 5 - failedCount);
            
            Log.Warning("Login failed for user: {Username} from IP: {ClientIp}, remaining attempts: {Remaining}", 
                request.Username, clientIp, remainingAttempts);

            // v44: 使用 AuditService 记录登录失败
            await auditService.LogLoginAsync("anonymous", request.Username, false, 
                $"密码错误或用户不存在，剩余尝试次数: {remainingAttempts}", ct);

            if (remainingAttempts <= 0)
            {
                return Results.Json(new { success = false, error = "账号已锁定，请 15 分钟后重试" }, statusCode: 429);
            }
            
            return Results.Json(new { success = false, error = $"用户名或密码错误，剩余尝试次数: {remainingAttempts}" }, statusCode: 401);
        }

        await userRepo.UpdateLastLoginAsync(user.UserId, ct);
        
        // 生成 Access Token 和 Refresh Token
        var (response, refreshExpiresUtc) = jwtService.GenerateTokens(user);
        
        // 保存 Refresh Token 到数据库
        await userRepo.SaveRefreshTokenAsync(user.UserId, response.RefreshToken, refreshExpiresUtc, ct);

        Log.Information("User logged in: {Username} from IP: {ClientIp}", user.Username, clientIp);

        // v44: 使用 AuditService 记录登录成功
        await auditService.LogLoginAsync(user.UserId, user.Username, true, null, ct);

        return Results.Ok(new { success = true, data = response });
    }

    private static async Task<IResult> RefreshAsync(
        HttpContext httpContext,
        [FromBody] RefreshTokenRequest request,
        [FromServices] IUserRepository userRepo,
        [FromServices] JwtService jwtService,
        [FromServices] AuditService auditService,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Results.BadRequest(new { success = false, error = "Refresh Token 不能为空" });
        }

        // 通过 Refresh Token 获取用户
        var user = await userRepo.GetByRefreshTokenAsync(request.RefreshToken, ct);

        if (user == null)
        {
            Log.Warning("Token refresh failed: invalid or expired refresh token from IP: {ClientIp}", 
                GetClientIp(httpContext));
            return Results.Unauthorized();
        }

        // 生成新的 Access Token 和 Refresh Token（Token Rotation）
        var (response, refreshExpiresUtc) = jwtService.GenerateTokens(user);
        
        // 保存新的 Refresh Token（旧的自动失效）
        await userRepo.SaveRefreshTokenAsync(user.UserId, response.RefreshToken, refreshExpiresUtc, ct);

        Log.Information("Token refreshed for user: {Username}", user.Username);

        // v44: 记录 Token 刷新
        await auditService.LogAsync(AuditActions.TokenRefresh, "Auth", null, "Token 刷新成功", ct);

        return Results.Ok(new { success = true, data = response });
    }

    private static async Task<IResult> LogoutAsync(
        [FromServices] IUserRepository userRepo,
        [FromServices] AuditService auditService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var userName = httpContext.User.FindFirst(ClaimTypes.Name)?.Value ?? "unknown";

        if (!string.IsNullOrEmpty(userId))
        {
            // 清除 Refresh Token
            await userRepo.ClearRefreshTokenAsync(userId, ct);

            Log.Information("User logged out: {Username}", userName);

            // v44: 使用 AuditService 记录登出
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
