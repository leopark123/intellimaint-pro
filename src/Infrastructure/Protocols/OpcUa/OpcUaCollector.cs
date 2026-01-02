using System.Runtime.CompilerServices;
using System.Threading.Channels;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Client;

namespace IntelliMaint.Infrastructure.Protocols.OpcUa;

/// <summary>
/// OPC UA Collector with full logging support.
/// Supports loading configuration from database with hot reload.
/// </summary>
public sealed class OpcUaCollector : ICollector, ITelemetrySource, IDisposable
{
    private readonly IOptions<OpcUaOptions> _options;
    private readonly OpcUaSessionManager _sessions;
    private readonly OpcUaSubscriptionManager _subs;
    private readonly OpcUaTypeMapper _typeMapper;
    private readonly OpcUaHealthChecker _health;
    private readonly ILogger<OpcUaCollector> _logger;
    
    // Batch 32: 数据库配置支持
    private readonly IOpcUaConfigAdapter? _configAdapter;
    private readonly IDbConfigProvider? _dbConfigProvider;
    private volatile OpcUaOptions _currentOptions;
    private readonly object _reloadLock = new();

    private readonly Channel<TelemetryPoint> _out;
    private CancellationTokenSource? _cts;
    private readonly List<Task> _loops = new();

    public string Protocol => "opcua";

    public OpcUaCollector(
        IOptions<OpcUaOptions> options,
        OpcUaSessionManager sessions,
        OpcUaSubscriptionManager subs,
        OpcUaTypeMapper typeMapper,
        OpcUaHealthChecker health,
        ILogger<OpcUaCollector> logger,
        IOpcUaConfigAdapter? configAdapter = null,
        IDbConfigProvider? dbConfigProvider = null)
    {
        _options = options;
        _sessions = sessions;
        _subs = subs;
        _typeMapper = typeMapper;
        _health = health;
        _logger = logger;
        _configAdapter = configAdapter;
        _dbConfigProvider = dbConfigProvider;
        _currentOptions = options.Value;

        _out = Channel.CreateBounded<TelemetryPoint>(new BoundedChannelOptions(50_000)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
        
        // 订阅配置变更事件
        if (_dbConfigProvider != null)
        {
            _dbConfigProvider.OnConfigChanged += OnConfigChangedHandler;
        }
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (_cts is not null) return;

        // 优先从数据库加载配置
        if (_configAdapter != null)
        {
            try
            {
                var dbOptions = await _configAdapter.LoadFromDatabaseAsync(ct);
                if (dbOptions.Enabled && dbOptions.Endpoints.Count > 0)
                {
                    _logger.LogInformation(
                        "Using database configuration: {EndpointCount} endpoints",
                        dbOptions.Endpoints.Count);
                    _currentOptions = dbOptions;
                }
                else
                {
                    _logger.LogInformation("No database configuration, falling back to appsettings.json");
                    _currentOptions = _options.Value;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load database configuration, falling back to appsettings.json");
                _currentOptions = _options.Value;
            }
        }
        else
        {
            _currentOptions = _options.Value;
        }

        var opt = _currentOptions;
        
        if (!opt.Enabled)
        {
            _logger.LogInformation("OPC UA collector is disabled");
            return;
        }

        if (opt.Endpoints.Count == 0)
        {
            _logger.LogWarning("OPC UA collector enabled but no endpoints configured");
            return;
        }

        var totalNodes = opt.Endpoints.Sum(e => e.Nodes?.Count ?? 0);
        _logger.LogInformation("OPC UA collector starting with {EndpointCount} endpoints and {NodeCount} nodes", 
            opt.Endpoints.Count, totalNodes);

        _health.SetInventory(totalNodes);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        foreach (var ep in opt.Endpoints)
        {
            _logger.LogInformation("Starting endpoint loop for {EndpointId} -> {EndpointUrl}", 
                ep.EndpointId, ep.EndpointUrl);
            var loop = Task.Run(() => RunEndpointAsync(ep, _cts.Token), _cts.Token);
            _loops.Add(loop);
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        await StopAsync(ct, completeChannel: true);
    }
    
    /// <summary>
    /// 停止采集器
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <param name="completeChannel">是否关闭 Channel（热重载时应为 false）</param>
    private async Task StopAsync(CancellationToken ct, bool completeChannel)
    {
        if (_cts is null) return;

        _logger.LogInformation("OPC UA collector stopping...");
        _cts.Cancel();
        try { await Task.WhenAll(_loops).WaitAsync(ct).ConfigureAwait(false); } catch { /* ignore */ }
        _loops.Clear();

        await _sessions.CloseAllAsync(ct).ConfigureAwait(false);

        if (completeChannel)
        {
            _out.Writer.TryComplete();
        }
        
        _cts.Dispose();
        _cts = null;
        _logger.LogInformation("OPC UA collector stopped");
    }

    public CollectorHealth GetHealth()
    {
        _health.MarkConnected(_sessions.ActiveSessionCount);
        var opt = _currentOptions;
        var healthy = 0;
        foreach (var ep in opt.Endpoints)
            healthy += _subs.GetHealthyNodeCount(ep.EndpointId);
        _health.SetHealthyTags(healthy);

        return _health.Snapshot();
    }

    public async IAsyncEnumerable<TelemetryPoint> ReadAsync([EnumeratorCancellation] CancellationToken ct)
    {
        while (await _out.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            while (_out.Reader.TryRead(out var tp))
                yield return tp;
        }
    }

    private async Task RunEndpointAsync(OpcUaEndpointConfig ep, CancellationToken ct)
    {
        int backoffStep = 0;
        _logger.LogDebug("RunEndpointAsync started for {EndpointId}", ep.EndpointId);

        while (!ct.IsCancellationRequested)
        {
            var started = StopwatchMs();
            try
            {
                _logger.LogDebug("Attempting to connect to {EndpointUrl}...", ep.EndpointUrl);
                var session = await _sessions.GetOrCreateSessionAsync(ep, ct).ConfigureAwait(false);
                _logger.LogInformation("Connected to OPC UA server: {EndpointUrl}, SessionId={SessionId}", 
                    ep.EndpointUrl, session.SessionId);

                bool subscribed = await TryEnsureSubscriptionAsync(ep, session, ct).ConfigureAwait(false);
                if (subscribed)
                {
                    _logger.LogInformation("Subscription active for {EndpointId}, waiting for data...", ep.EndpointId);
                    // Wait while subscription is active
                    while (!ct.IsCancellationRequested && session.Connected)
                    {
                        await Task.Delay(1000, ct).ConfigureAwait(false);
                    }
                }
                else
                {
                    _logger.LogWarning("Subscription failed for {EndpointId}, falling back to polling", ep.EndpointId);
                    await PollingLoopAsync(ep, session, ct).ConfigureAwait(false);
                }

                backoffStep = 0;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogDebug("Endpoint loop cancelled for {EndpointId}", ep.EndpointId);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in endpoint {EndpointId}: {Message}", ep.EndpointId, ex.Message);
                _health.MarkDisconnected($"endpoint={ep.EndpointId} error={ex.Message}");
                backoffStep = Math.Min(backoffStep + 1, 6);
                var delay = BackoffDelay(backoffStep);
                _logger.LogWarning("Backing off for {Delay} before retry (step {Step})", delay, backoffStep);
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> TryEnsureSubscriptionAsync(OpcUaEndpointConfig ep, ISession session, CancellationToken ct)
    {
        try
        {
            _logger.LogDebug("Creating subscription for {EndpointId} with {NodeCount} nodes", 
                ep.EndpointId, ep.Nodes?.Count ?? 0);
            
            await _subs.EnsureSubscriptionAsync(
                endpointId: ep.EndpointId,
                session: session,
                cfg: ep,
                onNotification: notif => HandleNotification(ep, notif),
                onNodeDisabled: tagId => {
                    _logger.LogWarning("Node disabled: {TagId} on {EndpointId}", tagId, ep.EndpointId);
                    _health.MarkDegraded($"node_disabled endpoint={ep.EndpointId} tag={tagId}");
                },
                ct: ct).ConfigureAwait(false);

            _logger.LogInformation("Subscription created successfully for {EndpointId}", ep.EndpointId);
            return true;
        }
        catch (ServiceResultException sre)
        {
            _logger.LogError("Subscription failed for {EndpointId}: StatusCode={StatusCode}", 
                ep.EndpointId, sre.StatusCode);
            _health.MarkDegraded($"subscription_failed endpoint={ep.EndpointId} status={sre.StatusCode}");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Subscription failed for {EndpointId}: {Message}", ep.EndpointId, ex.Message);
            _health.MarkDegraded($"subscription_failed endpoint={ep.EndpointId} error={ex.Message}");
            return false;
        }
    }

    private void HandleNotification(OpcUaEndpointConfig ep, OpcUaDataNotification notif)
    {
        try
        {
            var dv = notif.Value;
            if (dv is null)
                return;

            var swStart = StopwatchMs();

            var nodeCfg = ep.Nodes.FirstOrDefault(n => n.TagId == notif.TagId);
            var hint = nodeCfg?.ValueTypeHint;

            var (vt, materialized) = _typeMapper.MapVariant(notif.TagId, hint, dv);
            var quality = _typeMapper.MapQuality(dv);

            var tp = BuildTelemetryPoint(
                deviceId: ep.EndpointId,
                tagId: notif.TagId,
                vt: vt,
                value: materialized,
                quality: quality,
                unit: nodeCfg?.Unit);

            if (_out.Writer.TryWrite(tp))
            {
                _logger.LogDebug("Received data: {TagId}={Value} (Type={ValueType})", 
                    notif.TagId, materialized, vt);
            }
            else
            {
                _logger.LogWarning("Channel full, dropped data point for {TagId}", notif.TagId);
            }

            var swElapsed = StopwatchMs() - swStart;
            _health.MarkSuccess(swElapsed);
        }
        catch (OpcUaTypeMismatchException ex)
        {
            _logger.LogWarning("Type mismatch for {TagId}: {Message}", notif.TagId, ex.Message);
            _health.MarkTypeMismatch(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling notification for {TagId}: {Message}", notif.TagId, ex.Message);
            _health.MarkDegraded($"notif_error endpoint={ep.EndpointId} tag={notif.TagId} error={ex.Message}");
        }
    }

    private async Task PollingLoopAsync(OpcUaEndpointConfig ep, ISession session, CancellationToken ct)
    {
        var intervalMs = Math.Max(100, ep.Subscription.PublishingIntervalMs);
        _logger.LogInformation("Starting polling loop for {EndpointId} with interval {Interval}ms", 
            ep.EndpointId, intervalMs);

        while (!ct.IsCancellationRequested && session.Connected)
        {
            var swStart = StopwatchMs();
            try
            {
                var nodes = new List<ReadValueId>();
                foreach (var n in ep.Nodes)
                {
                    nodes.Add(new ReadValueId
                    {
                        NodeId = NodeId.Parse(n.NodeId),
                        AttributeId = Attributes.Value
                    });
                }

                if (nodes.Count == 0)
                {
                    _logger.LogWarning("No nodes to poll for {EndpointId}", ep.EndpointId);
                    return;
                }

                session.Read(
                    requestHeader: null,
                    maxAge: 0,
                    timestampsToReturn: TimestampsToReturn.Source,
                    nodesToRead: new ReadValueIdCollection(nodes),
                    results: out DataValueCollection results,
                    diagnosticInfos: out _);

                _logger.LogDebug("Polled {Count} nodes from {EndpointId}", results.Count, ep.EndpointId);

                for (int i = 0; i < results.Count; i++)
                {
                    var n = ep.Nodes[i];
                    var dv = results[i];

                    if (StatusCode.IsBad(dv.StatusCode))
                    {
                        _logger.LogWarning("Bad status for {TagId}: {StatusCode}", n.TagId, dv.StatusCode);
                        _health.MarkDegraded($"poll_bad_status endpoint={ep.EndpointId} tag={n.TagId} status={dv.StatusCode}");
                        continue;
                    }

                    try
                    {
                        var (vt, materialized) = _typeMapper.MapVariant(n.TagId, n.ValueTypeHint, dv);
                        var quality = _typeMapper.MapQuality(dv);

                        var tp = BuildTelemetryPoint(
                            deviceId: ep.EndpointId,
                            tagId: n.TagId,
                            vt: vt,
                            value: materialized,
                            quality: quality,
                            unit: n.Unit);

                        _out.Writer.TryWrite(tp);
                        _logger.LogDebug("Polled: {TagId}={Value}", n.TagId, materialized);
                    }
                    catch (OpcUaTypeMismatchException ex)
                    {
                        _logger.LogWarning("Type mismatch polling {TagId}: {Message}", n.TagId, ex.Message);
                        _health.MarkTypeMismatch(ex.Message);
                    }
                }

                _health.MarkSuccess(StopwatchMs() - swStart);
            }
            catch (ServiceResultException sre)
            {
                _logger.LogError("Polling failed for {EndpointId}: {StatusCode}", ep.EndpointId, sre.StatusCode);
                _health.MarkDisconnected($"polling_failed endpoint={ep.EndpointId} status={sre.StatusCode}");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Polling error for {EndpointId}: {Message}", ep.EndpointId, ex.Message);
                _health.MarkDegraded($"polling_failed endpoint={ep.EndpointId} error={ex.Message}");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(intervalMs), ct).ConfigureAwait(false);
        }
    }

    private static TelemetryPoint BuildTelemetryPoint(
        string deviceId,
        string tagId,
        TagValueType vt,
        object value,
        int quality,
        string? unit)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        TelemetryPoint tp = vt switch
        {
            TagValueType.Bool => new TelemetryPoint
            {
                DeviceId = deviceId,
                TagId = tagId,
                Ts = now,
                Seq = TelemetryPoint.GenerateSeq(),
                ValueType = vt,
                BoolValue = (bool)value,
                Quality = quality,
                Unit = unit,
                Protocol = "opcua"
            },
            TagValueType.Int8 => new TelemetryPoint
            {
                DeviceId = deviceId,
                TagId = tagId,
                Ts = now,
                Seq = TelemetryPoint.GenerateSeq(),
                ValueType = vt,
                Int8Value = (sbyte)value,
                Quality = quality,
                Unit = unit,
                Protocol = "opcua"
            },
            TagValueType.UInt8 => new TelemetryPoint
            {
                DeviceId = deviceId,
                TagId = tagId,
                Ts = now,
                Seq = TelemetryPoint.GenerateSeq(),
                ValueType = vt,
                UInt8Value = (byte)value,
                Quality = quality,
                Unit = unit,
                Protocol = "opcua"
            },
            TagValueType.Int16 => new TelemetryPoint
            {
                DeviceId = deviceId,
                TagId = tagId,
                Ts = now,
                Seq = TelemetryPoint.GenerateSeq(),
                ValueType = vt,
                Int16Value = (short)value,
                Quality = quality,
                Unit = unit,
                Protocol = "opcua"
            },
            TagValueType.UInt16 => new TelemetryPoint
            {
                DeviceId = deviceId,
                TagId = tagId,
                Ts = now,
                Seq = TelemetryPoint.GenerateSeq(),
                ValueType = vt,
                UInt16Value = (ushort)value,
                Quality = quality,
                Unit = unit,
                Protocol = "opcua"
            },
            TagValueType.Int32 => new TelemetryPoint
            {
                DeviceId = deviceId,
                TagId = tagId,
                Ts = now,
                Seq = TelemetryPoint.GenerateSeq(),
                ValueType = vt,
                Int32Value = (int)value,
                Quality = quality,
                Unit = unit,
                Protocol = "opcua"
            },
            TagValueType.UInt32 => new TelemetryPoint
            {
                DeviceId = deviceId,
                TagId = tagId,
                Ts = now,
                Seq = TelemetryPoint.GenerateSeq(),
                ValueType = vt,
                UInt32Value = (uint)value,
                Quality = quality,
                Unit = unit,
                Protocol = "opcua"
            },
            TagValueType.Int64 => new TelemetryPoint
            {
                DeviceId = deviceId,
                TagId = tagId,
                Ts = now,
                Seq = TelemetryPoint.GenerateSeq(),
                ValueType = vt,
                Int64Value = (long)value,
                Quality = quality,
                Unit = unit,
                Protocol = "opcua"
            },
            TagValueType.UInt64 => new TelemetryPoint
            {
                DeviceId = deviceId,
                TagId = tagId,
                Ts = now,
                Seq = TelemetryPoint.GenerateSeq(),
                ValueType = vt,
                UInt64Value = (ulong)value,
                Quality = quality,
                Unit = unit,
                Protocol = "opcua"
            },
            TagValueType.Float32 => new TelemetryPoint
            {
                DeviceId = deviceId,
                TagId = tagId,
                Ts = now,
                Seq = TelemetryPoint.GenerateSeq(),
                ValueType = vt,
                Float32Value = (float)value,
                Quality = quality,
                Unit = unit,
                Protocol = "opcua"
            },
            TagValueType.Float64 => new TelemetryPoint
            {
                DeviceId = deviceId,
                TagId = tagId,
                Ts = now,
                Seq = TelemetryPoint.GenerateSeq(),
                ValueType = vt,
                Float64Value = (double)value,
                Quality = quality,
                Unit = unit,
                Protocol = "opcua"
            },
            TagValueType.String => new TelemetryPoint
            {
                DeviceId = deviceId,
                TagId = tagId,
                Ts = now,
                Seq = TelemetryPoint.GenerateSeq(),
                ValueType = vt,
                StringValue = (string)value,
                Quality = quality,
                Unit = unit,
                Protocol = "opcua"
            },
            TagValueType.DateTime => new TelemetryPoint
            {
                DeviceId = deviceId,
                TagId = tagId,
                Ts = now,
                Seq = TelemetryPoint.GenerateSeq(),
                ValueType = vt,
                Int64Value = (long)value,
                Quality = quality,
                Unit = unit,
                Protocol = "opcua"
            },
            TagValueType.ByteArray => new TelemetryPoint
            {
                DeviceId = deviceId,
                TagId = tagId,
                Ts = now,
                Seq = TelemetryPoint.GenerateSeq(),
                ValueType = vt,
                ByteArrayValue = (byte[])value,
                Quality = quality,
                Unit = unit,
                Protocol = "opcua"
            },
            _ => throw new InvalidOperationException($"Unsupported TagValueType for TelemetryPoint builder: {vt}")
        };

        return tp;
    }

    private static long StopwatchMs()
        => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static TimeSpan BackoffDelay(int step) => step switch
    {
        0 => TimeSpan.Zero,
        1 => TimeSpan.FromSeconds(1),
        2 => TimeSpan.FromSeconds(2),
        3 => TimeSpan.FromSeconds(5),
        4 => TimeSpan.FromSeconds(10),
        5 => TimeSpan.FromSeconds(30),
        _ => TimeSpan.FromSeconds(60),
    };

    /// <summary>
    /// 配置变更事件处理器
    /// </summary>
    private void OnConfigChangedHandler()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                _logger.LogInformation("Configuration change detected, reloading OPC UA collector...");
                await ReloadAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reload OPC UA collector");
            }
        });
    }

    /// <summary>
    /// 热重载：停止并重新启动采集器
    /// </summary>
    public async Task ReloadAsync(CancellationToken ct)
    {
        lock (_reloadLock)
        {
            if (_cts == null)
            {
                _logger.LogDebug("Collector not running, skip reload");
                return;
            }
        }

        _logger.LogInformation("Stopping OPC UA collector for reload...");
        // 热重载时不关闭 Channel，保持消费者继续运行
        await StopAsync(ct, completeChannel: false);

        // 等待连接完全关闭
        await Task.Delay(500, ct);

        _logger.LogInformation("Restarting OPC UA collector with new configuration...");
        await StartAsync(ct);
    }

    public void Dispose()
    {
        // 取消订阅配置变更事件
        if (_dbConfigProvider != null)
        {
            _dbConfigProvider.OnConfigChanged -= OnConfigChangedHandler;
        }
        
        _cts?.Cancel();
        _cts?.Dispose();
        
        // 确保 Channel 被关闭
        _out.Writer.TryComplete();
    }
}
