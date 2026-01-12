namespace IntelliMaint.Core.Contracts;

public sealed class UserDto
{
    public required string UserId { get; init; }
    public required string Username { get; init; }
    public string? DisplayName { get; init; }
    public required string Role { get; init; }
    public bool Enabled { get; init; } = true;
    public long CreatedUtc { get; init; }
    public long? LastLoginUtc { get; init; }
    /// <summary>是否必须修改密码（首次登录或管理员重置后）</summary>
    public bool MustChangePassword { get; init; }
}

public sealed class LoginRequest
{
    public required string Username { get; init; }
    public required string Password { get; init; }
}

public sealed class LoginResponse
{
    public required string Token { get; init; }
    public required string RefreshToken { get; init; }
    public required string Username { get; init; }
    public required string Role { get; init; }
    public required long ExpiresAt { get; init; }
    public required long RefreshExpiresAt { get; init; }
    /// <summary>是否必须修改密码（首次登录或管理员重置后）</summary>
    public bool MustChangePassword { get; init; }
}

public sealed class RefreshTokenRequest
{
    public required string RefreshToken { get; init; }
}

// v40 新增
public sealed class CreateUserRequest
{
    public required string Username { get; init; }
    public required string Password { get; init; }
    public required string Role { get; init; }
    public string? DisplayName { get; init; }
}

// v40 新增
public sealed class UpdateUserRequest
{
    public string? DisplayName { get; init; }
    public string? Role { get; init; }
    public bool? Enabled { get; init; }
}

// v40 新增
public sealed class ChangePasswordRequest
{
    public required string CurrentPassword { get; init; }
    public required string NewPassword { get; init; }
}

// v40 新增
public sealed class ResetPasswordRequest
{
    public required string NewPassword { get; init; }
}

public static class UserRoles
{
    public const string Admin = "Admin";
    public const string Operator = "Operator";
    public const string Viewer = "Viewer";

    public static readonly string[] All = { Admin, Operator, Viewer };

    public static bool IsValid(string role) => All.Contains(role);
}

/// <summary>
/// 授权策略常量
/// </summary>
public static class AuthPolicies
{
    /// <summary>只有 Admin 可访问</summary>
    public const string AdminOnly = "AdminOnly";
    
    /// <summary>Admin 或 Operator 可访问</summary>
    public const string OperatorOrAbove = "OperatorOrAbove";
    
    /// <summary>所有已认证用户可访问</summary>
    public const string AllAuthenticated = "AllAuthenticated";
}
