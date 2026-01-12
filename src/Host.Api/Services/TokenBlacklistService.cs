using Microsoft.Extensions.Caching.Memory;

namespace IntelliMaint.Host.Api.Services;

/// <summary>
/// Token 黑名单服务 - 用于立即失效已登出或被禁用用户的 Token
/// 使用内存缓存存储被黑名单的用户ID和时间戳
/// </summary>
public sealed class TokenBlacklistService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<TokenBlacklistService> _logger;
    private const string CacheKeyPrefix = "token_blacklist:";

    // Access token 默认有效期（与 JwtService 保持一致）
    private static readonly TimeSpan BlacklistDuration = TimeSpan.FromMinutes(20);

    public TokenBlacklistService(IMemoryCache cache, ILogger<TokenBlacklistService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// 将用户添加到黑名单（立即使其所有 Token 失效）
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="reason">原因（用于日志）</param>
    public void BlacklistUser(string userId, string reason)
    {
        var blacklistedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var cacheKey = $"{CacheKeyPrefix}{userId}";

        _cache.Set(cacheKey, blacklistedAt, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = BlacklistDuration,
            Priority = CacheItemPriority.High
        });

        _logger.LogInformation(
            "User {UserId} added to token blacklist. Reason: {Reason}. Effective until: {Until}",
            userId, reason, DateTimeOffset.UtcNow.Add(BlacklistDuration));
    }

    /// <summary>
    /// 检查 Token 是否被黑名单（基于用户ID和Token签发时间）
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="tokenIssuedAt">Token 签发时间 (Unix timestamp in seconds)</param>
    /// <returns>如果 Token 被黑名单返回 true</returns>
    public bool IsTokenBlacklisted(string userId, long tokenIssuedAt)
    {
        var cacheKey = $"{CacheKeyPrefix}{userId}";

        if (_cache.TryGetValue<long>(cacheKey, out var blacklistedAt))
        {
            // 如果 Token 在用户被黑名单之前签发，则 Token 被视为无效
            if (tokenIssuedAt <= blacklistedAt)
            {
                _logger.LogDebug(
                    "Token rejected for user {UserId}. Token issued at {IssuedAt}, blacklisted at {BlacklistedAt}",
                    userId, tokenIssuedAt, blacklistedAt);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 从黑名单中移除用户（例如重新启用账号时）
    /// </summary>
    /// <param name="userId">用户ID</param>
    public void RemoveFromBlacklist(string userId)
    {
        var cacheKey = $"{CacheKeyPrefix}{userId}";
        _cache.Remove(cacheKey);
        _logger.LogInformation("User {UserId} removed from token blacklist", userId);
    }
}
