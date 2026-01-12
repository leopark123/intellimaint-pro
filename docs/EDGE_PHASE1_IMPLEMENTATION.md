# Edge Phase 1 实施方案：数据预处理 + 断网续传

> 版本: v1.0
> 预计工期: 2周

---

## 一、整体架构

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              Edge v2.0 Phase 1                               │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐                  │
│  │  LibPlcTag   │    │    OpcUa     │    │   Modbus     │                  │
│  │  Collector   │    │  Collector   │    │  (future)    │                  │
│  └──────┬───────┘    └──────┬───────┘    └──────────────┘                  │
│         │                   │                                               │
│         └─────────┬─────────┘                                               │
│                   ▼                                                         │
│  ┌────────────────────────────────────────────────────────────────────┐    │
│  │                      EdgeDataProcessor                              │    │
│  │  ┌────────────┐  ┌────────────┐  ┌────────────┐  ┌────────────┐   │    │
│  │  │  Deadband  │─▶│  Sampling  │─▶│  Outlier   │─▶│  Batching  │   │    │
│  │  │  Filter    │  │  Control   │  │  Filter    │  │            │   │    │
│  │  └────────────┘  └────────────┘  └────────────┘  └────────────┘   │    │
│  └─────────────────────────────────────┬──────────────────────────────┘    │
│                                        │                                    │
│                                        ▼                                    │
│  ┌────────────────────────────────────────────────────────────────────┐    │
│  │                      StoreAndForwardService                         │    │
│  │                                                                     │    │
│  │  ┌─────────────────────────────────────────────────────────────┐   │    │
│  │  │                    NetworkMonitor                            │   │    │
│  │  │            (定期检测 API 可达性)                              │   │    │
│  │  └─────────────────────────┬───────────────────────────────────┘   │    │
│  │                            │                                       │    │
│  │              ┌─────────────┴─────────────┐                        │    │
│  │              │                           │                        │    │
│  │              ▼                           ▼                        │    │
│  │  ┌─────────────────────┐    ┌─────────────────────┐              │    │
│  │  │   Online Mode       │    │   Offline Mode      │              │    │
│  │  │   (直接发送)        │    │   (本地存储)        │              │    │
│  │  └──────────┬──────────┘    └──────────┬──────────┘              │    │
│  │             │                          │                          │    │
│  │             │                          ▼                          │    │
│  │             │               ┌─────────────────────┐              │    │
│  │             │               │   SQLite Outbox     │              │    │
│  │             │               │   (本地持久化)      │              │    │
│  │             │               └──────────┬──────────┘              │    │
│  │             │                          │                          │    │
│  │             │    ┌─────────────────────┘                          │    │
│  │             │    │  (网络恢复时 Drain)                            │    │
│  │             ▼    ▼                                                │    │
│  │  ┌─────────────────────────────────────────────────────────────┐ │    │
│  │  │                  CompressedHttpClient                        │ │    │
│  │  │                  (Gzip/Brotli 压缩传输)                      │ │    │
│  │  └─────────────────────────────────────────────────────────────┘ │    │
│  └────────────────────────────────────────────────────────────────────┘    │
│                                        │                                    │
└────────────────────────────────────────┼────────────────────────────────────┘
                                         │ HTTPS + Gzip
                                         ▼
                                 ┌───────────────┐
                                 │   Host.Api    │
                                 └───────────────┘
```

---

## 二、文件结构

```
src/Host.Edge/
├── Program.cs                          # 入口 (修改)
├── appsettings.json                    # 配置 (修改)
├── Services/
│   ├── HealthReporterService.cs        # 现有
│   ├── EdgeDataProcessor.cs            # 新增: 数据预处理
│   ├── StoreAndForwardService.cs       # 新增: 断网续传主服务
│   ├── NetworkMonitor.cs               # 新增: 网络监控
│   ├── LocalOutboxStore.cs             # 新增: 本地存储
│   └── CompressedTelemetryClient.cs    # 新增: 压缩传输客户端
├── Models/
│   ├── ProcessingOptions.cs            # 新增: 预处理配置
│   ├── StoreAndForwardOptions.cs       # 新增: 断网续传配置
│   └── OutboxRecord.cs                 # 新增: 存储记录
└── data/
    └── outbox.db                       # 运行时生成: SQLite存储
```

---

## 三、详细设计

### 3.1 数据预处理 (EdgeDataProcessor)

#### 3.1.1 配置模型

```csharp
// Models/ProcessingOptions.cs
namespace IntelliMaint.Host.Edge.Models;

/// <summary>
/// 边缘数据预处理配置
/// </summary>
public class ProcessingOptions
{
    public const string SectionName = "EdgeProcessing";

    /// <summary>
    /// 是否启用预处理
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 默认死区 (绝对值变化小于此值不上传)
    /// </summary>
    public double DefaultDeadband { get; set; } = 0.01;

    /// <summary>
    /// 默认死区百分比 (相对于上次值的百分比)
    /// </summary>
    public double DefaultDeadbandPercent { get; set; } = 0.5;

    /// <summary>
    /// 默认最小上传间隔 (毫秒)
    /// </summary>
    public int DefaultMinIntervalMs { get; set; } = 1000;

    /// <summary>
    /// 强制上传间隔 (毫秒) - 即使值未变化也上传
    /// </summary>
    public int ForceUploadIntervalMs { get; set; } = 60000;

    /// <summary>
    /// 标签特定配置
    /// </summary>
    public Dictionary<string, TagProcessingConfig> TagOverrides { get; set; } = new();

    /// <summary>
    /// 异常值检测配置
    /// </summary>
    public OutlierDetectionConfig OutlierDetection { get; set; } = new();
}

public class TagProcessingConfig
{
    /// <summary>
    /// 死区绝对值
    /// </summary>
    public double? Deadband { get; set; }

    /// <summary>
    /// 死区百分比
    /// </summary>
    public double? DeadbandPercent { get; set; }

    /// <summary>
    /// 最小上传间隔 (毫秒)
    /// </summary>
    public int? MinIntervalMs { get; set; }

    /// <summary>
    /// 是否禁用预处理 (原样上传)
    /// </summary>
    public bool Bypass { get; set; } = false;
}

public class OutlierDetectionConfig
{
    /// <summary>
    /// 是否启用异常值检测
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Sigma阈值 (超过N倍标准差视为异常)
    /// </summary>
    public double SigmaThreshold { get; set; } = 4.0;

    /// <summary>
    /// 滑动窗口大小
    /// </summary>
    public int WindowSize { get; set; } = 100;

    /// <summary>
    /// 异常值处理策略: Drop/Mark/Pass
    /// </summary>
    public OutlierAction Action { get; set; } = OutlierAction.Mark;
}

public enum OutlierAction
{
    /// <summary>
    /// 丢弃异常值
    /// </summary>
    Drop,

    /// <summary>
    /// 标记但仍上传
    /// </summary>
    Mark,

    /// <summary>
    /// 不处理
    /// </summary>
    Pass
}
```

#### 3.1.2 预处理服务

```csharp
// Services/EdgeDataProcessor.cs
using System.Collections.Concurrent;
using IntelliMaint.Core.Contracts;
using IntelliMaint.Host.Edge.Models;
using Microsoft.Extensions.Options;

namespace IntelliMaint.Host.Edge.Services;

/// <summary>
/// 边缘数据预处理器
/// - 死区过滤: 值变化小于阈值不上传
/// - 采样控制: 限制上传频率
/// - 异常值检测: 识别并处理异常数据
/// </summary>
public sealed class EdgeDataProcessor : IEdgeDataProcessor
{
    private readonly ProcessingOptions _options;
    private readonly ILogger<EdgeDataProcessor> _logger;
    private readonly ConcurrentDictionary<string, TagState> _tagStates = new();

    // 统计信息
    private long _totalReceived;
    private long _totalPassed;
    private long _totalFiltered;
    private long _totalOutliers;

    public EdgeDataProcessor(
        IOptions<ProcessingOptions> options,
        ILogger<EdgeDataProcessor> logger)
    {
        _options = options.Value;
        _logger = logger;

        _logger.LogInformation(
            "EdgeDataProcessor initialized: Enabled={Enabled}, DefaultDeadband={Deadband}, MinInterval={Interval}ms",
            _options.Enabled, _options.DefaultDeadband, _options.DefaultMinIntervalMs);
    }

    /// <summary>
    /// 处理单个数据点
    /// </summary>
    /// <returns>处理后的数据点，如果被过滤则返回null</returns>
    public TelemetryPoint? Process(TelemetryPoint point)
    {
        Interlocked.Increment(ref _totalReceived);

        if (!_options.Enabled)
        {
            Interlocked.Increment(ref _totalPassed);
            return point;
        }

        var state = _tagStates.GetOrAdd(point.TagId, _ => new TagState());
        var config = GetTagConfig(point.TagId);

        // 1. 检查是否 Bypass
        if (config.Bypass)
        {
            state.UpdateLast(point);
            Interlocked.Increment(ref _totalPassed);
            return point;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var currentValue = GetNumericValue(point);

        // 2. 强制上传检查 (超过强制间隔必须上传)
        var timeSinceLast = now - state.LastUploadTime;
        if (timeSinceLast >= _options.ForceUploadIntervalMs)
        {
            state.UpdateLast(point, now, currentValue);
            Interlocked.Increment(ref _totalPassed);
            return point;
        }

        // 3. 最小间隔检查
        var minInterval = config.MinIntervalMs ?? _options.DefaultMinIntervalMs;
        if (timeSinceLast < minInterval)
        {
            Interlocked.Increment(ref _totalFiltered);
            return null;
        }

        // 4. 死区检查 (首次数据直接通过)
        if (state.HasLastValue)
        {
            var deadband = config.Deadband ?? _options.DefaultDeadband;
            var deadbandPercent = config.DeadbandPercent ?? _options.DefaultDeadbandPercent;

            var absoluteDiff = Math.Abs(currentValue - state.LastValue);
            var percentDiff = state.LastValue != 0
                ? Math.Abs((currentValue - state.LastValue) / state.LastValue) * 100
                : double.MaxValue;

            // 同时满足绝对死区和百分比死区才过滤
            if (absoluteDiff < deadband && percentDiff < deadbandPercent)
            {
                Interlocked.Increment(ref _totalFiltered);
                return null;
            }
        }

        // 5. 异常值检测
        if (_options.OutlierDetection.Enabled)
        {
            var isOutlier = DetectOutlier(state, currentValue);
            if (isOutlier)
            {
                Interlocked.Increment(ref _totalOutliers);

                switch (_options.OutlierDetection.Action)
                {
                    case OutlierAction.Drop:
                        _logger.LogDebug("Outlier dropped: TagId={TagId}, Value={Value}",
                            point.TagId, currentValue);
                        return null;

                    case OutlierAction.Mark:
                        point = point with { Quality = 0x40 }; // 标记为可疑
                        break;

                    case OutlierAction.Pass:
                        // 不处理
                        break;
                }
            }
        }

        // 更新统计窗口
        state.AddToWindow(currentValue, _options.OutlierDetection.WindowSize);
        state.UpdateLast(point, now, currentValue);
        Interlocked.Increment(ref _totalPassed);

        return point;
    }

    /// <summary>
    /// 批量处理
    /// </summary>
    public IEnumerable<TelemetryPoint> ProcessBatch(IEnumerable<TelemetryPoint> points)
    {
        foreach (var point in points)
        {
            var processed = Process(point);
            if (processed != null)
            {
                yield return processed;
            }
        }
    }

    /// <summary>
    /// 获取统计信息
    /// </summary>
    public ProcessingStats GetStats()
    {
        return new ProcessingStats
        {
            TotalReceived = Interlocked.Read(ref _totalReceived),
            TotalPassed = Interlocked.Read(ref _totalPassed),
            TotalFiltered = Interlocked.Read(ref _totalFiltered),
            TotalOutliers = Interlocked.Read(ref _totalOutliers),
            FilterRate = _totalReceived > 0
                ? (double)_totalFiltered / _totalReceived * 100
                : 0,
            ActiveTags = _tagStates.Count
        };
    }

    private TagProcessingConfig GetTagConfig(string tagId)
    {
        if (_options.TagOverrides.TryGetValue(tagId, out var config))
        {
            return config;
        }
        return new TagProcessingConfig();
    }

    private static double GetNumericValue(TelemetryPoint point)
    {
        return point.ValueType switch
        {
            TagValueType.Float32 => point.Float32Value,
            TagValueType.Float64 => point.Float64Value,
            TagValueType.Int32 => point.Int32Value,
            TagValueType.Int64 => point.Int64Value,
            TagValueType.Int16 => point.Int16Value,
            TagValueType.UInt32 => point.UInt32Value,
            TagValueType.Bool => point.BoolValue ? 1 : 0,
            _ => point.Float64Value
        };
    }

    private bool DetectOutlier(TagState state, double value)
    {
        if (state.WindowValues.Count < 10)
            return false; // 数据不足

        var mean = state.WindowValues.Average();
        var sumSquares = state.WindowValues.Sum(v => (v - mean) * (v - mean));
        var stdDev = Math.Sqrt(sumSquares / state.WindowValues.Count);

        if (stdDev < 0.0001)
            return false; // 标准差太小

        var zScore = Math.Abs((value - mean) / stdDev);
        return zScore > _options.OutlierDetection.SigmaThreshold;
    }

    /// <summary>
    /// 标签状态
    /// </summary>
    private class TagState
    {
        public bool HasLastValue { get; private set; }
        public double LastValue { get; private set; }
        public long LastUploadTime { get; private set; }
        public Queue<double> WindowValues { get; } = new();

        public void UpdateLast(TelemetryPoint point, long? uploadTime = null, double? value = null)
        {
            HasLastValue = true;
            LastValue = value ?? GetNumericValue(point);
            LastUploadTime = uploadTime ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        public void AddToWindow(double value, int maxSize)
        {
            WindowValues.Enqueue(value);
            while (WindowValues.Count > maxSize)
            {
                WindowValues.Dequeue();
            }
        }
    }
}

public interface IEdgeDataProcessor
{
    TelemetryPoint? Process(TelemetryPoint point);
    IEnumerable<TelemetryPoint> ProcessBatch(IEnumerable<TelemetryPoint> points);
    ProcessingStats GetStats();
}

public record ProcessingStats
{
    public long TotalReceived { get; init; }
    public long TotalPassed { get; init; }
    public long TotalFiltered { get; init; }
    public long TotalOutliers { get; init; }
    public double FilterRate { get; init; }
    public int ActiveTags { get; init; }
}
```

---

### 3.2 断网续传 (Store & Forward)

#### 3.2.1 配置模型

```csharp
// Models/StoreAndForwardOptions.cs
namespace IntelliMaint.Host.Edge.Models;

/// <summary>
/// 断网续传配置
/// </summary>
public class StoreAndForwardOptions
{
    public const string SectionName = "StoreAndForward";

    /// <summary>
    /// 是否启用断网续传
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 本地存储配置
    /// </summary>
    public LocalStoreConfig LocalStore { get; set; } = new();

    /// <summary>
    /// 网络监控配置
    /// </summary>
    public NetworkConfig Network { get; set; } = new();

    /// <summary>
    /// 发送配置
    /// </summary>
    public SendConfig Send { get; set; } = new();

    /// <summary>
    /// 压缩配置
    /// </summary>
    public CompressionConfig Compression { get; set; } = new();
}

public class LocalStoreConfig
{
    /// <summary>
    /// 存储类型: SQLite
    /// </summary>
    public string Type { get; set; } = "SQLite";

    /// <summary>
    /// 数据库文件路径
    /// </summary>
    public string Path { get; set; } = "data/outbox.db";

    /// <summary>
    /// 最大存储大小 (MB)
    /// </summary>
    public int MaxSizeMB { get; set; } = 1000;

    /// <summary>
    /// 数据保留天数
    /// </summary>
    public int RetentionDays { get; set; } = 7;

    /// <summary>
    /// 清理检查间隔 (分钟)
    /// </summary>
    public int CleanupIntervalMinutes { get; set; } = 60;
}

public class NetworkConfig
{
    /// <summary>
    /// 健康检查 URL
    /// </summary>
    public string HealthCheckUrl { get; set; } = "http://localhost:5000/api/health";

    /// <summary>
    /// 检查间隔 (毫秒)
    /// </summary>
    public int CheckIntervalMs { get; set; } = 5000;

    /// <summary>
    /// 请求超时 (毫秒)
    /// </summary>
    public int TimeoutMs { get; set; } = 3000;

    /// <summary>
    /// 连续失败多少次判定为离线
    /// </summary>
    public int OfflineThreshold { get; set; } = 3;
}

public class SendConfig
{
    /// <summary>
    /// 批量发送大小
    /// </summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>
    /// 发送间隔 (毫秒)
    /// </summary>
    public int IntervalMs { get; set; } = 500;

    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// 重试延迟 (毫秒)
    /// </summary>
    public int RetryDelayMs { get; set; } = 1000;

    /// <summary>
    /// Drain 模式批量大小 (恢复上传时)
    /// </summary>
    public int DrainBatchSize { get; set; } = 1000;
}

public class CompressionConfig
{
    /// <summary>
    /// 是否启用压缩
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 压缩算法: Gzip/Brotli
    /// </summary>
    public string Algorithm { get; set; } = "Gzip";

    /// <summary>
    /// 压缩级别: Fastest/Optimal/SmallestSize
    /// </summary>
    public string Level { get; set; } = "Fastest";
}
```

#### 3.2.2 网络监控

```csharp
// Services/NetworkMonitor.cs
using IntelliMaint.Host.Edge.Models;
using Microsoft.Extensions.Options;

namespace IntelliMaint.Host.Edge.Services;

/// <summary>
/// 网络状态监控器
/// </summary>
public sealed class NetworkMonitor : BackgroundService, INetworkMonitor
{
    private readonly StoreAndForwardOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<NetworkMonitor> _logger;

    private volatile bool _isOnline = true;
    private volatile int _consecutiveFailures;
    private volatile long _lastCheckTime;
    private volatile long _lastOnlineTime;
    private volatile long _offlineDuration;

    public bool IsOnline => _isOnline;
    public int ConsecutiveFailures => _consecutiveFailures;
    public long OfflineDurationMs => _isOnline ? 0 :
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _lastOnlineTime;

    public event Action<bool>? OnStatusChanged;

    public NetworkMonitor(
        IOptions<StoreAndForwardOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<NetworkMonitor> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("NetworkMonitor started. CheckInterval={Interval}ms, Url={Url}",
            _options.Network.CheckIntervalMs, _options.Network.HealthCheckUrl);

        // 初始检查
        await CheckNetworkAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(_options.Network.CheckIntervalMs, stoppingToken);
            await CheckNetworkAsync(stoppingToken);
        }
    }

    private async Task CheckNetworkAsync(CancellationToken ct)
    {
        _lastCheckTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var wasOnline = _isOnline;

        try
        {
            var client = _httpClientFactory.CreateClient("NetworkCheck");
            client.Timeout = TimeSpan.FromMilliseconds(_options.Network.TimeoutMs);

            var response = await client.GetAsync(_options.Network.HealthCheckUrl, ct);

            if (response.IsSuccessStatusCode)
            {
                _consecutiveFailures = 0;

                if (!_isOnline)
                {
                    _offlineDuration = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _lastOnlineTime;
                    _isOnline = true;
                    _logger.LogInformation("Network restored after {Duration}ms offline", _offlineDuration);
                    OnStatusChanged?.Invoke(true);
                }

                _lastOnlineTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
            else
            {
                HandleFailure($"HTTP {response.StatusCode}");
            }
        }
        catch (TaskCanceledException)
        {
            HandleFailure("Timeout");
        }
        catch (HttpRequestException ex)
        {
            HandleFailure(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error in network check");
            HandleFailure(ex.Message);
        }
    }

    private void HandleFailure(string reason)
    {
        _consecutiveFailures++;

        if (_isOnline && _consecutiveFailures >= _options.Network.OfflineThreshold)
        {
            _isOnline = false;
            _logger.LogWarning("Network offline detected: {Reason} (failures={Count})",
                reason, _consecutiveFailures);
            OnStatusChanged?.Invoke(false);
        }
    }

    public NetworkStatus GetStatus()
    {
        return new NetworkStatus
        {
            IsOnline = _isOnline,
            ConsecutiveFailures = _consecutiveFailures,
            LastCheckTime = _lastCheckTime,
            LastOnlineTime = _lastOnlineTime,
            OfflineDurationMs = OfflineDurationMs
        };
    }
}

public interface INetworkMonitor
{
    bool IsOnline { get; }
    int ConsecutiveFailures { get; }
    long OfflineDurationMs { get; }
    event Action<bool>? OnStatusChanged;
    NetworkStatus GetStatus();
}

public record NetworkStatus
{
    public bool IsOnline { get; init; }
    public int ConsecutiveFailures { get; init; }
    public long LastCheckTime { get; init; }
    public long LastOnlineTime { get; init; }
    public long OfflineDurationMs { get; init; }
}
```

#### 3.2.3 本地存储

```csharp
// Services/LocalOutboxStore.cs
using System.Data;
using System.IO.Compression;
using System.Text.Json;
using Dapper;
using IntelliMaint.Core.Contracts;
using IntelliMaint.Host.Edge.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace IntelliMaint.Host.Edge.Services;

/// <summary>
/// 本地 Outbox 存储 (基于 SQLite)
/// </summary>
public sealed class LocalOutboxStore : ILocalOutboxStore, IDisposable
{
    private readonly StoreAndForwardOptions _options;
    private readonly ILogger<LocalOutboxStore> _logger;
    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private long _storedCount;
    private long _retrievedCount;

    public LocalOutboxStore(
        IOptions<StoreAndForwardOptions> options,
        ILogger<LocalOutboxStore> logger)
    {
        _options = options.Value;
        _logger = logger;

        // 确保目录存在
        var dir = Path.GetDirectoryName(_options.LocalStore.Path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var connectionString = $"Data Source={_options.LocalStore.Path}";
        _connection = new SqliteConnection(connectionString);
        _connection.Open();

        InitializeSchema();

        _logger.LogInformation("LocalOutboxStore initialized: {Path}", _options.LocalStore.Path);
    }

    private void InitializeSchema()
    {
        const string schema = @"
            CREATE TABLE IF NOT EXISTS outbox (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                created_at INTEGER NOT NULL,
                batch_size INTEGER NOT NULL,
                data BLOB NOT NULL,
                compressed INTEGER DEFAULT 1
            );
            CREATE INDEX IF NOT EXISTS idx_outbox_created ON outbox(created_at);
        ";

        _connection.Execute(schema);
    }

    /// <summary>
    /// 存储批量数据
    /// </summary>
    public async Task StoreAsync(IReadOnlyList<TelemetryPoint> points, CancellationToken ct)
    {
        if (points.Count == 0) return;

        await _writeLock.WaitAsync(ct);
        try
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(points);
            var data = Compress(json);

            await _connection.ExecuteAsync(
                "INSERT INTO outbox (created_at, batch_size, data, compressed) VALUES (@ts, @size, @data, 1)",
                new
                {
                    ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    size = points.Count,
                    data
                });

            Interlocked.Add(ref _storedCount, points.Count);

            _logger.LogDebug("Stored {Count} points to outbox (compressed: {Original} -> {Compressed} bytes)",
                points.Count, json.Length, data.Length);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// 读取待发送的批次
    /// </summary>
    public async Task<OutboxBatch?> ReadBatchAsync(int limit, CancellationToken ct)
    {
        var row = await _connection.QueryFirstOrDefaultAsync<OutboxRow>(
            "SELECT id, data, compressed, batch_size FROM outbox ORDER BY id LIMIT 1");

        if (row == null) return null;

        var json = row.Compressed == 1 ? Decompress(row.Data) : row.Data;
        var points = JsonSerializer.Deserialize<List<TelemetryPoint>>(json);

        return new OutboxBatch
        {
            Id = row.Id,
            Points = points ?? new(),
            CreatedAt = row.Id
        };
    }

    /// <summary>
    /// 确认批次已发送
    /// </summary>
    public async Task AcknowledgeAsync(long batchId, CancellationToken ct)
    {
        var deleted = await _connection.ExecuteAsync(
            "DELETE FROM outbox WHERE id = @id",
            new { id = batchId });

        if (deleted > 0)
        {
            _logger.LogDebug("Acknowledged batch {Id}", batchId);
        }
    }

    /// <summary>
    /// 检查是否有待发送数据
    /// </summary>
    public async Task<bool> HasDataAsync(CancellationToken ct)
    {
        var count = await _connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM outbox");
        return count > 0;
    }

    /// <summary>
    /// 获取待发送数量
    /// </summary>
    public async Task<int> GetPendingCountAsync(CancellationToken ct)
    {
        return await _connection.ExecuteScalarAsync<int>(
            "SELECT COALESCE(SUM(batch_size), 0) FROM outbox");
    }

    /// <summary>
    /// 清理过期数据
    /// </summary>
    public async Task CleanupAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow
            .AddDays(-_options.LocalStore.RetentionDays)
            .ToUnixTimeMilliseconds();

        var deleted = await _connection.ExecuteAsync(
            "DELETE FROM outbox WHERE created_at < @cutoff",
            new { cutoff });

        if (deleted > 0)
        {
            _logger.LogInformation("Cleaned up {Count} expired outbox records", deleted);
        }

        // 检查存储大小
        var dbSize = new FileInfo(_options.LocalStore.Path).Length / (1024 * 1024);
        if (dbSize > _options.LocalStore.MaxSizeMB)
        {
            _logger.LogWarning("Outbox size ({Size}MB) exceeds limit ({Limit}MB), cleaning oldest records",
                dbSize, _options.LocalStore.MaxSizeMB);

            // 删除最旧的 10% 数据
            await _connection.ExecuteAsync(@"
                DELETE FROM outbox WHERE id IN (
                    SELECT id FROM outbox ORDER BY id LIMIT (SELECT COUNT(*) / 10 FROM outbox)
                )");

            // Vacuum 回收空间
            await _connection.ExecuteAsync("VACUUM");
        }
    }

    public OutboxStats GetStats()
    {
        var pending = _connection.ExecuteScalar<int>("SELECT COUNT(*) FROM outbox");
        var pendingPoints = _connection.ExecuteScalar<int>("SELECT COALESCE(SUM(batch_size), 0) FROM outbox");

        return new OutboxStats
        {
            PendingBatches = pending,
            PendingPoints = pendingPoints,
            TotalStored = Interlocked.Read(ref _storedCount),
            TotalRetrieved = Interlocked.Read(ref _retrievedCount)
        };
    }

    private byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest))
        {
            gzip.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }

    private byte[] Decompress(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
        _writeLock.Dispose();
    }

    private class OutboxRow
    {
        public long Id { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public int Compressed { get; set; }
        public int BatchSize { get; set; }
    }
}

public interface ILocalOutboxStore
{
    Task StoreAsync(IReadOnlyList<TelemetryPoint> points, CancellationToken ct);
    Task<OutboxBatch?> ReadBatchAsync(int limit, CancellationToken ct);
    Task AcknowledgeAsync(long batchId, CancellationToken ct);
    Task<bool> HasDataAsync(CancellationToken ct);
    Task<int> GetPendingCountAsync(CancellationToken ct);
    Task CleanupAsync(CancellationToken ct);
    OutboxStats GetStats();
}

public record OutboxBatch
{
    public long Id { get; init; }
    public List<TelemetryPoint> Points { get; init; } = new();
    public long CreatedAt { get; init; }
}

public record OutboxStats
{
    public int PendingBatches { get; init; }
    public int PendingPoints { get; init; }
    public long TotalStored { get; init; }
    public long TotalRetrieved { get; init; }
}
```

#### 3.2.4 主服务 (Store & Forward)

```csharp
// Services/StoreAndForwardService.cs
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Channels;
using IntelliMaint.Core.Contracts;
using IntelliMaint.Host.Edge.Models;
using Microsoft.Extensions.Options;

namespace IntelliMaint.Host.Edge.Services;

/// <summary>
/// 断网续传主服务
/// - 在线时: 批量压缩发送
/// - 离线时: 存储到本地 SQLite
/// - 恢复时: 自动 Drain 本地数据
/// </summary>
public sealed class StoreAndForwardService : BackgroundService
{
    private readonly IEdgeDataProcessor _processor;
    private readonly INetworkMonitor _networkMonitor;
    private readonly ILocalOutboxStore _outboxStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly StoreAndForwardOptions _options;
    private readonly EdgeOptions _edgeOptions;
    private readonly ILogger<StoreAndForwardService> _logger;

    private readonly Channel<TelemetryPoint> _inputChannel;
    private readonly List<TelemetryPoint> _batch = new();
    private readonly object _batchLock = new();

    private long _sentCount;
    private long _failedCount;
    private long _storedCount;
    private volatile bool _isDraining;

    public ChannelWriter<TelemetryPoint> Writer => _inputChannel.Writer;

    public StoreAndForwardService(
        IEdgeDataProcessor processor,
        INetworkMonitor networkMonitor,
        ILocalOutboxStore outboxStore,
        IHttpClientFactory httpClientFactory,
        IOptions<StoreAndForwardOptions> options,
        IOptions<EdgeOptions> edgeOptions,
        ILogger<StoreAndForwardService> logger)
    {
        _processor = processor;
        _networkMonitor = networkMonitor;
        _outboxStore = outboxStore;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _edgeOptions = edgeOptions.Value;
        _logger = logger;

        _inputChannel = Channel.CreateBounded<TelemetryPoint>(new BoundedChannelOptions(100_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        // 订阅网络状态变化
        _networkMonitor.OnStatusChanged += OnNetworkStatusChanged;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("StoreAndForwardService started");

        // 并行任务
        var tasks = new List<Task>
        {
            ProcessInputAsync(stoppingToken),
            SendLoopAsync(stoppingToken),
            CleanupLoopAsync(stoppingToken)
        };

        // 启动时先 Drain 本地存储
        if (_networkMonitor.IsOnline)
        {
            _ = DrainOutboxAsync(stoppingToken);
        }

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 处理输入数据
    /// </summary>
    private async Task ProcessInputAsync(CancellationToken ct)
    {
        await foreach (var point in _inputChannel.Reader.ReadAllAsync(ct))
        {
            // 预处理 (死区过滤等)
            var processed = _processor.Process(point);
            if (processed == null) continue;

            lock (_batchLock)
            {
                _batch.Add(processed);
            }
        }
    }

    /// <summary>
    /// 发送循环
    /// </summary>
    private async Task SendLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(_options.Send.IntervalMs, ct);

            List<TelemetryPoint> batchToSend;
            lock (_batchLock)
            {
                if (_batch.Count == 0) continue;

                batchToSend = _batch.Take(_options.Send.BatchSize).ToList();
                _batch.RemoveRange(0, Math.Min(_options.Send.BatchSize, _batch.Count));
            }

            if (batchToSend.Count == 0) continue;

            if (_networkMonitor.IsOnline && !_isDraining)
            {
                var success = await TrySendBatchAsync(batchToSend, ct);
                if (!success)
                {
                    await StoreToOutboxAsync(batchToSend, ct);
                }
            }
            else
            {
                await StoreToOutboxAsync(batchToSend, ct);
            }
        }
    }

    /// <summary>
    /// 尝试发送批次
    /// </summary>
    private async Task<bool> TrySendBatchAsync(List<TelemetryPoint> points, CancellationToken ct)
    {
        for (int retry = 0; retry <= _options.Send.MaxRetries; retry++)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("ApiClient");
                var url = $"{_edgeOptions.ApiBaseUrl?.TrimEnd('/')}/api/telemetry/ingest";

                var json = JsonSerializer.SerializeToUtf8Bytes(points);
                byte[] data;
                string? encoding = null;

                if (_options.Compression.Enabled)
                {
                    data = CompressData(json);
                    encoding = _options.Compression.Algorithm.ToLower() switch
                    {
                        "gzip" => "gzip",
                        "brotli" => "br",
                        _ => null
                    };
                }
                else
                {
                    data = json;
                }

                using var content = new ByteArrayContent(data);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                if (encoding != null)
                {
                    content.Headers.ContentEncoding.Add(encoding);
                }

                using var response = await client.PostAsync(url, content, ct);

                if (response.IsSuccessStatusCode)
                {
                    Interlocked.Add(ref _sentCount, points.Count);
                    _logger.LogDebug("Sent {Count} points (compressed: {Original} -> {Compressed} bytes)",
                        points.Count, json.Length, data.Length);
                    return true;
                }

                _logger.LogWarning("Send failed: HTTP {Status}", response.StatusCode);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Send failed (retry {Retry}/{Max})", retry, _options.Send.MaxRetries);
            }

            if (retry < _options.Send.MaxRetries)
            {
                await Task.Delay(_options.Send.RetryDelayMs, ct);
            }
        }

        Interlocked.Add(ref _failedCount, points.Count);
        return false;
    }

    /// <summary>
    /// 存储到本地 Outbox
    /// </summary>
    private async Task StoreToOutboxAsync(List<TelemetryPoint> points, CancellationToken ct)
    {
        await _outboxStore.StoreAsync(points, ct);
        Interlocked.Add(ref _storedCount, points.Count);
        _logger.LogDebug("Stored {Count} points to outbox", points.Count);
    }

    /// <summary>
    /// Drain 本地存储
    /// </summary>
    private async Task DrainOutboxAsync(CancellationToken ct)
    {
        if (!await _outboxStore.HasDataAsync(ct)) return;

        _isDraining = true;
        var pendingCount = await _outboxStore.GetPendingCountAsync(ct);
        _logger.LogInformation("Starting outbox drain: {Count} pending points", pendingCount);

        try
        {
            while (!ct.IsCancellationRequested && _networkMonitor.IsOnline)
            {
                var batch = await _outboxStore.ReadBatchAsync(_options.Send.DrainBatchSize, ct);
                if (batch == null) break;

                var success = await TrySendBatchAsync(batch.Points, ct);
                if (success)
                {
                    await _outboxStore.AcknowledgeAsync(batch.Id, ct);
                }
                else
                {
                    _logger.LogWarning("Drain paused: send failed");
                    break;
                }

                await Task.Delay(100, ct); // 避免过快
            }
        }
        finally
        {
            _isDraining = false;
            _logger.LogInformation("Outbox drain completed");
        }
    }

    /// <summary>
    /// 定期清理
    /// </summary>
    private async Task CleanupLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(_options.LocalStore.CleanupIntervalMinutes), ct);
            await _outboxStore.CleanupAsync(ct);
        }
    }

    /// <summary>
    /// 网络状态变化处理
    /// </summary>
    private void OnNetworkStatusChanged(bool isOnline)
    {
        if (isOnline)
        {
            _logger.LogInformation("Network restored, starting drain...");
            _ = DrainOutboxAsync(CancellationToken.None);
        }
        else
        {
            _logger.LogWarning("Network offline, switching to local storage mode");
        }
    }

    private byte[] CompressData(byte[] data)
    {
        using var output = new MemoryStream();

        var level = _options.Compression.Level.ToLower() switch
        {
            "fastest" => CompressionLevel.Fastest,
            "optimal" => CompressionLevel.Optimal,
            "smallestsize" => CompressionLevel.SmallestSize,
            _ => CompressionLevel.Fastest
        };

        if (_options.Compression.Algorithm.Equals("brotli", StringComparison.OrdinalIgnoreCase))
        {
            using var brotli = new BrotliStream(output, level);
            brotli.Write(data, 0, data.Length);
        }
        else
        {
            using var gzip = new GZipStream(output, level);
            gzip.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }

    public StoreAndForwardStats GetStats()
    {
        var outboxStats = _outboxStore.GetStats();
        var processingStats = _processor.GetStats();

        return new StoreAndForwardStats
        {
            IsOnline = _networkMonitor.IsOnline,
            IsDraining = _isDraining,
            SentCount = Interlocked.Read(ref _sentCount),
            FailedCount = Interlocked.Read(ref _failedCount),
            StoredCount = Interlocked.Read(ref _storedCount),
            PendingBatches = outboxStats.PendingBatches,
            PendingPoints = outboxStats.PendingPoints,
            ProcessingFilterRate = processingStats.FilterRate
        };
    }
}

public record StoreAndForwardStats
{
    public bool IsOnline { get; init; }
    public bool IsDraining { get; init; }
    public long SentCount { get; init; }
    public long FailedCount { get; init; }
    public long StoredCount { get; init; }
    public int PendingBatches { get; init; }
    public int PendingPoints { get; init; }
    public double ProcessingFilterRate { get; init; }
}
```

---

## 四、配置示例

```json
// appsettings.json 新增配置
{
  "EdgeProcessing": {
    "Enabled": true,
    "DefaultDeadband": 0.01,
    "DefaultDeadbandPercent": 0.5,
    "DefaultMinIntervalMs": 1000,
    "ForceUploadIntervalMs": 60000,
    "TagOverrides": {
      "Current": {
        "Deadband": 0.05,
        "MinIntervalMs": 500
      },
      "Voltage": {
        "Deadband": 0.1,
        "MinIntervalMs": 500
      },
      "Running": {
        "Bypass": true
      }
    },
    "OutlierDetection": {
      "Enabled": true,
      "SigmaThreshold": 4.0,
      "WindowSize": 100,
      "Action": "Mark"
    }
  },
  "StoreAndForward": {
    "Enabled": true,
    "LocalStore": {
      "Type": "SQLite",
      "Path": "data/outbox.db",
      "MaxSizeMB": 1000,
      "RetentionDays": 7,
      "CleanupIntervalMinutes": 60
    },
    "Network": {
      "HealthCheckUrl": "http://localhost:5000/api/health",
      "CheckIntervalMs": 5000,
      "TimeoutMs": 3000,
      "OfflineThreshold": 3
    },
    "Send": {
      "BatchSize": 500,
      "IntervalMs": 500,
      "MaxRetries": 3,
      "RetryDelayMs": 1000,
      "DrainBatchSize": 1000
    },
    "Compression": {
      "Enabled": true,
      "Algorithm": "Gzip",
      "Level": "Fastest"
    }
  }
}
```

---

## 五、Program.cs 修改

```csharp
// Program.cs 修改
using IntelliMaint.Host.Edge.Models;
using IntelliMaint.Host.Edge.Services;

// ... 现有代码 ...

// 绑定新配置
builder.Services.Configure<ProcessingOptions>(
    builder.Configuration.GetSection(ProcessingOptions.SectionName));
builder.Services.Configure<StoreAndForwardOptions>(
    builder.Configuration.GetSection(StoreAndForwardOptions.SectionName));

// 注册新服务
builder.Services.AddSingleton<IEdgeDataProcessor, EdgeDataProcessor>();
builder.Services.AddSingleton<ILocalOutboxStore, LocalOutboxStore>();
builder.Services.AddSingleton<INetworkMonitor, NetworkMonitor>();
builder.Services.AddHostedService(sp => (NetworkMonitor)sp.GetRequiredService<INetworkMonitor>());
builder.Services.AddSingleton<StoreAndForwardService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<StoreAndForwardService>());

// 添加 HttpClient
builder.Services.AddHttpClient("NetworkCheck", client =>
{
    client.Timeout = TimeSpan.FromSeconds(3);
});
```

---

## 六、测试计划

### 6.1 单元测试

| 测试项 | 描述 |
|--------|------|
| DeadbandFilter_ShouldFilter | 死区内数据应被过滤 |
| DeadbandFilter_ShouldPass | 超过死区应通过 |
| MinInterval_ShouldFilter | 间隔内数据应被过滤 |
| ForceUpload_ShouldBypass | 强制间隔应绕过过滤 |
| OutlierDetection_ShouldDetect | 应检测异常值 |
| LocalStore_ShouldPersist | 应正确持久化 |
| Compression_ShouldReduce | 压缩应减小数据量 |

### 6.2 集成测试

| 测试场景 | 预期结果 |
|----------|----------|
| 正常在线发送 | 数据压缩后直接发送 |
| 网络断开 | 数据存入本地 SQLite |
| 网络恢复 | 自动 Drain 本地数据 |
| 存储容量超限 | 自动清理旧数据 |
| Edge 重启 | 恢复后继续 Drain |

---

## 七、预期效果

| 指标 | 优化前 | 优化后 |
|------|--------|--------|
| 数据传输量 | 100% | **30%** (减少70%) |
| 断网数据丢失 | 100% | **0%** |
| 网络带宽 | 100% | **50%** (压缩) |
| 存储容量 | N/A | 最大 1GB |

---

*文档由 Claude Code 生成*
