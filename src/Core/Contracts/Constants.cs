namespace IntelliMaint.Core.Contracts;

/// <summary>
/// P1: 系统常量定义 - 消除魔法数字
/// </summary>
public static class SystemConstants
{
    /// <summary>
    /// 告警相关常量
    /// </summary>
    public static class Alarm
    {
        /// <summary>告警评估默认批次大小</summary>
        public const int DefaultBatchSize = 1000;

        /// <summary>告警评估间隔（毫秒）</summary>
        public const int EvaluationIntervalMs = 5000;

        /// <summary>规则缓存刷新间隔（毫秒）</summary>
        public const int RuleCacheRefreshMs = 30000;

        /// <summary>最小告警严重级别</summary>
        public const int MinSeverity = 1;

        /// <summary>最大告警严重级别</summary>
        public const int MaxSeverity = 5;
    }

    /// <summary>
    /// 查询相关常量
    /// </summary>
    public static class Query
    {
        /// <summary>默认查询限制</summary>
        public const int DefaultLimit = 100;

        /// <summary>最大查询限制</summary>
        public const int MaxLimit = 1000;

        /// <summary>默认时间范围（小时）</summary>
        public const int DefaultHoursRange = 24;

        /// <summary>最大时间范围（小时）</summary>
        public const int MaxHoursRange = 168; // 7天
    }

    /// <summary>
    /// 认证相关常量
    /// </summary>
    public static class Auth
    {
        /// <summary>最大登录尝试次数</summary>
        public const int MaxLoginAttempts = 5;

        /// <summary>账号锁定时间（分钟）</summary>
        public const int LockoutMinutes = 15;

        /// <summary>JWT 密钥最小长度</summary>
        public const int MinSecretKeyLength = 32;
    }

    /// <summary>
    /// 验证相关常量
    /// </summary>
    public static class Validation
    {
        /// <summary>用户名最小长度</summary>
        public const int UsernameMinLength = 3;

        /// <summary>用户名最大长度</summary>
        public const int UsernameMaxLength = 50;

        /// <summary>密码最小长度</summary>
        public const int PasswordMinLength = 8;

        /// <summary>密码最大长度</summary>
        public const int PasswordMaxLength = 100;

        /// <summary>显示名最大长度</summary>
        public const int DisplayNameMaxLength = 100;

        /// <summary>通用名称最大长度</summary>
        public const int NameMaxLength = 200;

        /// <summary>描述最大长度</summary>
        public const int DescriptionMaxLength = 1000;
    }

    /// <summary>
    /// 数据管道常量
    /// </summary>
    public static class Pipeline
    {
        /// <summary>默认 Channel 容量</summary>
        public const int DefaultChannelCapacity = 10000;

        /// <summary>批量写入大小</summary>
        public const int BatchWriteSize = 100;

        /// <summary>批量写入超时（毫秒）</summary>
        public const int BatchWriteTimeoutMs = 1000;
    }

    /// <summary>
    /// 缓存常量
    /// </summary>
    public static class Cache
    {
        /// <summary>默认缓存过期时间（秒）</summary>
        public const int DefaultExpirationSeconds = 300;

        /// <summary>短期缓存过期时间（秒）</summary>
        public const int ShortExpirationSeconds = 60;

        /// <summary>长期缓存过期时间（秒）</summary>
        public const int LongExpirationSeconds = 3600;
    }

    /// <summary>
    /// 请求限流常量
    /// </summary>
    public static class RateLimiting
    {
        /// <summary>时间窗口（秒）</summary>
        public const int WindowSeconds = 60;

        /// <summary>最大请求次数</summary>
        public const int MaxRequests = 100;
    }
}
