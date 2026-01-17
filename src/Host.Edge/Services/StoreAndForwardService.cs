using System.IO.Compression;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IntelliMaint.Host.Edge.Services;

/// <summary>
/// v65: 断网续传服务 - 网络监控 + 本地缓存 + 自动恢复
/// </summary>
public sealed class StoreAndForwardService : BackgroundService, IAsyncDisposable
{
    private readonly FileRollingStore _store;
    private readonly ConfigSyncService _configSync;
    private readonly EdgeDataProcessor _processor;
    private readonly HttpClient _httpClient;
    private readonly ILogger<StoreAndForwardService> _logger;
    private readonly Channel<IReadOnlyList<TelemetryPoint>> _sendChannel;

    private readonly string _apiBaseUrl;
    private readonly string _edgeId;

    private bool _isOnline = true;
    private int _consecutiveFailures;
    private long _totalSent;

    public StoreAndForwardService(
        FileRollingStore store,
        ConfigSyncService configSync,
        EdgeDataProcessor processor,
        IHttpClientFactory httpClientFactory,
        IOptions<EdgeOptions> edgeOptions,
        ILogger<StoreAndForwardService> logger)
    {
        _store = store;
        _configSync = configSync;
        _processor = processor;
        _httpClient = httpClientFactory.CreateClient("Telemetry");
        _logger = logger;
        _apiBaseUrl = edgeOptions.Value.ApiBaseUrl;
        _edgeId = edgeOptions.Value.EdgeId;

        _sendChannel = Channel.CreateBounded<IReadOnlyList<TelemetryPoint>>(
            new BoundedChannelOptions(100)
            {
                FullMode = BoundedChannelFullMode.Wait
            });
    }

    /// <summary>
    /// 是否在线
    /// </summary>
    public bool IsOnline => _isOnline;

    /// <summary>
    /// 发送数据点（主入口）
    /// </summary>
    public async ValueTask SendAsync(IReadOnlyList<TelemetryPoint> points, CancellationToken ct)
    {
        if (points.Count == 0) return;

        // 1. 预处理
        var processed = _processor.Process(points);
        if (processed.Count == 0) return;

        // 2. 发送到通道
        await _sendChannel.Writer.WriteAsync(processed, ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("StoreAndForwardService started");

        // 启动网络监控
        _ = MonitorNetworkAsync(stoppingToken);

        // 启动发送循环
        await SendLoopAsync(stoppingToken);
    }

    /// <summary>
    /// 发送循环
    /// </summary>
    private async Task SendLoopAsync(CancellationToken ct)
    {
        var batch = new List<TelemetryPoint>();
        var config = _configSync.NetworkConfig;
        var batchSize = config?.SendBatchSize ?? 500;
        var sendInterval = TimeSpan.FromMilliseconds(config?.SendIntervalMs ?? 500);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // 先尝试发送本地缓存
                if (_isOnline && _store.HasData)
                {
                    await DrainStoredDataAsync(ct);
                }

                // 从通道读取数据
                batch.Clear();
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(sendInterval);

                try
                {
                    while (batch.Count < batchSize)
                    {
                        var points = await _sendChannel.Reader.ReadAsync(timeoutCts.Token);
                        batch.AddRange(points);
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // 超时，发送当前批次
                }

                if (batch.Count == 0) continue;

                // 发送批次
                if (_isOnline)
                {
                    var success = await TrySendAsync(batch, ct);
                    if (!success)
                    {
                        // 发送失败，存入本地
                        await _store.StoreAsync(batch, ct);
                    }
                }
                else
                {
                    // 离线，直接存入本地
                    await _store.StoreAsync(batch, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Send loop error");
                await Task.Delay(1000, ct);
            }
        }
    }

    /// <summary>
    /// 网络监控
    /// </summary>
    private async Task MonitorNetworkAsync(CancellationToken ct)
    {
        var config = _configSync.NetworkConfig;
        var checkInterval = TimeSpan.FromMilliseconds(config?.HealthCheckIntervalMs ?? 5000);
        var timeout = TimeSpan.FromMilliseconds(config?.HealthCheckTimeoutMs ?? 3000);
        var offlineThreshold = config?.OfflineThreshold ?? 3;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(checkInterval, ct);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(timeout);

                var response = await _httpClient.GetAsync($"{_apiBaseUrl}/health/live", timeoutCts.Token);

                if (response.IsSuccessStatusCode)
                {
                    if (!_isOnline)
                    {
                        _logger.LogInformation("Network recovered, resuming transmission");
                    }
                    _isOnline = true;
                    _consecutiveFailures = 0;

                    // 上报心跳状态
                    await ReportStatusAsync(ct);
                }
                else
                {
                    HandleFailure();
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                HandleFailure();
            }
        }

        void HandleFailure()
        {
            _consecutiveFailures++;
            if (_consecutiveFailures >= offlineThreshold && _isOnline)
            {
                _isOnline = false;
                _logger.LogWarning("Network offline after {Count} failures, switching to store mode",
                    _consecutiveFailures);
            }
        }
    }

    /// <summary>
    /// 发送本地缓存数据
    /// </summary>
    private async Task DrainStoredDataAsync(CancellationToken ct)
    {
        _logger.LogInformation("Draining stored data, pending: {Count} points", _store.PendingPointCount);

        while (_store.HasData && _isOnline && !ct.IsCancellationRequested)
        {
            var batch = await _store.ReadBatchAsync(500, ct);
            if (batch == null) break;

            var success = await TrySendAsync(batch.Points, ct);
            if (success)
            {
                await _store.AcknowledgeAsync(batch.Id, ct);
            }
            else
            {
                // 发送失败，停止发送
                break;
            }
        }
    }

    /// <summary>
    /// 尝试发送数据
    /// </summary>
    private async Task<bool> TrySendAsync(IReadOnlyList<TelemetryPoint> points, CancellationToken ct)
    {
        try
        {
            var config = _configSync.StoreForwardConfig;
            HttpContent content;

            if (config?.CompressionEnabled == true)
            {
                // 压缩发送
                var json = JsonSerializer.Serialize(points);
                var compressed = Compress(Encoding.UTF8.GetBytes(json), config.CompressionAlgorithm);
                content = new ByteArrayContent(compressed);
                content.Headers.Add("Content-Encoding", config.CompressionAlgorithm.ToLower());
                content.Headers.Add("Content-Type", "application/json");
            }
            else
            {
                content = JsonContent.Create(points);
            }

            var response = await _httpClient.PostAsync(
                $"{_apiBaseUrl}/api/telemetry/batch",
                content,
                ct);

            if (response.IsSuccessStatusCode)
            {
                Interlocked.Add(ref _totalSent, points.Count);
                return true;
            }

            _logger.LogWarning("Failed to send data: {Status}", response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Send failed");
            return false;
        }
    }

    /// <summary>
    /// 压缩数据
    /// </summary>
    private static byte[] Compress(byte[] data, string algorithm)
    {
        using var output = new MemoryStream();

        if (algorithm.Equals("Brotli", StringComparison.OrdinalIgnoreCase))
        {
            using var brotli = new BrotliStream(output, CompressionLevel.Fastest);
            brotli.Write(data);
        }
        else
        {
            using var gzip = new GZipStream(output, CompressionLevel.Fastest);
            gzip.Write(data);
        }

        return output.ToArray();
    }

    /// <summary>
    /// 获取服务状态
    /// </summary>
    public ServiceStatus GetStatus()
    {
        var storeStats = _store.GetStats();
        var processorStats = _processor.GetStats();

        return new ServiceStatus
        {
            IsOnline = _isOnline,
            ConsecutiveFailures = _consecutiveFailures,
            TotalSent = Interlocked.Read(ref _totalSent),
            PendingPoints = storeStats.PendingPoints,
            StoredSizeMB = storeStats.TotalStoredBytes / (1024.0 * 1024.0),
            FilterRate = processorStats.FilterRate,
            TotalFiltered = processorStats.TotalFiltered
        };
    }

    /// <summary>
    /// 上报状态到 API
    /// </summary>
    public async Task ReportStatusAsync(CancellationToken ct)
    {
        try
        {
            var status = GetStatus();
            var dto = new EdgeStatusDto
            {
                EdgeId = _edgeId,
                IsOnline = status.IsOnline,
                PendingPoints = status.PendingPoints,
                FilterRate = status.FilterRate,
                SentCount = status.TotalSent,
                StoredMB = status.StoredSizeMB,
                LastHeartbeatUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Version = "1.0.0"
            };

            await _httpClient.PostAsJsonAsync(
                $"{_apiBaseUrl}/api/edge-config/{_edgeId}/heartbeat",
                dto,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to report status");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _store.Dispose();
        await ValueTask.CompletedTask;
    }
}

/// <summary>
/// 服务状态
/// </summary>
public class ServiceStatus
{
    public bool IsOnline { get; set; }
    public int ConsecutiveFailures { get; set; }
    public long TotalSent { get; set; }
    public int PendingPoints { get; set; }
    public double StoredSizeMB { get; set; }
    public double FilterRate { get; set; }
    public long TotalFiltered { get; set; }
}
