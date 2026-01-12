using Microsoft.Extensions.Caching.Memory;
using IntelliMaint.Core.Contracts;

namespace IntelliMaint.Host.Api.Services;

/// <summary>
/// v48: 缓存服务 - 封装 IMemoryCache 提供类型安全的缓存操作
/// </summary>
public sealed class CacheService
{
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan DefaultExpiration = TimeSpan.FromMinutes(5);
    
    // 缓存键常量
    public static class Keys
    {
        public const string DeviceList = "devices:all";
        public const string TagList = "tags:all";
        public const string EnabledTagList = "tags:enabled";
        public const string Settings = "settings:all";
        public const string AlarmStats = "alarms:stats";

        // v56: 审计日志筛选项缓存
        public const string AuditActions = "audit:actions";
        public const string AuditResourceTypes = "audit:resourceTypes";

        // v56: 告警规则缓存
        public const string AlarmRuleList = "alarmrules:all";

        public static string DeviceById(string deviceId) => $"device:{deviceId}";
        public static string TagsByDevice(string deviceId) => $"tags:device:{deviceId}";
    }

    public CacheService(IMemoryCache cache)
    {
        _cache = cache;
    }

    /// <summary>获取或创建缓存项</summary>
    public async Task<T?> GetOrCreateAsync<T>(
        string key, 
        Func<Task<T>> factory, 
        TimeSpan? expiration = null)
    {
        return await _cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = expiration ?? DefaultExpiration;
            return await factory();
        });
    }

    /// <summary>获取缓存项</summary>
    public T? Get<T>(string key)
    {
        return _cache.TryGetValue(key, out T? value) ? value : default;
    }

    /// <summary>设置缓存项</summary>
    public void Set<T>(string key, T value, TimeSpan? expiration = null)
    {
        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiration ?? DefaultExpiration
        };
        _cache.Set(key, value, options);
    }

    /// <summary>移除缓存项</summary>
    public void Remove(string key)
    {
        _cache.Remove(key);
    }

    /// <summary>使设备相关缓存失效</summary>
    public void InvalidateDevice(string? deviceId = null)
    {
        _cache.Remove(Keys.DeviceList);
        if (deviceId != null)
        {
            _cache.Remove(Keys.DeviceById(deviceId));
            _cache.Remove(Keys.TagsByDevice(deviceId));
        }
    }

    /// <summary>使标签相关缓存失效</summary>
    public void InvalidateTags(string? deviceId = null)
    {
        _cache.Remove(Keys.TagList);
        _cache.Remove(Keys.EnabledTagList);
        if (deviceId != null)
        {
            _cache.Remove(Keys.TagsByDevice(deviceId));
        }
    }

    /// <summary>使设置缓存失效</summary>
    public void InvalidateSettings()
    {
        _cache.Remove(Keys.Settings);
    }

    /// <summary>使告警统计缓存失效</summary>
    public void InvalidateAlarmStats()
    {
        _cache.Remove(Keys.AlarmStats);
    }

    /// <summary>使告警规则缓存失效</summary>
    public void InvalidateAlarmRules()
    {
        _cache.Remove(Keys.AlarmRuleList);
    }

    /// <summary>清除所有缓存</summary>
    public void Clear()
    {
        // IMemoryCache 不支持清除所有，这里只能逐个移除已知键
        _cache.Remove(Keys.DeviceList);
        _cache.Remove(Keys.TagList);
        _cache.Remove(Keys.EnabledTagList);
        _cache.Remove(Keys.Settings);
        _cache.Remove(Keys.AlarmStats);
        _cache.Remove(Keys.AlarmRuleList);
        _cache.Remove(Keys.AuditActions);
        _cache.Remove(Keys.AuditResourceTypes);
    }
}
