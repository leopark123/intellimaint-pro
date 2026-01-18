using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Application.Services;

// Note: ITokenService 已移至 Core.Abstractions.ITokenService

/// <summary>
/// P1: 认证服务接口 - 集中认证业务逻辑
/// </summary>
public interface IAuthService
{
    /// <summary>用户登录</summary>
    Task<AuthResult> LoginAsync(string username, string password, string clientIp, CancellationToken ct);

    /// <summary>刷新 Token</summary>
    Task<AuthResult> RefreshTokenAsync(string refreshToken, CancellationToken ct);

    /// <summary>用户登出</summary>
    Task LogoutAsync(string userId, CancellationToken ct);
}

/// <summary>
/// 认证结果
/// </summary>
public sealed record AuthResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public int? StatusCode { get; init; }
    public LoginResponse? LoginResponse { get; init; }
    public UserDto? User { get; init; }

    public static AuthResult Failed(string error, int statusCode = 401)
        => new() { Success = false, Error = error, StatusCode = statusCode };

    public static AuthResult Succeeded(LoginResponse loginResponse, UserDto user)
        => new() { Success = true, LoginResponse = loginResponse, User = user };
}

/// <summary>
/// P1: 认证服务实现
/// </summary>
public sealed class AuthService : IAuthService
{
    private readonly IUserRepository _userRepo;
    private readonly ITokenService _tokenService;
    private readonly ILogger<AuthService> _logger;

    // P1: 常量化魔法数字
    private const int MaxLoginAttempts = 5;
    private const int LockoutMinutes = 15;

    public AuthService(
        IUserRepository userRepo,
        ITokenService tokenService,
        ILogger<AuthService> logger)
    {
        _userRepo = userRepo;
        _tokenService = tokenService;
        _logger = logger;
    }

    public async Task<AuthResult> LoginAsync(string username, string password, string clientIp, CancellationToken ct)
    {
        // 检查账号是否被锁定
        var (isLocked, remainingMinutes) = await _userRepo.CheckLockoutAsync(username, ct);
        if (isLocked)
        {
            _logger.LogWarning("Login blocked for locked account: {Username} from IP: {ClientIp}", username, clientIp);
            return AuthResult.Failed($"账号已锁定，请 {remainingMinutes} 分钟后重试", 429);
        }

        // 验证凭据
        var user = await _userRepo.ValidateCredentialsAsync(username, password, ct);

        if (user == null)
        {
            var failedCount = await _userRepo.GetFailedLoginCountAsync(username, ct);
            var remainingAttempts = Math.Max(0, MaxLoginAttempts - failedCount);

            _logger.LogWarning("Login failed for user: {Username} from IP: {ClientIp}, remaining attempts: {Remaining}",
                username, clientIp, remainingAttempts);

            if (remainingAttempts <= 0)
            {
                return AuthResult.Failed($"账号已锁定，请 {LockoutMinutes} 分钟后重试", 429);
            }

            return AuthResult.Failed($"用户名或密码错误，剩余尝试次数: {remainingAttempts}", 401);
        }

        // 更新最后登录时间
        await _userRepo.UpdateLastLoginAsync(user.UserId, ct);

        // 生成 Token
        var (loginResponse, refreshExpiresUtc) = _tokenService.GenerateTokens(user);

        // 保存 Refresh Token
        await _userRepo.SaveRefreshTokenAsync(user.UserId, loginResponse.RefreshToken, refreshExpiresUtc, ct);

        _logger.LogInformation("User logged in: {Username} from IP: {ClientIp}", user.Username, clientIp);

        return AuthResult.Succeeded(loginResponse, user);
    }

    public async Task<AuthResult> RefreshTokenAsync(string refreshToken, CancellationToken ct)
    {
        // 通过 Refresh Token 获取用户
        var user = await _userRepo.GetByRefreshTokenAsync(refreshToken, ct);

        if (user == null)
        {
            _logger.LogWarning("Token refresh failed: invalid or expired refresh token");
            return AuthResult.Failed("Invalid or expired refresh token", 401);
        }

        // 生成新的 Token（Token Rotation）
        var (loginResponse, refreshExpiresUtc) = _tokenService.GenerateTokens(user);

        // 保存新的 Refresh Token
        await _userRepo.SaveRefreshTokenAsync(user.UserId, loginResponse.RefreshToken, refreshExpiresUtc, ct);

        _logger.LogInformation("Token refreshed for user: {Username}", user.Username);

        return AuthResult.Succeeded(loginResponse, user);
    }

    public async Task LogoutAsync(string userId, CancellationToken ct)
    {
        await _userRepo.ClearRefreshTokenAsync(userId, ct);
        _logger.LogInformation("User logged out: {UserId}", userId);
    }
}
