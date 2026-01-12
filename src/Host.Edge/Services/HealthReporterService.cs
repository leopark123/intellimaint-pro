using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Options;

namespace IntelliMaint.Host.Edge.Services;

/// <summary>
/// 健康上报服务
/// 定期收集采集器健康状态并上报到 API
/// </summary>
public sealed class HealthReporterService : BackgroundService
{
    private readonly IEnumerable<ICollector> _collectors;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EdgeOptions _options;
    private readonly ILogger<HealthReporterService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // 上报间隔（秒）
    private const int ReportIntervalSeconds = 30;

    public HealthReporterService(
        IEnumerable<ICollector> collectors,
        IHttpClientFactory httpClientFactory,
        IOptions<EdgeOptions> options,
        ILogger<HealthReporterService> logger)
    {
        _collectors = collectors;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("HealthReporterService started. Reporting interval: {Interval}s", ReportIntervalSeconds);

        // 等待采集器启动
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReportHealthAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to report health, will retry in {Interval}s", ReportIntervalSeconds);
            }

            await Task.Delay(TimeSpan.FromSeconds(ReportIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("HealthReporterService stopped.");
    }

    private async Task ReportHealthAsync(CancellationToken ct)
    {
        // 1. 收集所有采集器的健康状态
        var collectors = new Dictionary<string, CollectorHealth>();

        foreach (var collector in _collectors)
        {
            try
            {
                var health = collector.GetHealth();
                collectors[collector.Protocol] = health;

                _logger.LogDebug(
                    "Collector {Protocol}: State={State}, Errors={Errors}, Latency={Latency}ms",
                    collector.Protocol, health.State, health.ConsecutiveErrors, health.AvgLatencyMs);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get health from collector {Protocol}", collector.Protocol);

                // 记录错误状态
                collectors[collector.Protocol] = new CollectorHealth
                {
                    Protocol = collector.Protocol,
                    State = CollectorState.Disconnected,
                    LastError = ex.Message,
                    ConsecutiveErrors = 1
                };
            }
        }

        // 2. 计算整体状态
        var overallState = CalculateOverallState(collectors.Values);

        // 3. 构建健康快照
        var snapshot = new HealthSnapshot
        {
            UtcTime = DateTimeOffset.UtcNow,
            OverallState = overallState,
            DatabaseState = DatabaseState.Healthy, // Edge 不直接操作数据库，默认健康
            QueueState = QueueState.Normal,
            QueueDepth = 0,
            DroppedPoints = 0,
            WriteLatencyMsP95 = 0,
            Collectors = collectors,
            MqttConnected = false,
            OutboxDepth = 0,
            MemoryUsedMb = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024)
        };

        // 4. 上报到 API
        await PostSnapshotAsync(snapshot, ct);

        _logger.LogInformation(
            "Health reported: OverallState={State}, Collectors={Count}, Memory={Memory}MB",
            overallState, collectors.Count, snapshot.MemoryUsedMb);
    }

    private static HealthState CalculateOverallState(IEnumerable<CollectorHealth> collectors)
    {
        var collectorList = collectors.ToList();

        if (collectorList.Count == 0)
            return HealthState.NotReady;

        // 任何采集器断开 -> NotReady
        if (collectorList.Any(c => c.State == CollectorState.Disconnected))
            return HealthState.NotReady;

        // 任何采集器降级 -> Degraded
        if (collectorList.Any(c => c.State == CollectorState.Degraded))
            return HealthState.Degraded;

        // 所有采集器正常 -> Healthy
        return HealthState.Healthy;
    }

    private async Task PostSnapshotAsync(HealthSnapshot snapshot, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("ApiClient");

        var apiBaseUrl = _options.ApiBaseUrl?.TrimEnd('/') ?? "http://localhost:5000";
        var url = $"{apiBaseUrl}/api/health/snapshot";

        using var response = await client.PostAsJsonAsync(url, snapshot, JsonOptions, ct);

        if (!response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "Failed to post health snapshot: {StatusCode} - {Content}",
                response.StatusCode, content);
        }
    }
}
