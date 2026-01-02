using System.Security.Claims;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;

namespace IntelliMaint.Host.Api.Services;

/// <summary>
/// v44: 审计日志辅助服务
/// 统一审计日志记录，自动提取用户信息和 IP 地址
/// </summary>
public sealed class AuditService
{
    private readonly IAuditLogRepository _auditRepo;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuditService(IAuditLogRepository auditRepo, IHttpContextAccessor httpContextAccessor)
    {
        _auditRepo = auditRepo;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// 记录审计日志
    /// </summary>
    public async Task LogAsync(
        string action,
        string resourceType,
        string? resourceId = null,
        string? details = null,
        CancellationToken ct = default)
    {
        var context = _httpContextAccessor.HttpContext;
        var user = context?.User;

        var entry = new AuditLogEntry
        {
            Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UserId = GetUserId(user) ?? "system",
            UserName = GetUserName(user) ?? "system",
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Details = details,
            IpAddress = GetClientIp(context)
        };

        await _auditRepo.CreateAsync(entry, ct);
    }

    /// <summary>
    /// 记录登录事件
    /// </summary>
    public async Task LogLoginAsync(
        string userId,
        string userName,
        bool success,
        string? failReason = null,
        CancellationToken ct = default)
    {
        var context = _httpContextAccessor.HttpContext;

        var entry = new AuditLogEntry
        {
            Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UserId = userId,
            UserName = userName,
            Action = success ? AuditActions.Login : AuditActions.LoginFailed,
            ResourceType = "Auth",
            ResourceId = null,
            Details = failReason,
            IpAddress = GetClientIp(context)
        };

        await _auditRepo.CreateAsync(entry, ct);
    }

    /// <summary>
    /// 记录登出事件
    /// </summary>
    public async Task LogLogoutAsync(CancellationToken ct = default)
    {
        var context = _httpContextAccessor.HttpContext;
        var user = context?.User;

        var entry = new AuditLogEntry
        {
            Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UserId = GetUserId(user) ?? "unknown",
            UserName = GetUserName(user) ?? "unknown",
            Action = AuditActions.Logout,
            ResourceType = "Auth",
            ResourceId = null,
            Details = null,
            IpAddress = GetClientIp(context)
        };

        await _auditRepo.CreateAsync(entry, ct);
    }

    private static string? GetUserId(ClaimsPrincipal? user)
    {
        return user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }

    private static string? GetUserName(ClaimsPrincipal? user)
    {
        return user?.FindFirst(ClaimTypes.Name)?.Value 
            ?? user?.Identity?.Name;
    }

    private static string? GetClientIp(HttpContext? context)
    {
        if (context == null) return null;

        // 优先使用 X-Forwarded-For（代理场景）
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',')[0].Trim();
        }

        return context.Connection.RemoteIpAddress?.ToString();
    }
}

/// <summary>
/// 审计动作常量
/// </summary>
public static class AuditActions
{
    // 认证
    public const string Login = "Login";
    public const string LoginFailed = "LoginFailed";
    public const string Logout = "Logout";
    public const string TokenRefresh = "TokenRefresh";
    
    // 设备
    public const string DeviceCreate = "DeviceCreate";
    public const string DeviceUpdate = "DeviceUpdate";
    public const string DeviceDelete = "DeviceDelete";
    
    // 标签
    public const string TagCreate = "TagCreate";
    public const string TagUpdate = "TagUpdate";
    public const string TagDelete = "TagDelete";
    
    // 告警
    public const string AlarmAck = "AlarmAck";
    public const string AlarmClose = "AlarmClose";
    
    // 告警规则
    public const string AlarmRuleCreate = "AlarmRuleCreate";
    public const string AlarmRuleUpdate = "AlarmRuleUpdate";
    public const string AlarmRuleDelete = "AlarmRuleDelete";
    
    // 用户
    public const string UserCreate = "UserCreate";
    public const string UserUpdate = "UserUpdate";
    public const string UserDelete = "UserDelete";
    public const string UserPasswordChange = "UserPasswordChange";
    public const string UserPasswordReset = "UserPasswordReset";
    
    // 系统
    public const string SettingUpdate = "SettingUpdate";
    public const string DataExport = "DataExport";
}
