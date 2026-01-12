using FluentAssertions;
using IntelliMaint.Core.Contracts;
using IntelliMaint.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace IntelliMaint.Tests.Unit;

/// <summary>
/// v48: JWT 和认证相关测试
/// </summary>
public class JwtServiceTests
{
    private readonly JwtService _jwtService;

    public JwtServiceTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SecretKey"] = "ThisIsAVeryLongSecretKeyForTestingPurposesOnly123456",
                ["Jwt:Issuer"] = "IntelliMaint.Test",
                ["Jwt:Audience"] = "IntelliMaint.Test",
                ["Jwt:AccessTokenExpirationMinutes"] = "15",
                ["Jwt:RefreshTokenExpirationDays"] = "7"
            })
            .Build();

        _jwtService = new JwtService(config);
    }

    [Fact]
    public void GenerateTokens_ShouldReturnValidTokens()
    {
        // Arrange
        var user = new UserDto
        {
            UserId = "test-user-id",
            Username = "testuser",
            Role = UserRoles.Admin,
            Enabled = true,
            CreatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        // Act
        var (response, refreshExpiresUtc) = _jwtService.GenerateTokens(user);

        // Assert
        response.Token.Should().NotBeNullOrEmpty();
        response.RefreshToken.Should().NotBeNullOrEmpty();
        response.Username.Should().Be("testuser");
        response.Role.Should().Be(UserRoles.Admin);
        response.ExpiresAt.Should().BeGreaterThan(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        refreshExpiresUtc.Should().BeGreaterThan(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    [Fact]
    public void GenerateTokens_ShouldGenerateUniqueRefreshTokens()
    {
        // Arrange
        var user = new UserDto
        {
            UserId = "test-user-id",
            Username = "testuser",
            Role = UserRoles.Operator,
            Enabled = true,
            CreatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        // Act
        var (response1, _) = _jwtService.GenerateTokens(user);
        var (response2, _) = _jwtService.GenerateTokens(user);

        // Assert
        response1.RefreshToken.Should().NotBe(response2.RefreshToken);
    }

    [Fact]
    public void GenerateTokens_ShouldIncludeCorrectExpirationTimes()
    {
        // Arrange
        var user = new UserDto
        {
            UserId = "test-user-id",
            Username = "testuser",
            Role = UserRoles.Viewer,
            Enabled = true,
            CreatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        var now = DateTimeOffset.UtcNow;

        // Act
        var (response, refreshExpiresUtc) = _jwtService.GenerateTokens(user);

        // Assert
        // Access token should expire in ~15 minutes
        var accessExpires = DateTimeOffset.FromUnixTimeMilliseconds(response.ExpiresAt);
        accessExpires.Should().BeCloseTo(now.AddMinutes(15), TimeSpan.FromMinutes(1));

        // Refresh token should expire in ~7 days
        var refreshExpires = DateTimeOffset.FromUnixTimeMilliseconds(refreshExpiresUtc);
        refreshExpires.Should().BeCloseTo(now.AddDays(7), TimeSpan.FromMinutes(1));
    }
}

/// <summary>
/// v48: 密码哈希测试
/// </summary>
public class PasswordHashingTests
{
    [Fact]
    public void BCrypt_ShouldHashAndVerifyPassword()
    {
        // Arrange
        var password = "TestPassword123!";

        // Act
        var hash = BCrypt.Net.BCrypt.HashPassword(password, BCrypt.Net.BCrypt.GenerateSalt(12));
        var isValid = BCrypt.Net.BCrypt.Verify(password, hash);
        var isInvalid = BCrypt.Net.BCrypt.Verify("WrongPassword", hash);

        // Assert
        hash.Should().NotBeNullOrEmpty();
        hash.Should().NotBe(password);
        isValid.Should().BeTrue();
        isInvalid.Should().BeFalse();
    }

    [Fact]
    public void BCrypt_ShouldGenerateDifferentHashesForSamePassword()
    {
        // Arrange
        var password = "TestPassword123!";

        // Act
        var hash1 = BCrypt.Net.BCrypt.HashPassword(password, BCrypt.Net.BCrypt.GenerateSalt(12));
        var hash2 = BCrypt.Net.BCrypt.HashPassword(password, BCrypt.Net.BCrypt.GenerateSalt(12));

        // Assert - 每次哈希应该不同（因为盐值不同）
        hash1.Should().NotBe(hash2);
        
        // 但两个哈希都应该验证通过
        BCrypt.Net.BCrypt.Verify(password, hash1).Should().BeTrue();
        BCrypt.Net.BCrypt.Verify(password, hash2).Should().BeTrue();
    }

    [Fact]
    public void BCrypt_ShouldHandleEmptyPassword()
    {
        // Arrange
        var password = "";

        // Act
        var hash = BCrypt.Net.BCrypt.HashPassword(password, BCrypt.Net.BCrypt.GenerateSalt(12));
        var isValid = BCrypt.Net.BCrypt.Verify(password, hash);

        // Assert
        hash.Should().NotBeNullOrEmpty();
        isValid.Should().BeTrue();
    }

    [Fact]
    public void BCrypt_ShouldHandleUnicodePassword()
    {
        // Arrange
        var password = "密码123!@#中文";

        // Act
        var hash = BCrypt.Net.BCrypt.HashPassword(password, BCrypt.Net.BCrypt.GenerateSalt(12));
        var isValid = BCrypt.Net.BCrypt.Verify(password, hash);

        // Assert
        hash.Should().NotBeNullOrEmpty();
        isValid.Should().BeTrue();
    }
}

/// <summary>
/// v48: 授权策略测试
/// </summary>
public class AuthPoliciesTests
{
    [Fact]
    public void UserRoles_ShouldContainAllRoles()
    {
        // Assert
        UserRoles.All.Should().Contain(UserRoles.Admin);
        UserRoles.All.Should().Contain(UserRoles.Operator);
        UserRoles.All.Should().Contain(UserRoles.Viewer);
        UserRoles.All.Should().HaveCount(3);
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Operator", true)]
    [InlineData("Viewer", true)]
    [InlineData("admin", false)]  // 大小写敏感
    [InlineData("SuperAdmin", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void UserRoles_IsValid_ShouldValidateCorrectly(string? role, bool expected)
    {
        // Act
        var result = role != null && UserRoles.IsValid(role);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void AuthPolicies_ShouldHaveCorrectValues()
    {
        // Assert
        AuthPolicies.AdminOnly.Should().Be("AdminOnly");
        AuthPolicies.OperatorOrAbove.Should().Be("OperatorOrAbove");
        AuthPolicies.AllAuthenticated.Should().Be("AllAuthenticated");
    }
}
