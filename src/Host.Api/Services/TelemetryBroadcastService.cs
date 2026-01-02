using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using IntelliMaint.Host.Api.Hubs;
using Microsoft.AspNetCore.SignalR;
using Serilog;

namespace IntelliMaint.Host.Api.Services;

/// <summary>
/// Background service that broadcasts telemetry data via SignalR.
/// v56: 优化版本 - 添加缓存、超时保护、降低查询频率
/// </summary>
public sealed class TelemetryBroadcastService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<TelemetryHub> _hubContext;
    private readonly CacheService _cache;

    // Track last timestamp per tag to detect changes
    // Key = "deviceId|tagId" -> lastTs
    private readonly Dictionary<string, long> _lastTsByKey = new(StringComparer.Ordinal);
    
    // v56: 降低频率从1秒到2秒，减少数据库压力
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(2);
    
    // v56: 查询超时保护（5秒）
    private readonly TimeSpan _queryTimeout = TimeSpan.FromSeconds(5);
    
    // v56: 连续失败计数
    private int _consecutiveFailures = 0;
    private const int MaxConsecutiveFailures = 5;

    public TelemetryBroadcastService(
        IServiceScopeFactory scopeFactory, 
        IHubContext<TelemetryHub> hubContext,
        CacheService cache)
    {
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _cache = cache;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Log.Information("TelemetryBroadcastService started (interval: {Interval}s)", _interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await BroadcastAsync(stoppingToken);
                _consecutiveFailures = 0; // 重置失败计数
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _consecutiveFailures++;
                
                if (_consecutiveFailures >= MaxConsecutiveFailures)
                {
                    Log.Warning("TelemetryBroadcastService: {Failures} consecutive failures, backing off...", 
                        _consecutiveFailures);
                    
                    // 退避：连续失败时增加等待时间
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                    }
                    catch (OperationCanceledException) { break; }
                }
                else
                {
                    Log.Error(ex, "TelemetryBroadcastService tick failed ({Failures}/{Max})", 
                        _consecutiveFailures, MaxConsecutiveFailures);
                }
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        Log.Information("TelemetryBroadcastService stopped");
    }

    private async Task BroadcastAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();

        // v56: 添加查询超时保护
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_queryTimeout);

        IReadOnlyList<TelemetryPoint> latest;
        try
        {
            latest = await repo.GetLatestAsync(deviceId: null, tagId: null, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            Log.Warning("TelemetryBroadcastService: GetLatestAsync timed out after {Timeout}s", 
                _queryTimeout.TotalSeconds);
            return;
        }

        if (latest.Count == 0)
            return;

        // Filter to only changed points (by timestamp)
        var changed = new List<TelemetryDto>(capacity: latest.Count);

        foreach (var p in latest)
        {
            var key = MakeKey(p.DeviceId, p.TagId);
            
            if (_lastTsByKey.TryGetValue(key, out var lastTs) && lastTs >= p.Ts)
            {
                continue; // No change
            }

            _lastTsByKey[key] = p.Ts;

            changed.Add(new TelemetryDto
            {
                DeviceId = p.DeviceId,
                TagId = p.TagId,
                Ts = p.Ts,
                Value = ExtractValue(p),
                ValueType = p.ValueType.ToString(),
                Quality = p.Quality,
                Unit = p.Unit
            });
        }

        if (changed.Count == 0)
            return;

        // Broadcast to "all" group
        await _hubContext.Clients.Group("all").SendAsync("ReceiveData", changed, ct);

        // Also broadcast to device-specific groups
        foreach (var deviceGroup in changed.GroupBy(x => x.DeviceId, StringComparer.Ordinal))
        {
            var groupName = $"device:{deviceGroup.Key}";
            await _hubContext.Clients.Group(groupName).SendAsync("ReceiveData", deviceGroup.ToList(), ct);
        }

        Log.Debug("Broadcasted {Count} telemetry points", changed.Count);
    }

    private static string MakeKey(string deviceId, string tagId) => $"{deviceId}|{tagId}";

    /// <summary>
    /// Extract the actual value from TelemetryPoint based on its ValueType
    /// </summary>
    private static object? ExtractValue(TelemetryPoint p)
    {
        return p.ValueType switch
        {
            TagValueType.Bool => p.BoolValue,
            TagValueType.Int8 => p.Int8Value,
            TagValueType.UInt8 => p.UInt8Value,
            TagValueType.Int16 => p.Int16Value,
            TagValueType.UInt16 => p.UInt16Value,
            TagValueType.Int32 => p.Int32Value,
            TagValueType.UInt32 => p.UInt32Value,
            TagValueType.Int64 => p.Int64Value,
            TagValueType.UInt64 => p.UInt64Value,
            TagValueType.Float32 => p.Float32Value,
            TagValueType.Float64 => p.Float64Value,
            TagValueType.String => p.StringValue,
            TagValueType.ByteArray => p.ByteArrayValue != null ? Convert.ToBase64String(p.ByteArrayValue) : null,
            _ => null
        };
    }

    /// <summary>
    /// DTO for SignalR broadcast (using camelCase for JSON serialization)
    /// </summary>
    private sealed class TelemetryDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("deviceId")]
        public required string DeviceId { get; init; }
        
        [System.Text.Json.Serialization.JsonPropertyName("tagId")]
        public required string TagId { get; init; }
        
        [System.Text.Json.Serialization.JsonPropertyName("ts")]
        public required long Ts { get; init; }
        
        [System.Text.Json.Serialization.JsonPropertyName("value")]
        public object? Value { get; init; }
        
        [System.Text.Json.Serialization.JsonPropertyName("valueType")]
        public required string ValueType { get; init; }
        
        [System.Text.Json.Serialization.JsonPropertyName("quality")]
        public int Quality { get; init; }
        
        [System.Text.Json.Serialization.JsonPropertyName("unit")]
        public string? Unit { get; init; }
    }
}
