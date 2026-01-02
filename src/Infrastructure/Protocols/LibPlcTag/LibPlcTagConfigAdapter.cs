using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.Protocols.LibPlcTag;

/// <summary>
/// 将数据库配置转换为 LibPlcTag Collector 配置
/// </summary>
public interface ILibPlcTagConfigAdapter
{
    /// <summary>
    /// 从数据库加载并转换为 LibPlcTagOptions（用于 LibPlcTagCollector）
    /// </summary>
    Task<LibPlcTagOptions> LoadFromDatabaseAsync(CancellationToken ct);
}

public sealed class LibPlcTagConfigAdapter : ILibPlcTagConfigAdapter
{
    private readonly IDbConfigProvider _configProvider;
    private readonly ILogger<LibPlcTagConfigAdapter> _logger;

    public LibPlcTagConfigAdapter(
        IDbConfigProvider configProvider,
        ILogger<LibPlcTagConfigAdapter> logger)
    {
        _configProvider = configProvider;
        _logger = logger;
    }

    public async Task<LibPlcTagOptions> LoadFromDatabaseAsync(CancellationToken ct)
    {
        var devicesWithTags = await _configProvider.GetDevicesWithTagsAsync("libplctag", ct);

        if (devicesWithTags.Count == 0)
        {
            _logger.LogDebug("No LibPlcTag devices found in database");
            return new LibPlcTagOptions { Enabled = false };
        }

        var plcs = new List<PlcEndpointConfig>();

        foreach (var dwt in devicesWithTags)
        {
            var device = dwt.Device;
            var tags = dwt.Tags;

            // 获取 IP 地址
            var ipAddress = device.Host;
            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                _logger.LogWarning("Device {DeviceId} has no host/IP address, skipping", device.DeviceId);
                continue;
            }

            // 从 Metadata 读取 LibPlcTag 特有配置
            var plcType = GetMetadataValue(device.Metadata, "PlcType", "ControlLogix");
            var path = GetMetadataValue(device.Metadata, "Path", "1,0");
            var slot = GetMetadataValueInt(device.Metadata, "Slot", 0);
            var maxConnections = GetMetadataValueInt(device.Metadata, "MaxConnections", 4);
            var timeoutMs = GetMetadataValueInt(device.Metadata, "TimeoutMs", 5000);
            var readMode = GetMetadataValue(device.Metadata, "ReadMode", "BatchRead");

            // 按 TagGroup 分组标签
            var tagGroups = new Dictionary<string, List<PlcTagConfig>>();

            foreach (var tag in tags)
            {
                if (string.IsNullOrWhiteSpace(tag.Address))
                {
                    _logger.LogWarning("Tag {TagId} has no address (PLC tag name), skipping", tag.TagId);
                    continue;
                }

                var groupName = tag.TagGroup ?? "Normal";
                if (!tagGroups.ContainsKey(groupName))
                {
                    tagGroups[groupName] = new List<PlcTagConfig>();
                }

                // 从 tag metadata 或 device metadata 获取 CipType
                var cipType = GetCipType(tag);

                tagGroups[groupName].Add(new PlcTagConfig
                {
                    TagId = tag.TagId,
                    Name = tag.Address,  // PLC 内部 tag 名
                    CipType = cipType,
                    ArrayLength = GetMetadataValueInt(tag.Metadata, "ArrayLength", 0),
                    Unit = tag.Unit,
                    Description = tag.Description
                });
            }

            if (tagGroups.Count == 0)
            {
                _logger.LogWarning("Device {DeviceId} has no valid tags, skipping", device.DeviceId);
                continue;
            }

            // 构建 TagGroupConfig 列表
            var tagGroupConfigs = tagGroups.Select(kv => new TagGroupConfig
            {
                Name = kv.Key,
                ScanIntervalMs = GetScanInterval(kv.Key),
                BatchReadSize = 100,
                Tags = kv.Value
            }).ToList();

            plcs.Add(new PlcEndpointConfig
            {
                PlcId = device.DeviceId,
                IpAddress = ipAddress,
                Path = path,
                Slot = slot,
                PlcType = plcType,
                MaxConnections = maxConnections,
                ReadMode = readMode,
                TimeoutMs = timeoutMs,
                Retry = new RetryPolicyConfig
                {
                    MaxRetries = 3,
                    InitialDelayMs = 1000,
                    MaxDelayMs = 60000,
                    BackoffMultiplier = 2.0
                },
                TagGroups = tagGroupConfigs
            });

            _logger.LogInformation(
                "Loaded LibPlcTag device {DeviceId} ({PlcType}) with {TagCount} tags in {GroupCount} groups from database",
                device.DeviceId,
                plcType,
                tags.Count,
                tagGroupConfigs.Count);
        }

        return new LibPlcTagOptions
        {
            Enabled = plcs.Count > 0,
            Plcs = plcs
        };
    }

    /// <summary>
    /// 根据 TagGroup 名称获取默认扫描间隔
    /// </summary>
    private static int GetScanInterval(string groupName)
    {
        return groupName.ToUpperInvariant() switch
        {
            "FAST" => 100,
            "NORMAL" => 1000,
            "SLOW" => 5000,
            _ => 1000
        };
    }

    /// <summary>
    /// 从 tag 的 DataType 或 Metadata 推断 CipType
    /// </summary>
    private string GetCipType(TagDto tag)
    {
        // 优先使用 metadata 中的 CipType
        var cipType = GetMetadataValue(tag.Metadata, "CipType", null);
        if (!string.IsNullOrEmpty(cipType))
            return cipType;

        // 根据 DataType 推断
        return tag.DataType switch
        {
            TagValueType.Bool => "BOOL",
            TagValueType.Int8 => "SINT",
            TagValueType.UInt8 => "USINT",
            TagValueType.Int16 => "INT",
            TagValueType.UInt16 => "UINT",
            TagValueType.Int32 => "DINT",
            TagValueType.UInt32 => "UDINT",
            TagValueType.Int64 => "LINT",
            TagValueType.UInt64 => "ULINT",
            TagValueType.Float32 => "REAL",
            TagValueType.Float64 => "LREAL",
            TagValueType.String => "STRING",
            _ => "REAL"  // 默认
        };
    }

    private static string GetMetadataValue(Dictionary<string, string>? metadata, string key, string? defaultValue)
    {
        if (metadata != null && metadata.TryGetValue(key, out var value))
            return value;
        return defaultValue ?? string.Empty;
    }

    private static int GetMetadataValueInt(Dictionary<string, string>? metadata, string key, int defaultValue)
    {
        if (metadata != null && metadata.TryGetValue(key, out var value) && int.TryParse(value, out var result))
            return result;
        return defaultValue;
    }
}
