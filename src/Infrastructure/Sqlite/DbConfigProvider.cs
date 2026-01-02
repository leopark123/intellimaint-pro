using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.Sqlite;

/// <summary>
/// 从数据库加载设备与标签配置（供采集器使用）
/// </summary>
public sealed class DbConfigProvider : IDbConfigProvider
{
    private readonly IDeviceRepository _deviceRepo;
    private readonly ITagRepository _tagRepo;
    private readonly ILogger<DbConfigProvider> _logger;

    public event Action? OnConfigChanged;

    public DbConfigProvider(
        IDeviceRepository deviceRepo,
        ITagRepository tagRepo,
        ILogger<DbConfigProvider> logger)
    {
        _deviceRepo = deviceRepo;
        _tagRepo = tagRepo;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DeviceWithTags>> GetDevicesWithTagsAsync(string protocol, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(protocol))
            return Array.Empty<DeviceWithTags>();

        var allDevices = await _deviceRepo.ListAsync(ct);

        var selected = allDevices
            .Where(d =>
                d.Enabled &&
                !string.IsNullOrWhiteSpace(d.Protocol) &&
                string.Equals(d.Protocol, protocol, StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d.Name)
            .ThenBy(d => d.DeviceId)
            .ToList();

        if (selected.Count == 0)
        {
            _logger.LogDebug("No enabled devices for protocol: {Protocol}", protocol);
            return Array.Empty<DeviceWithTags>();
        }

        var result = new List<DeviceWithTags>(capacity: selected.Count);

        foreach (var device in selected)
        {
            var allTags = await _tagRepo.ListByDeviceAsync(device.DeviceId, ct);
            var enabledTags = allTags.Where(t => t.Enabled).ToList();

            if (enabledTags.Count == 0)
            {
                _logger.LogDebug("Device {DeviceId} has no enabled tags, skip", device.DeviceId);
                continue;
            }

            result.Add(new DeviceWithTags
            {
                Device = device,
                Tags = enabledTags
            });
        }

        _logger.LogDebug("Loaded config: {DeviceCount} devices for {Protocol}", result.Count, protocol);
        return result;
    }

    public void NotifyConfigChanged()
    {
        _logger.LogInformation("DbConfigProvider config changed notification triggered");
        OnConfigChanged?.Invoke();
    }
}
