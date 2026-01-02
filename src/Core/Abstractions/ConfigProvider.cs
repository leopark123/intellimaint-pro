using IntelliMaint.Core.Contracts;

namespace IntelliMaint.Core.Abstractions;

/// <summary>
/// 设备与标签配置提供者接口（供采集器使用）
/// </summary>
public interface IDbConfigProvider
{
    /// <summary>
    /// 获取指定协议的启用设备及其启用标签
    /// </summary>
    Task<IReadOnlyList<DeviceWithTags>> GetDevicesWithTagsAsync(string protocol, CancellationToken ct);

    /// <summary>
    /// 配置变更事件
    /// </summary>
    event Action? OnConfigChanged;

    /// <summary>
    /// 手动触发配置变更通知
    /// </summary>
    void NotifyConfigChanged();
}

/// <summary>
/// 配置版本提供者接口（用于配置变更检测）
/// </summary>
public interface IConfigRevisionProvider
{
    /// <summary>
    /// 获取当前配置版本号
    /// </summary>
    Task<long> GetRevisionAsync(CancellationToken ct);

    /// <summary>
    /// 递增配置版本号（设备/标签/规则变更时调用）
    /// </summary>
    Task IncrementRevisionAsync(CancellationToken ct);
}

/// <summary>
/// 设备及其标签
/// </summary>
public sealed record DeviceWithTags
{
    public required DeviceDto Device { get; init; }
    public required IReadOnlyList<TagDto> Tags { get; init; }
}
