using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace IntelliMaint.Host.Api.Services;

public sealed class JwtService
{
    private readonly string _secretKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _accessTokenMinutes;
    private readonly int _refreshTokenDays;

    public JwtService(IConfiguration configuration)
    {
        var jwtSection = configuration.GetSection("Jwt");
        
        // v43: 优先从环境变量读取密钥
        _secretKey = Environment.GetEnvironmentVariable("JWT_SECRET_KEY")
            ?? jwtSection["SecretKey"] 
            ?? throw new InvalidOperationException("JWT_SECRET_KEY env or Jwt:SecretKey config is required");
        
        _issuer = jwtSection["Issuer"] ?? "IntelliMaint";
        _audience = jwtSection["Audience"] ?? "IntelliMaint";
        _accessTokenMinutes = int.TryParse(jwtSection["AccessTokenMinutes"], out var a) ? a : 15;
        _refreshTokenDays = int.TryParse(jwtSection["RefreshTokenDays"], out var r) ? r : 7;
    }

    /// <summary>
    /// 生成 Access Token 和 Refresh Token
    /// </summary>
    public (LoginResponse Response, long RefreshTokenExpiresUtc) GenerateTokens(UserDto user)
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
            RefreshExpiresAt = refreshExpiresUtc
        };

        return (response, refreshExpiresUtc);
    }

    private (string Token, long ExpiresAt) GenerateAccessToken(UserDto user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(_accessTokenMinutes);

        var claims = new[]
        {
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
