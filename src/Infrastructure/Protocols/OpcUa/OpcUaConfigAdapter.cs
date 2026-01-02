using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.Protocols.OpcUa;

/// <summary>
/// 将数据库配置转换为 OPC UA Collector 配置
/// </summary>
public interface IOpcUaConfigAdapter
{
    /// <summary>
    /// 从数据库加载并转换为 OpcUaOptions（用于 OpcUaCollector）
    /// </summary>
    Task<OpcUaOptions> LoadFromDatabaseAsync(CancellationToken ct);
}

public sealed class OpcUaConfigAdapter : IOpcUaConfigAdapter
{
    private readonly IDbConfigProvider _configProvider;
    private readonly ILogger<OpcUaConfigAdapter> _logger;

    public OpcUaConfigAdapter(
        IDbConfigProvider configProvider,
        ILogger<OpcUaConfigAdapter> logger)
    {
        _configProvider = configProvider;
        _logger = logger;
    }

    public async Task<OpcUaOptions> LoadFromDatabaseAsync(CancellationToken ct)
    {
        var devicesWithTags = await _configProvider.GetDevicesWithTagsAsync("opcua", ct);

        if (devicesWithTags.Count == 0)
        {
            _logger.LogDebug("No OPC UA devices found in database");
            return new OpcUaOptions { Enabled = false };
        }

        var endpoints = new List<OpcUaEndpointConfig>();

        foreach (var dwt in devicesWithTags)
        {
            var device = dwt.Device;
            var tags = dwt.Tags;

            var endpointUrl = device.GetEffectiveEndpoint();
            if (string.IsNullOrWhiteSpace(endpointUrl))
            {
                _logger.LogWarning("Device {DeviceId} has no valid endpoint URL, skipping", device.DeviceId);
                continue;
            }

            var nodes = new List<OpcUaNodeConfig>();

            foreach (var tag in tags)
            {
                if (string.IsNullOrWhiteSpace(tag.Address))
                {
                    _logger.LogWarning("Tag {TagId} has no address (NodeId), skipping", tag.TagId);
                    continue;
                }

                nodes.Add(new OpcUaNodeConfig
                {
                    TagId = tag.TagId,
                    NodeId = tag.Address,
                    ValueTypeHint = MapDataTypeToHint(tag.DataType),
                    SamplingIntervalMs = tag.ScanIntervalMs ?? 1000,
                    QueueSize = 10,
                    DiscardOldest = true,
                    Unit = tag.Unit
                });
            }

            if (nodes.Count == 0)
            {
                _logger.LogWarning("Device {DeviceId} has no valid tags, skipping", device.DeviceId);
                continue;
            }

            // 从 Metadata 读取安全配置（可选）
            var securityPolicy = device.Metadata != null && device.Metadata.TryGetValue("SecurityPolicy", out var sp)
                ? sp
                : "None";

            var securityMode = device.Metadata != null && device.Metadata.TryGetValue("MessageSecurityMode", out var sm)
                ? sm
                : "None";

            string? username = null;
            if (device.Metadata != null && device.Metadata.TryGetValue("Username", out var u))
                username = u;

            string? password = null;
            if (device.Metadata != null && device.Metadata.TryGetValue("Password", out var p))
                password = p;

            endpoints.Add(new OpcUaEndpointConfig
            {
                EndpointId = device.DeviceId,
                EndpointUrl = endpointUrl,
                ServerType = "KEPServerEX",
                SecurityPolicy = securityPolicy,
                MessageSecurityMode = securityMode,
                Username = username,
                Password = password,
                SessionTimeoutMs = 60000,
                Subscription = new OpcUaSubscriptionConfig
                {
                    PublishingIntervalMs = 100,
                    LifetimeCount = 300,
                    MaxKeepAliveCount = 10,
                    MaxNotificationsPerPublish = 1000
                },
                Nodes = nodes
            });

            _logger.LogInformation(
                "Loaded OPC UA device {DeviceId} with {NodeCount} nodes from database",
                device.DeviceId,
                nodes.Count);
        }

        return new OpcUaOptions
        {
            Enabled = endpoints.Count > 0,
            Endpoints = endpoints
        };
    }

    private static string? MapDataTypeToHint(TagValueType dataType)
    {
        return dataType switch
        {
            TagValueType.Bool => "Boolean",
            TagValueType.Int8 => "SByte",
            TagValueType.UInt8 => "Byte",
            TagValueType.Int16 => "Int16",
            TagValueType.UInt16 => "UInt16",
            TagValueType.Int32 => "Int32",
            TagValueType.UInt32 => "UInt32",
            TagValueType.Int64 => "Int64",
            TagValueType.UInt64 => "UInt64",
            TagValueType.Float32 => "Float",
            TagValueType.Float64 => "Double",
            TagValueType.String => "String",
            TagValueType.DateTime => "DateTime",
            _ => null
        };
    }
}
