using IntelliMaint.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Host.Api.Services;

/// <summary>
/// v65: Edge 配置变更通知服务实现
/// 通过内存事件通知（未来可扩展为 SignalR 或消息队列）
/// </summary>
public sealed class EdgeNotificationService : IEdgeNotificationService
{
    private readonly ILogger<EdgeNotificationService> _logger;
    private readonly Dictionary<string, DateTimeOffset> _configVersions = new();
    private readonly object _lock = new();

    public event Action<string>? OnConfigChanged;

    public EdgeNotificationService(ILogger<EdgeNotificationService> logger)
    {
        _logger = logger;
    }

    public Task NotifyConfigChangedAsync(string edgeId, CancellationToken ct)
    {
        lock (_lock)
        {
            _configVersions[edgeId] = DateTimeOffset.UtcNow;
        }

        _logger.LogInformation("Edge config changed notification: {EdgeId}", edgeId);

        // 触发事件
        OnConfigChanged?.Invoke(edgeId);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取配置版本（用于 Edge 轮询检测变更）
    /// </summary>
    public DateTimeOffset GetConfigVersion(string edgeId)
    {
        lock (_lock)
        {
            return _configVersions.TryGetValue(edgeId, out var version)
                ? version
                : DateTimeOffset.MinValue;
        }
    }

    /// <summary>
    /// 检查配置是否有变更
    /// </summary>
    public bool HasConfigChanged(string edgeId, DateTimeOffset lastKnownVersion)
    {
        var currentVersion = GetConfigVersion(edgeId);
        return currentVersion > lastKnownVersion;
    }
}
