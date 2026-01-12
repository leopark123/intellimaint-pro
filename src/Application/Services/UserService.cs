using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Application.Services;

/// <summary>
/// P1: 用户服务接口 - 集中用户管理业务逻辑
/// </summary>
public interface IUserService
{
    /// <summary>获取用户列表</summary>
    Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken ct);

    /// <summary>获取用户详情</summary>
    Task<UserResult> GetByIdAsync(string userId, CancellationToken ct);

    /// <summary>创建用户</summary>
    Task<UserResult> CreateAsync(string username, string password, string role, string? displayName, CancellationToken ct);

    /// <summary>更新用户</summary>
    Task<UserResult> UpdateAsync(string userId, string? displayName, string? role, bool? enabled, CancellationToken ct);

    /// <summary>禁用用户</summary>
    Task<UserResult> DisableAsync(string userId, string operatorId, CancellationToken ct);

    /// <summary>重置密码（管理员操作）</summary>
    Task<UserResult> ResetPasswordAsync(string userId, string newPassword, CancellationToken ct);

    /// <summary>修改密码（用户自己操作）</summary>
    Task<UserResult> ChangePasswordAsync(string userId, string currentPassword, string newPassword, CancellationToken ct);
}

/// <summary>
/// 用户操作结果
/// </summary>
public sealed record UserResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public int? StatusCode { get; init; }
    public UserDto? User { get; init; }

    public static UserResult Failed(string error, int statusCode = 400)
        => new() { Success = false, Error = error, StatusCode = statusCode };

    public static UserResult NotFound(string error = "用户不存在")
        => new() { Success = false, Error = error, StatusCode = 404 };

    public static UserResult Conflict(string error)
        => new() { Success = false, Error = error, StatusCode = 409 };

    public static UserResult Succeeded(UserDto? user = null)
        => new() { Success = true, User = user };
}

/// <summary>
/// P1: 用户服务实现
/// </summary>
public sealed class UserService : IUserService
{
    private readonly IUserRepository _userRepo;
    private readonly ILogger<UserService> _logger;

    public UserService(IUserRepository userRepo, ILogger<UserService> logger)
    {
        _userRepo = userRepo;
        _logger = logger;
    }

    public async Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken ct)
    {
        return await _userRepo.ListAsync(ct);
    }

    public async Task<UserResult> GetByIdAsync(string userId, CancellationToken ct)
    {
        var user = await _userRepo.GetByIdAsync(userId, ct);
        if (user == null)
        {
            return UserResult.NotFound();
        }
        return UserResult.Succeeded(user);
    }

    public async Task<UserResult> CreateAsync(string username, string password, string role, string? displayName, CancellationToken ct)
    {
        // 业务验证：角色有效性
        if (!UserRoles.IsValid(role))
        {
            return UserResult.Failed($"无效角色，可选值: {string.Join(", ", UserRoles.All)}");
        }

        // 业务验证：用户名唯一性
        var existing = await _userRepo.GetByUsernameAsync(username, ct);
        if (existing != null)
        {
            return UserResult.Conflict("用户名已存在");
        }

        var user = await _userRepo.CreateAsync(username, password, role, displayName, ct);
        if (user == null)
        {
            _logger.LogError("Failed to create user: {Username}", username);
            return UserResult.Failed("创建用户失败", 500);
        }

        _logger.LogInformation("User created: {Username}, Role: {Role}", username, role);
        return UserResult.Succeeded(user);
    }

    public async Task<UserResult> UpdateAsync(string userId, string? displayName, string? role, bool? enabled, CancellationToken ct)
    {
        var existing = await _userRepo.GetByIdAsync(userId, ct);
        if (existing == null)
        {
            return UserResult.NotFound();
        }

        // 业务验证：角色有效性
        if (role != null && !UserRoles.IsValid(role))
        {
            return UserResult.Failed($"无效角色，可选值: {string.Join(", ", UserRoles.All)}");
        }

        var user = await _userRepo.UpdateAsync(userId, displayName, role, enabled, ct);

        _logger.LogInformation("User updated: {UserId}", userId);
        return UserResult.Succeeded(user);
    }

    public async Task<UserResult> DisableAsync(string userId, string operatorId, CancellationToken ct)
    {
        // 业务规则：不能禁用自己
        if (userId == operatorId)
        {
            return UserResult.Failed("不能禁用自己");
        }

        var existing = await _userRepo.GetByIdAsync(userId, ct);
        if (existing == null)
        {
            return UserResult.NotFound();
        }

        var success = await _userRepo.DisableAsync(userId, ct);
        if (!success)
        {
            _logger.LogError("Failed to disable user: {UserId}", userId);
            return UserResult.Failed("禁用用户失败", 500);
        }

        _logger.LogInformation("User disabled: {UserId}", userId);
        return UserResult.Succeeded();
    }

    public async Task<UserResult> ResetPasswordAsync(string userId, string newPassword, CancellationToken ct)
    {
        var existing = await _userRepo.GetByIdAsync(userId, ct);
        if (existing == null)
        {
            return UserResult.NotFound();
        }

        var success = await _userRepo.ResetPasswordAsync(userId, newPassword, ct);
        if (!success)
        {
            _logger.LogError("Failed to reset password for user: {UserId}", userId);
            return UserResult.Failed("重置密码失败", 500);
        }

        _logger.LogInformation("Password reset for user: {UserId}", userId);
        return UserResult.Succeeded();
    }

    public async Task<UserResult> ChangePasswordAsync(string userId, string currentPassword, string newPassword, CancellationToken ct)
    {
        var success = await _userRepo.UpdatePasswordAsync(userId, currentPassword, newPassword, ct);
        if (!success)
        {
            return UserResult.Failed("当前密码错误");
        }

        _logger.LogInformation("Password changed for user: {UserId}", userId);
        return UserResult.Succeeded();
    }
}
