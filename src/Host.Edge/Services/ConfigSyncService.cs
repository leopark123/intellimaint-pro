using System.Net.Http.Json;
using System.Text.Json;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IntelliMaint.Host.Edge.Services;

/// <summary>
/// v65: 配置同步服务 - 定期从 API 拉取配置更新
/// </summary>
public sealed class ConfigSyncService : BackgroundService
{
    private readonly string _edgeId;
    private readonly string _apiBaseUrl;
    private readonly int _syncIntervalMs;
    private readonly HttpClient _httpClient;
    private readonly ILogger<ConfigSyncService> _logger;

    private EdgeConfigDto? _currentConfig;
    private Dictionary<string, TagProcessingConfigDto> _tagConfigs = new();
    private long _lastSyncedVersion;

    public event Action<EdgeConfigDto>? OnConfigChanged;
    public event Action<Dictionary<string, TagProcessingConfigDto>>? OnTagConfigsChanged;

    public ConfigSyncService(
        IOptions<EdgeOptions> edgeOptions,
        IHttpClientFactory httpClientFactory,
        ILogger<ConfigSyncService> logger)
    {
        _edgeId = edgeOptions.Value.EdgeId;
        _apiBaseUrl = edgeOptions.Value.ApiBaseUrl;
        _syncIntervalMs = edgeOptions.Value.ConfigSyncIntervalMs;
        _httpClient = httpClientFactory.CreateClient("ConfigSync");
        _logger = logger;
    }

    /// <summary>
    /// 获取当前预处理配置
    /// </summary>
    public ProcessingConfigDto? ProcessingConfig => _currentConfig?.Processing;

    /// <summary>
    /// 获取当前断网续传配置
    /// </summary>
    public StoreForwardConfigDto? StoreForwardConfig => _currentConfig?.StoreForward;

    /// <summary>
    /// 获取当前网络配置
    /// </summary>
    public NetworkConfigDto? NetworkConfig => _currentConfig?.Network;

    /// <summary>
    /// 获取标签级配置
    /// </summary>
    public TagProcessingConfigDto? GetTagConfig(string tagId)
    {
        return _tagConfigs.TryGetValue(tagId, out var config) ? config : null;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ConfigSyncService started for edge {EdgeId}", _edgeId);

        // 首次同步
        await SyncConfigAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_syncIntervalMs, stoppingToken);
                await SyncConfigAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Config sync failed, will retry");
            }
        }

        _logger.LogInformation("ConfigSyncService stopped");
    }

    /// <summary>
    /// 手动触发配置同步
    /// </summary>
    public async Task ForceSyncAsync(CancellationToken ct)
    {
        await SyncConfigAsync(ct);
    }

    private async Task SyncConfigAsync(CancellationToken ct)
    {
        try
        {
            // 1. 获取 Edge 配置
            var configUrl = $"{_apiBaseUrl}/api/edge-config/{_edgeId}";
            var response = await _httpClient.GetAsync(configUrl, ct);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ApiResponse<EdgeConfigDto>>(cancellationToken: ct);
                if (result?.Data != null)
                {
                    var newVersion = result.Data.UpdatedUtc ?? result.Data.CreatedUtc;

                    if (newVersion != _lastSyncedVersion)
                    {
                        _currentConfig = result.Data;
                        _lastSyncedVersion = newVersion;
                        _logger.LogInformation("Edge config updated, version: {Version}", newVersion);
                        OnConfigChanged?.Invoke(result.Data);
                    }
                }
            }
            else
            {
                _logger.LogWarning("Failed to fetch edge config: {Status}", response.StatusCode);
            }

            // 2. 获取标签配置
            var tagsUrl = $"{_apiBaseUrl}/api/edge-config/{_edgeId}/tags?pageSize=1000";
            var tagsResponse = await _httpClient.GetAsync(tagsUrl, ct);

            if (tagsResponse.IsSuccessStatusCode)
            {
                var tagsResult = await tagsResponse.Content.ReadFromJsonAsync<ApiResponse<PagedTagConfigResult>>(cancellationToken: ct);
                if (tagsResult?.Data?.Items != null)
                {
                    var newTagConfigs = tagsResult.Data.Items.ToDictionary(t => t.TagId, t => t);

                    // 检查是否有变化
                    if (!TagConfigsEqual(_tagConfigs, newTagConfigs))
                    {
                        _tagConfigs = newTagConfigs;
                        _logger.LogInformation("Tag configs updated, count: {Count}", newTagConfigs.Count);
                        OnTagConfigsChanged?.Invoke(newTagConfigs);
                    }
                }
            }
            else
            {
                _logger.LogWarning("Failed to fetch tag configs: {Status}", tagsResponse.StatusCode);
            }

            _logger.LogDebug("Config sync completed");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Config sync HTTP error: {Message}", ex.Message);
        }
    }

    private static bool TagConfigsEqual(
        Dictionary<string, TagProcessingConfigDto> a,
        Dictionary<string, TagProcessingConfigDto> b)
    {
        if (a.Count != b.Count) return false;

        foreach (var (key, value) in a)
        {
            if (!b.TryGetValue(key, out var other)) return false;
            if (value.Deadband != other.Deadband) return false;
            if (value.DeadbandPercent != other.DeadbandPercent) return false;
            if (value.MinIntervalMs != other.MinIntervalMs) return false;
            if (value.Bypass != other.Bypass) return false;
        }

        return true;
    }

    private class ApiResponse<T>
    {
        public bool Success { get; set; }
        public T? Data { get; set; }
        public string? Message { get; set; }
    }
}

// EdgeOptions 已移至 IntelliMaint.Core.Contracts.EdgeOptions
