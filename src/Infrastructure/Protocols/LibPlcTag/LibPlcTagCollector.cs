using System.Runtime.CompilerServices;
using System.Threading.Channels;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IntelliMaint.Infrastructure.Protocols.LibPlcTag;

/// <summary>
/// libplctag main collector.
/// - Implements ICollector + ITelemetrySource (outputs TelemetryPoint stream)
/// - TagGroup scheduling (Fast/Normal/Slow) per PLC
/// - Independent loops per PLC per TagGroup (fault isolation)
/// - Strict type mapping (Fail Fast) using ITagTypeMapper
/// - v55: Supports simulation mode and database configuration
/// </summary>
public sealed class LibPlcTagCollector : ICollector, ITelemetrySource, IDisposable
{
    private readonly IOptions<LibPlcTagOptions> _options;
    private readonly LibPlcTagConnectionPool _pool;
    private readonly LibPlcTagTagReader _reader;
    private readonly SimulatedTagReader _simulatedReader;
    private readonly ITagTypeMapper _typeMapper;
    private readonly LibPlcTagHealthChecker _health;
    private readonly ILogger<LibPlcTagCollector> _logger;

    // v55: 数据库配置支持
    private readonly ILibPlcTagConfigAdapter? _configAdapter;
    private readonly IDbConfigProvider? _dbConfigProvider;
    private volatile LibPlcTagOptions _currentOptions;
    private readonly object _reloadLock = new();

    private readonly Channel<TelemetryPoint> _output;
    private CancellationTokenSource? _cts;
    private readonly List<Task> _loops = new();

    public string Protocol => "libplctag";

    public LibPlcTagCollector(
        IOptions<LibPlcTagOptions> options,
        LibPlcTagConnectionPool pool,
        LibPlcTagTagReader reader,
        SimulatedTagReader simulatedReader,
        ITagTypeMapper typeMapper,
        LibPlcTagHealthChecker health,
        ILogger<LibPlcTagCollector> logger,
        ILibPlcTagConfigAdapter? configAdapter = null,
        IDbConfigProvider? dbConfigProvider = null)
    {
        _options = options;
        _pool = pool;
        _reader = reader;
        _simulatedReader = simulatedReader;
        _typeMapper = typeMapper;
        _health = health;
        _logger = logger;
        _configAdapter = configAdapter;
        _dbConfigProvider = dbConfigProvider;
        _currentOptions = options.Value;

        // Internal output channel: bounded, drop oldest on full
        _output = Channel.CreateBounded<TelemetryPoint>(new BoundedChannelOptions(100_000)
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
        if (_cts != null) return;

        // 优先从数据库加载配置
        if (_configAdapter != null)
        {
            try
            {
                var dbOptions = await _configAdapter.LoadFromDatabaseAsync(ct);
                if (dbOptions.Enabled && dbOptions.Plcs.Count > 0)
                {
                    _logger.LogInformation(
                        "Using database configuration: {PlcCount} PLCs",
                        dbOptions.Plcs.Count);
                    
                    // 继承 appsettings 的 SimulationMode
                    dbOptions = dbOptions with { SimulationMode = _options.Value.SimulationMode };
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
            _logger.LogInformation("LibPlcTag collector is disabled");
            return;
        }

        if (opt.SimulationMode)
        {
            _logger.LogWarning("⚠️ LibPlcTag collector running in SIMULATION MODE - no real PLC connection");
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _logger.LogInformation("Starting LibPlcTag collector with {PlcCount} PLCs (SimulationMode={SimMode})", 
            opt.Plcs.Count, opt.SimulationMode);

        foreach (var plc in opt.Plcs)
        {
            foreach (var group in plc.TagGroups)
            {
                if (group.Tags.Count == 0) continue;

                var loop = Task.Run(() => RunPlcGroupLoopAsync(plc, group, opt.SimulationMode, _cts.Token), _cts.Token);
                _loops.Add(loop);
                
                _logger.LogInformation("Started loop for PLC {PlcId} group {GroupName} with {TagCount} tags, interval {IntervalMs}ms",
                    plc.PlcId, group.Name, group.Tags.Count, group.ScanIntervalMs);
            }
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        await StopAsync(ct, completeChannel: true);
    }

    /// <summary>
    /// 停止采集器
    /// </summary>
    private async Task StopAsync(CancellationToken ct, bool completeChannel)
    {
        if (_cts == null) return;

        _logger.LogInformation("Stopping LibPlcTag collector...");
        
        _cts.Cancel();
        
        try
        {
            await Task.WhenAll(_loops).WaitAsync(TimeSpan.FromSeconds(10), ct);
        }
        catch (OperationCanceledException) { }
        catch (TimeoutException)
        {
            _logger.LogWarning("Timeout waiting for collector loops to stop");
        }
        finally
        {
            _loops.Clear();
            _cts.Dispose();
            _cts = null;
            
            if (completeChannel)
            {
                _output.Writer.TryComplete();
            }
        }

        _logger.LogInformation("LibPlcTag collector stopped");
    }

    public CollectorHealth GetHealth() => _health.GetHealth();

    public async IAsyncEnumerable<TelemetryPoint> ReadAsync([EnumeratorCancellation] CancellationToken ct)
    {
        while (await _output.Reader.WaitToReadAsync(ct))
        {
            while (_output.Reader.TryRead(out var point))
            {
                yield return point;
            }
        }
    }

    private async Task RunPlcGroupLoopAsync(PlcEndpointConfig plc, TagGroupConfig group, bool simulationMode, CancellationToken ct)
    {
        var intervalMs = group.ScanIntervalMs > 0 ? group.ScanIntervalMs : 100;
        var disabledTags = new HashSet<string>();
        var backoffStep = 0;

        _logger.LogDebug("Loop started: PLC={PlcId} Group={GroupName} Interval={IntervalMs}ms SimMode={SimMode}",
            plc.PlcId, group.Name, intervalMs, simulationMode);

        while (!ct.IsCancellationRequested)
        {
            var started = DateTimeOffset.UtcNow;

            try
            {
                IReadOnlyList<TagReadResult> results;

                if (simulationMode)
                {
                    // 使用模拟器读取
                    results = await _simulatedReader.ReadBatchAsync(group, plc, ct);
                }
                else
                {
                    // 真实 PLC 读取
                    var tagGroup = _pool.AcquireTagGroup(plc, group);
                    results = await _reader.ReadBatchAsync(tagGroup, plc, ct);
                }

                var anySuccess = false;

                foreach (var result in results)
                {
                    // Skip disabled tags
                    if (disabledTags.Contains(result.TagId))
                        continue;

                    if (!result.Success)
                    {
                        _health.MarkReadFail(result.Error, result.ErrorMessage);
                        if (!simulationMode)
                        {
                            HandleReadError(plc, result, disabledTags, ref backoffStep);
                        }
                        continue;
                    }

                    anySuccess = true;
                    _health.MarkReadOk(result.LatencyMs);

                    try
                    {
                        // Map to TelemetryPoint using type mapper
                        var expectedType = _typeMapper.MapType(Protocol, result.TagId, result.TagConfig.CipType, null);
                        var point = _typeMapper.MapValue(
                            deviceId: result.DeviceId,
                            tagId: result.TagId,
                            expectedType: expectedType,
                            rawValue: result.RawValue!,
                            quality: result.Quality,
                            protocol: Protocol);

                        // Add unit from config
                        if (!string.IsNullOrEmpty(result.TagConfig.Unit))
                        {
                            point = point with { Unit = result.TagConfig.Unit };
                        }

                        // Write to output channel
                        _output.Writer.TryWrite(point);
                    }
                    catch (LibPlcTagTypeMismatchException ex)
                    {
                        _health.MarkReadFail(LibPlcTagError.TYPE_MISMATCH, ex.Message);
                        _logger.LogWarning("Type mismatch for tag {TagId}: {Message}", result.TagId, ex.Message);
                        // Drop by design (Fail Fast)
                    }
                }

                // Reset backoff on any success
                if (anySuccess)
                {
                    backoffStep = 0;
                }

                // Update connection stats
                if (simulationMode)
                {
                    _health.UpdateConnectionStats(1, group.Tags.Count, group.Tags.Count);
                }
                else
                {
                    _health.UpdateConnectionStats(
                        _pool.GetActiveConnectionCount(plc.PlcId),
                        group.Tags.Count,
                        group.Tags.Count - disabledTags.Count);
                }
            }
            catch (LibPlcTagPoolBusyException ex)
            {
                _health.MarkReadFail(LibPlcTagError.TOO_MANY_CONN, ex.Message);
                _logger.LogWarning("Pool busy for PLC {PlcId}: {Message}", plc.PlcId, ex.Message);
                await Task.Delay(Math.Min(50, intervalMs), ct);
            }
            catch (LibPlcTagPoolFaultedException ex)
            {
                _health.MarkReadFail(LibPlcTagError.NO_ROUTE, ex.Message);
                _logger.LogWarning("Pool faulted for PLC {PlcId}: {Message}", plc.PlcId, ex.Message);
                backoffStep = Math.Min(backoffStep + 1, 6);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _health.MarkReadFail(LibPlcTagError.UNKNOWN, ex.Message);
                _logger.LogError(ex, "Unexpected error in loop for PLC {PlcId} group {GroupName}",
                    plc.PlcId, group.Name);
            }

            // Calculate delay for next iteration
            var elapsed = DateTimeOffset.UtcNow - started;
            var delay = TimeSpan.FromMilliseconds(Math.Max(0, intervalMs - elapsed.TotalMilliseconds));

            // Apply backoff if needed (only for real PLC)
            if (!simulationMode && backoffStep > 0)
            {
                var backoff = BackoffDelay(backoffStep);
                if (backoff > delay)
                    delay = backoff;
            }

            if (delay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(delay, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogDebug("Loop stopped: PLC={PlcId} Group={GroupName}", plc.PlcId, group.Name);
    }

    private void HandleReadError(
        PlcEndpointConfig plc,
        TagReadResult result,
        HashSet<string> disabledTags,
        ref int backoffStep)
    {
        switch (result.Error)
        {
            case LibPlcTagError.TIMEOUT:
                _pool.MarkDegraded(plc.PlcId, "TIMEOUT");
                _logger.LogWarning("Tag {TagId} timeout", result.TagId);
                break;

            case LibPlcTagError.NO_ROUTE:
                _pool.MarkFaulted(plc.PlcId, "NO_ROUTE");
                backoffStep = Math.Min(backoffStep + 1, 6);
                _logger.LogError("No route to PLC {PlcId}", plc.PlcId);
                break;

            case LibPlcTagError.BAD_TAG:
                // Stop this tag
                disabledTags.Add(result.TagId);
                _logger.LogWarning("Tag {TagId} disabled (BAD_TAG)", result.TagId);
                break;

            case LibPlcTagError.TOO_MANY_CONN:
                _pool.MarkFaulted(plc.PlcId, "TOO_MANY_CONN");
                _logger.LogWarning("Too many connections for PLC {PlcId}", plc.PlcId);
                break;

            case LibPlcTagError.TYPE_MISMATCH:
                _pool.MarkDegraded(plc.PlcId, "TYPE_MISMATCH");
                _logger.LogWarning("Type mismatch for tag {TagId}", result.TagId);
                break;
        }
    }

    private static TimeSpan BackoffDelay(int step) => step switch
    {
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
                _logger.LogInformation("Configuration change detected, reloading LibPlcTag collector...");
                await ReloadAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reload LibPlcTag collector");
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

        _logger.LogInformation("Stopping LibPlcTag collector for reload...");
        await StopAsync(ct, completeChannel: false);

        await Task.Delay(500, ct);

        _logger.LogInformation("Restarting LibPlcTag collector with new configuration...");
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
        _output.Writer.TryComplete();
    }
}
