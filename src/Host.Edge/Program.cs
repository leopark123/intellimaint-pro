using IntelliMaint.Core.Contracts;
using IntelliMaint.Host.Edge.Services;
using IntelliMaint.Infrastructure.Pipeline;
using IntelliMaint.Infrastructure.Protocols.LibPlcTag;
using IntelliMaint.Infrastructure.Protocols.OpcUa;
using IntelliMaint.Infrastructure.TimescaleDb;
using Microsoft.Extensions.Configuration;
using Serilog;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .WriteTo.File("logs/edge-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    Log.Information("Starting IntelliMaint Edge...");

    var builder = Host.CreateApplicationBuilder(args);
    
    // Configure Serilog
    builder.Services.AddSerilog();

    // Bind configuration options
    builder.Services.Configure<EdgeOptions>(
        builder.Configuration.GetSection(EdgeOptions.SectionName));
    builder.Services.Configure<ChannelCapacityOptions>(
        builder.Configuration.GetSection("ChannelCapacity"));
    builder.Services.Configure<MqttOptions>(
        builder.Configuration.GetSection(MqttOptions.SectionName));

    // v65: Store & Forward 配置
    builder.Services.Configure<StoreForwardOptions>(
        builder.Configuration.GetSection(StoreForwardOptions.SectionName));

    // Add infrastructure services
    builder.Services.AddTimescaleDbInfrastructure();
    builder.Services.AddPipelineInfrastructure();
    
    // Add protocol collectors
    builder.Services.AddLibPlcTagCollector(builder.Configuration);
    builder.Services.AddOpcUaCollector(builder.Configuration);

    // TODO: Add MQTT publisher when ready
    // builder.Services.AddMqttPublisher(builder.Configuration);

    // Add HttpClient for API calls
    builder.Services.AddHttpClient("ApiClient", client =>
    {
        client.Timeout = TimeSpan.FromSeconds(10);
    });

    // v65: HttpClient for config sync
    builder.Services.AddHttpClient("ConfigSync", client =>
    {
        client.Timeout = TimeSpan.FromSeconds(10);
    });

    // v65: HttpClient for telemetry upload
    builder.Services.AddHttpClient("Telemetry", client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
    });

    // v65: Edge 预处理与断网续传服务
    builder.Services.AddSingleton<FileRollingStore>();
    builder.Services.AddSingleton<ConfigSyncService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<ConfigSyncService>());
    builder.Services.AddSingleton<EdgeDataProcessor>();
    builder.Services.AddSingleton<StoreAndForwardService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<StoreAndForwardService>());

    // Add the main edge worker
    builder.Services.AddHostedService<EdgeWorker>();

    // Add health reporter service (reports collector health to API)
    builder.Services.AddHostedService<HealthReporterService>();

    var host = builder.Build();

    // Initialize database
    await host.Services.InitializeDatabaseAsync();

    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>
/// Main Edge Worker - coordinates collectors and pipeline
/// </summary>
public class EdgeWorker : BackgroundService
{
    private readonly IEnumerable<IntelliMaint.Core.Abstractions.ICollector> _collectors;
    private readonly IEnumerable<IntelliMaint.Core.Abstractions.ITelemetrySource> _sources;
    private readonly IntelliMaint.Infrastructure.Pipeline.TelemetryPipeline _pipeline;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EdgeWorker> _logger;

    public EdgeWorker(
        IEnumerable<IntelliMaint.Core.Abstractions.ICollector> collectors,
        IEnumerable<IntelliMaint.Core.Abstractions.ITelemetrySource> sources,
        IntelliMaint.Infrastructure.Pipeline.TelemetryPipeline pipeline,
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<EdgeWorker> logger)
    {
        _collectors = collectors;
        _sources = sources;
        _pipeline = pipeline;
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EdgeWorker starting...");
        
        // v55: 自动注册配置文件中的设备到数据库
        await AutoRegisterDevicesAsync(stoppingToken);
        
        _logger.LogInformation("Found {CollectorCount} collectors and {SourceCount} telemetry sources", 
            _collectors.Count(), _sources.Count());

        // 1. 先启动转发任务（消费者），确保 Channel 有人读取
        var forwardingTasks = new List<Task>();
        foreach (var source in _sources)
        {
            _logger.LogInformation("Creating forwarding task for source: {Protocol}", source.Protocol);
            var task = ForwardSourceToPipelineAsync(source, stoppingToken);
            forwardingTasks.Add(task);
        }
        _logger.LogInformation("Started {Count} forwarding tasks", forwardingTasks.Count);

        // 2. 再启动采集器（生产者）
        foreach (var collector in _collectors)
        {
            try
            {
                await collector.StartAsync(stoppingToken);
                _logger.LogInformation("Started collector: {Protocol}", collector.Protocol);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start collector: {Protocol}", collector.Protocol);
            }
        }

        _logger.LogInformation("EdgeWorker running. Press Ctrl+C to stop.");

        // Wait for all forwarding tasks or cancellation
        try
        {
            if (forwardingTasks.Count > 0)
            {
                await Task.WhenAll(forwardingTasks);
            }
            else
            {
                _logger.LogWarning("No telemetry sources found! Data will not be forwarded to pipeline.");
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected
        }

        // Stop all collectors
        _logger.LogInformation("EdgeWorker stopping...");
        foreach (var collector in _collectors)
        {
            try
            {
                await collector.StopAsync(stoppingToken);
                _logger.LogInformation("Stopped collector: {Protocol}", collector.Protocol);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping collector: {Protocol}", collector.Protocol);
            }
        }

        _logger.LogInformation("EdgeWorker stopped.");
    }

    private async Task ForwardSourceToPipelineAsync(IntelliMaint.Core.Abstractions.ITelemetrySource source, CancellationToken ct)
    {
        _logger.LogInformation("ForwardSourceToPipelineAsync started for {Protocol}", source.Protocol);
        long count = 0;
        try
        {
            await foreach (var point in source.ReadAsync(ct))
            {
                count++;
                if (_pipeline.Writer.TryWrite(point))
                {
                    if (count <= 5 || count % 100 == 0)
                    {
                        _logger.LogInformation("Forwarded point #{Count} from {Protocol}: TagId={TagId}, Value={Value}", 
                            count, source.Protocol, point.TagId, GetPointValue(point));
                    }
                }
                else
                {
                    _logger.LogWarning("Pipeline full, dropping point from {Protocol}", source.Protocol);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("Forwarding stopped for {Protocol}, total forwarded: {Count}", source.Protocol, count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error forwarding from {Protocol}", source.Protocol);
        }
    }
    
    /// <summary>
    /// v55: 自动注册配置文件中的设备和标签到数据库
    /// 如果设备已存在则跳过
    /// </summary>
    private async Task AutoRegisterDevicesAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var deviceRepo = scope.ServiceProvider.GetRequiredService<IntelliMaint.Core.Abstractions.IDeviceRepository>();
            var tagRepo = scope.ServiceProvider.GetRequiredService<IntelliMaint.Core.Abstractions.ITagRepository>();
            
            // 读取 LibPlcTag 配置
            var libPlcTagSection = _configuration.GetSection("Protocols:LibPlcTag");
            var enabled = libPlcTagSection.GetValue<bool>("Enabled");
            if (!enabled)
            {
                _logger.LogDebug("LibPlcTag is disabled, skipping device registration");
                return;
            }
            
            var plcs = libPlcTagSection.GetSection("Plcs").GetChildren();
            foreach (var plcSection in plcs)
            {
                var plcId = plcSection["PlcId"];
                if (string.IsNullOrEmpty(plcId)) continue;
                
                // 检查设备是否已存在
                var existingDevice = await deviceRepo.GetAsync(plcId, ct);
                if (existingDevice != null)
                {
                    _logger.LogDebug("Device {DeviceId} already exists, skipping registration", plcId);
                    continue;
                }
                
                // 创建设备
                var device = new IntelliMaint.Core.Contracts.DeviceDto
                {
                    DeviceId = plcId,
                    Name = plcSection["PlcType"] ?? "LibPlcTag PLC",
                    Protocol = "LibPlcTag",
                    Host = plcSection["IpAddress"] ?? "127.0.0.1",
                    Port = 44818,
                    Enabled = true,
                    CreatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    UpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Metadata = new Dictionary<string, string>
                    {
                        ["PlcType"] = plcSection["PlcType"] ?? "ControlLogix",
                        ["Path"] = plcSection["Path"] ?? "1,0",
                        ["Slot"] = plcSection["Slot"] ?? "0",
                        ["SimulationMode"] = libPlcTagSection.GetValue<bool>("SimulationMode").ToString()
                    }
                };
                
                await deviceRepo.UpsertAsync(device, ct);
                _logger.LogInformation("✅ Auto-registered device: {DeviceId} ({Protocol})", plcId, "LibPlcTag");
                
                // 注册标签
                var tagGroups = plcSection.GetSection("TagGroups").GetChildren();
                var tagCount = 0;
                foreach (var groupSection in tagGroups)
                {
                    var groupName = groupSection["Name"] ?? "Normal";
                    var tags = groupSection.GetSection("Tags").GetChildren();
                    
                    foreach (var tagSection in tags)
                    {
                        var tagId = tagSection["TagId"];
                        if (string.IsNullOrEmpty(tagId)) continue;
                        
                        // 检查标签是否已存在
                        var existingTag = await tagRepo.GetAsync(tagId, ct);
                        if (existingTag != null) continue;
                        
                        var cipType = tagSection["CipType"] ?? "REAL";
                        var dataType = MapCipTypeToDataType(cipType);
                        
                        var tag = new IntelliMaint.Core.Contracts.TagDto
                        {
                            TagId = tagId,
                            DeviceId = plcId,
                            Name = tagSection["Description"] ?? tagId,
                            Description = tagSection["Description"],
                            Unit = tagSection["Unit"],
                            DataType = dataType,
                            Address = tagSection["Name"], // PLC tag name
                            TagGroup = groupName,
                            Enabled = true,
                            CreatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                            UpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                            Metadata = new Dictionary<string, string>
                            {
                                ["CipType"] = cipType
                            }
                        };
                        
                        await tagRepo.UpsertAsync(tag, ct);
                        tagCount++;
                    }
                }
                
                if (tagCount > 0)
                {
                    _logger.LogInformation("✅ Auto-registered {TagCount} tags for device {DeviceId}", tagCount, plcId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to auto-register devices (non-fatal, continuing...)");
        }
    }
    
    private static TagValueType MapCipTypeToDataType(string cipType)
    {
        return cipType.ToUpperInvariant() switch
        {
            "BOOL" => TagValueType.Bool,
            "SINT" => TagValueType.Int8,
            "USINT" => TagValueType.UInt8,
            "INT" => TagValueType.Int16,
            "UINT" => TagValueType.UInt16,
            "DINT" => TagValueType.Int32,
            "UDINT" => TagValueType.UInt32,
            "LINT" => TagValueType.Int64,
            "ULINT" => TagValueType.UInt64,
            "REAL" => TagValueType.Float32,
            "LREAL" => TagValueType.Float64,
            "STRING" => TagValueType.String,
            _ => TagValueType.Float32
        };
    }
    
    private static object? GetPointValue(TelemetryPoint point)
    {
        return point.ValueType switch
        {
            TagValueType.Bool => point.BoolValue,
            TagValueType.Int8 => point.Int8Value,
            TagValueType.UInt8 => point.UInt8Value,
            TagValueType.Int16 => point.Int16Value,
            TagValueType.UInt16 => point.UInt16Value,
            TagValueType.Int32 => point.Int32Value,
            TagValueType.UInt32 => point.UInt32Value,
            TagValueType.Int64 => point.Int64Value,
            TagValueType.UInt64 => point.UInt64Value,
            TagValueType.Float32 => point.Float32Value,
            TagValueType.Float64 => point.Float64Value,
            TagValueType.String => point.StringValue,
            _ => null
        };
    }
}
