using System.Collections.Concurrent;
using System.Net;
using Serilog;

namespace IntelliMaint.Host.Api.Middleware;

/// <summary>
/// v44: 简单的滑动窗口限流中间件
/// 按 IP 地址限制请求频率
/// v56.2: 支持端点级别的差异化限流（登录端点更严格）
/// </summary>
public sealed class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RateLimitOptions _options;

    // 存储每个 IP 的请求记录 (IP -> 请求时间戳列表)
    private readonly ConcurrentDictionary<string, RequestTracker> _requestTrackers = new();

    // v56.2: 登录端点专用限流追踪器（更严格）
    private readonly ConcurrentDictionary<string, RequestTracker> _loginTrackers = new();

    public RateLimitingMiddleware(RequestDelegate next, RateLimitOptions? options = null)
    {
        _next = next;
        _options = options ?? new RateLimitOptions();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // 跳过健康检查和 SignalR
        var path = context.Request.Path.Value ?? "";
        if (path.StartsWith("/health") || path.StartsWith("/hubs"))
        {
            await _next(context);
            return;
        }

        var clientIp = GetClientIp(context);
        var now = DateTimeOffset.UtcNow;

        // v56.2: 登录端点使用更严格的限流（防止暴力破解）
        var isLoginEndpoint = path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase)
                              && context.Request.Method == "POST";

        if (isLoginEndpoint)
        {
            var (loginLimited, loginCount) = CheckLoginRateLimit(clientIp, now);
            if (loginLimited)
            {
                Log.Warning("[Security] Login rate limit exceeded for IP {ClientIp}: {Count} attempts in {Window}s",
                    clientIp, loginCount, _options.LoginWindowSeconds);

                context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
                context.Response.Headers["Retry-After"] = _options.LoginWindowSeconds.ToString();
                context.Response.ContentType = "application/json";

                var response = new
                {
                    success = false,
                    error = $"登录尝试过于频繁，请 {_options.LoginWindowSeconds} 秒后重试",
                    retryAfter = _options.LoginWindowSeconds
                };

                await context.Response.WriteAsJsonAsync(response);
                return;
            }
        }

        var tracker = _requestTrackers.GetOrAdd(clientIp, _ => new RequestTracker());

        bool isRateLimited;
        int currentCount;

        lock (tracker)
        {
            // 清理过期的请求记录
            var cutoff = now.AddSeconds(-_options.WindowSeconds);
            tracker.Timestamps.RemoveAll(ts => ts < cutoff);

            currentCount = tracker.Timestamps.Count;

            // 检查是否超过限制
            if (currentCount >= _options.MaxRequests)
            {
                isRateLimited = true;
            }
            else
            {
                isRateLimited = false;
                // 记录本次请求
                tracker.Timestamps.Add(now);
            }
        }

        // 在 lock 外处理限流响应
        if (isRateLimited)
        {
            Log.Warning("Rate limit exceeded for IP {ClientIp}: {Count} requests in {Window}s",
                clientIp, currentCount, _options.WindowSeconds);

            context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
            context.Response.Headers["Retry-After"] = _options.WindowSeconds.ToString();
            context.Response.ContentType = "application/json";

            var response = new
            {
                success = false,
                error = "Too many requests. Please try again later.",
                retryAfter = _options.WindowSeconds
            };

            await context.Response.WriteAsJsonAsync(response);
            return;
        }

        await _next(context);

        // 定期清理长时间不活跃的 IP 记录
        if (_requestTrackers.Count > 10000)
        {
            CleanupInactiveTrackers();
        }
    }

    /// <summary>
    /// v56.2: 登录端点专用限流检查
    /// 防止暴力破解攻击：每 IP 每 60 秒最多 5 次登录尝试
    /// </summary>
    private (bool IsLimited, int Count) CheckLoginRateLimit(string clientIp, DateTimeOffset now)
    {
        var tracker = _loginTrackers.GetOrAdd(clientIp, _ => new RequestTracker());

        lock (tracker)
        {
            // 清理过期记录
            var cutoff = now.AddSeconds(-_options.LoginWindowSeconds);
            tracker.Timestamps.RemoveAll(ts => ts < cutoff);

            var currentCount = tracker.Timestamps.Count;

            if (currentCount >= _options.LoginMaxAttempts)
            {
                return (true, currentCount);
            }

            // 记录本次尝试
            tracker.Timestamps.Add(now);
            return (false, currentCount + 1);
        }
    }

    private static string GetClientIp(HttpContext context)
    {
        // 优先使用 X-Forwarded-For（代理场景）
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            // 取第一个 IP（原始客户端）
            return forwardedFor.Split(',')[0].Trim();
        }

        // 直连场景
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private void CleanupInactiveTrackers()
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-5);
        var keysToRemove = new List<string>();

        foreach (var kvp in _requestTrackers)
        {
            lock (kvp.Value)
            {
                if (kvp.Value.Timestamps.Count == 0 || 
                    kvp.Value.Timestamps.Max() < cutoff)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }
        }

        foreach (var key in keysToRemove)
        {
            _requestTrackers.TryRemove(key, out _);
        }
    }

    private sealed class RequestTracker
    {
        public List<DateTimeOffset> Timestamps { get; } = new();
    }
}

/// <summary>
/// 限流配置
/// </summary>
public sealed class RateLimitOptions
{
    /// <summary>
    /// 通用 API 时间窗口（秒）
    /// </summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>
    /// 通用 API 窗口内最大请求数
    /// </summary>
    public int MaxRequests { get; set; } = 100;

    /// <summary>
    /// v56.2: 登录端点时间窗口（秒）- 防暴力破解
    /// </summary>
    public int LoginWindowSeconds { get; set; } = 60;

    /// <summary>
    /// v56.2: 登录端点窗口内最大尝试次数 - 防暴力破解
    /// 默认每 IP 每分钟最多 5 次登录尝试
    /// </summary>
    public int LoginMaxAttempts { get; set; } = 5;
}

/// <summary>
/// 扩展方法
/// </summary>
public static class RateLimitingExtensions
{
    public static IApplicationBuilder UseRateLimiting(
        this IApplicationBuilder app, 
        Action<RateLimitOptions>? configure = null)
    {
        var options = new RateLimitOptions();
        configure?.Invoke(options);
        
        return app.UseMiddleware<RateLimitingMiddleware>(options);
    }
}
