using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using IntelliMaint.Application.Services;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace IntelliMaint.Infrastructure.Security;

/// <summary>
/// P2: JWT 服务 - 从 Host.Api 移至 Infrastructure 层
/// 符合洁净架构：基础设施服务不应放在宿主层
/// P1: 实现 ITokenService 接口
/// </summary>
public sealed class JwtService : ITokenService
{
    private const int MinSecretKeyLength = 32;
    private const string DefaultInsecureKeyPrefix = "IntelliMaint-Pro-Secret-Key";

    private readonly string _secretKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _accessTokenMinutes;
    private readonly int _refreshTokenDays;
    private readonly ILogger<JwtService>? _logger;

    public JwtService(IConfiguration configuration, ILogger<JwtService>? logger = null)
    {
        _logger = logger;
        var jwtSection = configuration.GetSection("Jwt");

        // v56.1: 优先从环境变量读取密钥，增强安全性验证
        var envKey = Environment.GetEnvironmentVariable("JWT_SECRET_KEY");
        var configKey = jwtSection["SecretKey"];

        _secretKey = envKey ?? configKey
            ?? throw new InvalidOperationException("JWT_SECRET_KEY environment variable or Jwt:SecretKey config is required");

        // v56.1: 验证密钥强度
        ValidateSecretKey(_secretKey, envKey != null);

        _issuer = jwtSection["Issuer"] ?? "IntelliMaint";
        _audience = jwtSection["Audience"] ?? "IntelliMaint";
        _accessTokenMinutes = int.TryParse(jwtSection["AccessTokenMinutes"], out var a) ? a : 15;
        _refreshTokenDays = int.TryParse(jwtSection["RefreshTokenDays"], out var r) ? r : 7;
    }

    /// <summary>
    /// v56.1: 验证密钥强度，生产环境必须使用安全密钥
    /// </summary>
    private void ValidateSecretKey(string key, bool fromEnvironment)
    {
        if (key.Length < MinSecretKeyLength)
        {
            throw new InvalidOperationException(
                $"JWT secret key must be at least {MinSecretKeyLength} characters. Current: {key.Length}");
        }

        // 检查是否使用默认不安全密钥
        if (key.StartsWith(DefaultInsecureKeyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var isDevelopment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
            if (isDevelopment)
            {
                _logger?.LogWarning(
                    "[Security] Using default JWT key in Development. Set JWT_SECRET_KEY env var for production!");
            }
            else
            {
                throw new InvalidOperationException(
                    "Default JWT secret key detected in non-Development environment. " +
                    "Set JWT_SECRET_KEY environment variable with a secure random key (64+ chars recommended).");
            }
        }
        else if (!fromEnvironment)
        {
            _logger?.LogWarning(
                "[Security] JWT key loaded from config file. Consider using JWT_SECRET_KEY environment variable.");
        }
    }

    /// <summary>
    /// 生成 Access Token 和 Refresh Token
    /// </summary>
    public (LoginResponse Response, long RefreshExpiresUtc) GenerateTokens(UserDto user)
    {
        var (accessToken, accessExpiresAt) = GenerateAccessToken(user);
        var refreshToken = GenerateRefreshToken();
        var refreshExpiresUtc = DateTimeOffset.UtcNow.AddDays(_refreshTokenDays).ToUnixTimeMilliseconds();

        var response = new LoginResponse
        {
            Token = accessToken,
            RefreshToken = refreshToken,
            Username = user.Username,
            Role = user.Role,
            ExpiresAt = accessExpiresAt,
            RefreshExpiresAt = refreshExpiresUtc,
            MustChangePassword = user.MustChangePassword
        };

        return (response, refreshExpiresUtc);
    }

    private (string Token, long ExpiresAt) GenerateAccessToken(UserDto user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(_accessTokenMinutes);
        var issuedAt = new DateTimeOffset(now).ToUnixTimeSeconds();

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()), // JWT ID for blacklisting
            new Claim(JwtRegisteredClaimNames.Iat, issuedAt.ToString(), ClaimValueTypes.Integer64), // 签发时间（用于黑名单检查）
            new Claim(ClaimTypes.NameIdentifier, user.UserId),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim("display_name", user.DisplayName ?? user.Username)
        };

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: expires,
            signingCredentials: credentials
        );

        return (
            new JwtSecurityTokenHandler().WriteToken(token),
            new DateTimeOffset(expires).ToUnixTimeMilliseconds()
        );
    }

    private static string GenerateRefreshToken()
    {
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }

    /// <summary>
    /// 保留旧方法以兼容
    /// </summary>
    public LoginResponse GenerateToken(UserDto user)
    {
        var (response, _) = GenerateTokens(user);
        return response;
    }
}
