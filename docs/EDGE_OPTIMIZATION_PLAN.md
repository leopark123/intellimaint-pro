# Edge 服务优化方案

> 版本: v1.0
> 日期: 2026-01-11

---

## 一、当前架构分析

### 1.1 现有架构

```
┌─────────────────────────────────────────────────────────────┐
│                      Host.Edge                               │
├─────────────────────────────────────────────────────────────┤
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐ │
│  │  EdgeWorker │  │HealthReport│  │ TelemetryPipeline   │ │
│  │  (协调器)   │  │  (健康上报) │  │ (数据管道)          │ │
│  └──────┬──────┘  └─────────────┘  └──────────┬──────────┘ │
│         │                                      │            │
│  ┌──────▼──────────────────────────────────────▼──────────┐ │
│  │                   Protocol Collectors                   │ │
│  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  │ │
│  │  │  LibPlcTag   │  │    OpcUa     │  │   (Modbus)   │  │ │
│  │  │  Collector   │  │   Collector  │  │   未实现     │  │ │
│  │  └──────────────┘  └──────────────┘  └──────────────┘  │ │
│  └─────────────────────────────────────────────────────────┘ │
└──────────────────────────────┬──────────────────────────────┘
                               │ HTTP POST (批量)
                               ▼
                      ┌─────────────────┐
                      │    Host.Api     │
                      └─────────────────┘
```

### 1.2 现有优点
- Channel-based 高性能管道
- 多协议支持 (LibPlcTag, OpcUa)
- 模拟模式便于开发测试
- 配置热重载支持
- 连接池管理
- 指数退避重试

### 1.3 待优化点
1. **无边缘计算** - 所有数据原样上传
2. **无断网续传** - 网络断开数据丢失
3. **无数据压缩** - 带宽消耗大
4. **告警在云端** - 延迟较高
5. **协议支持有限** - 缺少 Modbus/MQTT

---

## 二、优化方案总览

| 优化项 | 优先级 | 难度 | 收益 |
|--------|--------|------|------|
| 边缘数据预处理 | P0 | 中 | 减少 70% 数据传输 |
| 断网续传 (Store & Forward) | P0 | 高 | 保证数据不丢失 |
| 边缘告警引擎 | P1 | 中 | 降低告警延迟到 <100ms |
| 数据压缩传输 | P1 | 低 | 减少 50% 带宽 |
| Modbus TCP 支持 | P1 | 中 | 扩大设备覆盖 |
| MQTT 发布支持 | P2 | 中 | 支持云边协同 |
| 边缘 AI 推理 | P2 | 高 | 实时故障检测 |

---

## 三、详细优化方案

### 3.1 边缘数据预处理 (P0)

#### 目标
- 减少 70% 以上的数据传输量
- 降低云端存储成本
- 提高实时性

#### 实现方案

```csharp
// 新增: EdgeDataProcessor.cs
public class EdgeDataProcessor : IEdgeDataProcessor
{
    private readonly ConcurrentDictionary<string, TagProcessingState> _tagStates = new();
    private readonly EdgeProcessingOptions _options;

    /// <summary>
    /// 处理策略
    /// </summary>
    public TelemetryPoint? Process(TelemetryPoint point)
    {
        var state = _tagStates.GetOrAdd(point.TagId, _ => new TagProcessingState());

        // 1. 死区过滤 (Deadband)
        if (IsWithinDeadband(point, state))
            return null;

        // 2. 变化检测 (只传输变化的值)
        if (!HasChanged(point, state))
            return null;

        // 3. 采样降频 (高频数据降采样)
        if (ShouldDownsample(point, state))
            return AggregatePoint(point, state);

        // 4. 异常值过滤 (可选)
        if (IsOutlier(point, state))
        {
            LogOutlier(point);
            return null; // 或标记后仍然传输
        }

        state.UpdateLastValue(point);
        return point;
    }

    /// <summary>
    /// 死区过滤: 值变化小于阈值则不传输
    /// </summary>
    private bool IsWithinDeadband(TelemetryPoint point, TagProcessingState state)
    {
        if (state.LastValue == null) return false;

        var deadband = _options.GetDeadband(point.TagId);
        var diff = Math.Abs(point.Float64Value - state.LastValue.Float64Value);

        return diff < deadband;
    }
}
```

#### 配置示例

```json
{
  "EdgeProcessing": {
    "Enabled": true,
    "DefaultDeadband": 0.01,
    "DefaultMinInterval": 1000,
    "TagOverrides": {
      "Current": { "Deadband": 0.05, "MinInterval": 500 },
      "Temperature": { "Deadband": 0.1, "MinInterval": 5000 },
      "Running": { "Deadband": 0, "MinInterval": 0 }
    },
    "Downsampling": {
      "Enabled": true,
      "WindowMs": 1000,
      "AggregationType": "Average"
    },
    "OutlierDetection": {
      "Enabled": true,
      "SigmaThreshold": 3.0
    }
  }
}
```

---

### 3.2 断网续传 (Store & Forward) (P0)

#### 目标
- 网络断开时数据 0 丢失
- 自动恢复上传
- 支持容量限制和过期清理

#### 架构设计

```
┌─────────────────────────────────────────────────────────────┐
│                     Store & Forward                          │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  ┌──────────┐    ┌──────────────┐    ┌──────────────────┐  │
│  │ Collector │───▶│ DataProcessor│───▶│ OutboundQueue    │  │
│  └──────────┘    └──────────────┘    └────────┬─────────┘  │
│                                               │             │
│                       ┌───────────────────────┴──────┐      │
│                       │     NetworkMonitor           │      │
│                       │  (检测网络状态)               │      │
│                       └───────────────────────────────┘      │
│                                  │                           │
│                    ┌─────────────┼─────────────┐            │
│                    │             │             │            │
│                    ▼             ▼             ▼            │
│            ┌────────────┐ ┌────────────┐ ┌────────────┐    │
│            │ Direct Send│ │ LocalStore │ │ Drain Loop │    │
│            │ (在线)     │ │ (离线存储) │ │ (恢复上传) │    │
│            └────────────┘ └────────────┘ └────────────┘    │
│                                  │             │            │
│                                  ▼             ▼            │
│                          ┌─────────────────────────┐        │
│                          │  SQLite / RocksDB       │        │
│                          │  (本地持久化存储)        │        │
│                          └─────────────────────────┘        │
└─────────────────────────────────────────────────────────────┘
```

#### 核心实现

```csharp
// StoreAndForwardService.cs
public class StoreAndForwardService : BackgroundService
{
    private readonly ILocalStore _store;
    private readonly IApiClient _apiClient;
    private readonly NetworkMonitor _networkMonitor;
    private readonly Channel<TelemetryBatch> _outboundQueue;

    private const int BatchSize = 1000;
    private const int DrainIntervalMs = 100;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // 启动时先 Drain 本地存储
        await DrainLocalStoreAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            var batch = await CollectBatchAsync(ct);

            if (_networkMonitor.IsOnline)
            {
                var success = await TrySendAsync(batch, ct);
                if (!success)
                {
                    await _store.StoreAsync(batch, ct);
                }
            }
            else
            {
                await _store.StoreAsync(batch, ct);
            }
        }
    }

    private async Task DrainLocalStoreAsync(CancellationToken ct)
    {
        _logger.LogInformation("Draining local store...");

        while (await _store.HasDataAsync(ct))
        {
            if (!_networkMonitor.IsOnline)
            {
                await Task.Delay(1000, ct);
                continue;
            }

            var batch = await _store.ReadBatchAsync(BatchSize, ct);
            var success = await TrySendAsync(batch, ct);

            if (success)
            {
                await _store.AcknowledgeAsync(batch.Id, ct);
            }
            else
            {
                break; // 网络异常，停止 drain
            }
        }

        _logger.LogInformation("Local store drained");
    }
}

// LocalStore.cs - 基于 SQLite
public class SqliteLocalStore : ILocalStore
{
    private const string Schema = @"
        CREATE TABLE IF NOT EXISTS outbox (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            created_at INTEGER NOT NULL,
            data BLOB NOT NULL,
            compressed INTEGER DEFAULT 0
        );
        CREATE INDEX IF NOT EXISTS idx_outbox_created ON outbox(created_at);
    ";

    public async Task StoreAsync(TelemetryBatch batch, CancellationToken ct)
    {
        var compressed = Compress(batch);
        await _db.ExecuteAsync(
            "INSERT INTO outbox (created_at, data, compressed) VALUES (@ts, @data, 1)",
            new { ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), data = compressed });
    }

    public async Task<TelemetryBatch> ReadBatchAsync(int limit, CancellationToken ct)
    {
        var rows = await _db.QueryAsync<OutboxRow>(
            "SELECT id, data, compressed FROM outbox ORDER BY id LIMIT @limit",
            new { limit });
        // ...
    }

    // 定期清理过期数据
    public async Task CleanupAsync(TimeSpan retention, CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.Add(-retention).ToUnixTimeMilliseconds();
        await _db.ExecuteAsync("DELETE FROM outbox WHERE created_at < @cutoff", new { cutoff });
    }
}
```

#### 配置示例

```json
{
  "StoreAndForward": {
    "Enabled": true,
    "LocalStore": {
      "Type": "SQLite",
      "Path": "data/outbox.db",
      "MaxSizeMB": 1000,
      "RetentionDays": 7,
      "Compression": true
    },
    "Network": {
      "HealthCheckUrl": "http://api:5000/api/health",
      "CheckIntervalMs": 5000,
      "TimeoutMs": 3000
    },
    "Drain": {
      "BatchSize": 1000,
      "IntervalMs": 100,
      "MaxRetries": 3
    }
  }
}
```

---

### 3.3 边缘告警引擎 (P1)

#### 目标
- 告警延迟从 ~500ms 降低到 <100ms
- 支持离线告警
- 减少云端告警评估负载

#### 实现方案

```csharp
// EdgeAlarmEngine.cs
public class EdgeAlarmEngine : IEdgeAlarmEngine
{
    private readonly ConcurrentDictionary<string, AlarmRuleState> _ruleStates = new();
    private List<AlarmRule> _rules = new();

    public async Task InitializeAsync(CancellationToken ct)
    {
        // 从 API 加载告警规则
        _rules = await _apiClient.GetAlarmRulesAsync(ct);
        _logger.LogInformation("Loaded {Count} alarm rules", _rules.Count);
    }

    public AlarmEvent? Evaluate(TelemetryPoint point)
    {
        // 找到匹配的规则
        var rules = _rules.Where(r => r.TagId == point.TagId && r.Enabled);

        foreach (var rule in rules)
        {
            var state = _ruleStates.GetOrAdd(rule.RuleId, _ => new AlarmRuleState());
            var alarm = EvaluateRule(point, rule, state);

            if (alarm != null)
            {
                return alarm;
            }
        }

        return null;
    }

    private AlarmEvent? EvaluateRule(TelemetryPoint point, AlarmRule rule, AlarmRuleState state)
    {
        var value = point.Float64Value;
        var triggered = rule.Type switch
        {
            AlarmRuleType.AboveThreshold => value > rule.Threshold,
            AlarmRuleType.BelowThreshold => value < rule.Threshold,
            AlarmRuleType.OutOfRange => value < rule.LowLimit || value > rule.HighLimit,
            AlarmRuleType.RateOfChange => CalculateRoc(state, value) > rule.RocThreshold,
            _ => false
        };

        if (triggered && !state.IsActive)
        {
            // 防抖动: 连续 N 次触发才生成告警
            state.ConsecutiveTriggers++;
            if (state.ConsecutiveTriggers >= rule.DebounceCount)
            {
                state.IsActive = true;
                state.ActivatedAt = DateTimeOffset.UtcNow;

                return new AlarmEvent
                {
                    AlarmId = Guid.NewGuid().ToString(),
                    RuleId = rule.RuleId,
                    TagId = point.TagId,
                    DeviceId = point.DeviceId,
                    Severity = rule.Severity,
                    Value = value,
                    Threshold = rule.Threshold,
                    Message = rule.Message,
                    Timestamp = point.Timestamp,
                    Source = "Edge"
                };
            }
        }
        else if (!triggered)
        {
            state.ConsecutiveTriggers = 0;
            if (state.IsActive)
            {
                state.IsActive = false;
                // 可选: 生成告警恢复事件
            }
        }

        return null;
    }
}
```

---

### 3.4 数据压缩传输 (P1)

#### 目标
- 减少 50% 以上的网络带宽
- 支持多种压缩算法

#### 实现方案

```csharp
// CompressedTelemetryClient.cs
public class CompressedTelemetryClient : ITelemetryClient
{
    private readonly HttpClient _httpClient;
    private readonly CompressionOptions _options;

    public async Task SendBatchAsync(List<TelemetryPoint> points, CancellationToken ct)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(points);

        byte[] data;
        string contentEncoding;

        switch (_options.Algorithm)
        {
            case CompressionAlgorithm.Gzip:
                data = await CompressGzipAsync(json);
                contentEncoding = "gzip";
                break;
            case CompressionAlgorithm.Brotli:
                data = await CompressBrotliAsync(json);
                contentEncoding = "br";
                break;
            case CompressionAlgorithm.Lz4:
                data = CompressLz4(json);
                contentEncoding = "lz4";
                break;
            default:
                data = json;
                contentEncoding = null;
                break;
        }

        var content = new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        if (contentEncoding != null)
            content.Headers.ContentEncoding.Add(contentEncoding);

        await _httpClient.PostAsync("/api/telemetry/ingest", content, ct);
    }

    private static async Task<byte[]> CompressBrotliAsync(byte[] data)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Fastest))
        {
            await brotli.WriteAsync(data);
        }
        return output.ToArray();
    }
}
```

#### 压缩效果对比

| 算法 | 压缩率 | 压缩速度 | 解压速度 | 推荐场景 |
|------|--------|----------|----------|----------|
| Gzip | 70% | 中 | 快 | 通用 |
| Brotli | 80% | 慢 | 快 | 带宽受限 |
| LZ4 | 50% | 极快 | 极快 | CPU受限 |

---

### 3.5 Modbus TCP 支持 (P1)

#### 架构设计

```csharp
// ModbusCollector.cs
public class ModbusCollector : ICollector, ITelemetrySource
{
    private readonly ModbusOptions _options;
    private readonly Channel<TelemetryPoint> _output;
    private readonly Dictionary<string, ModbusMaster> _masters = new();

    public async Task StartAsync(CancellationToken ct)
    {
        foreach (var slave in _options.Slaves)
        {
            var master = new ModbusMaster(
                slave.IpAddress,
                slave.Port,
                slave.SlaveId);

            await master.ConnectAsync(ct);
            _masters[slave.SlaveId] = master;

            // 启动采集循环
            _ = RunSlaveLoopAsync(slave, master, ct);
        }
    }

    private async Task RunSlaveLoopAsync(ModbusSlaveConfig slave, ModbusMaster master, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            foreach (var register in slave.Registers)
            {
                var values = register.Type switch
                {
                    ModbusRegisterType.HoldingRegister =>
                        await master.ReadHoldingRegistersAsync(register.Address, register.Count, ct),
                    ModbusRegisterType.InputRegister =>
                        await master.ReadInputRegistersAsync(register.Address, register.Count, ct),
                    ModbusRegisterType.Coil =>
                        await master.ReadCoilsAsync(register.Address, register.Count, ct),
                    _ => throw new NotSupportedException()
                };

                var point = ConvertToTelemetryPoint(slave, register, values);
                _output.Writer.TryWrite(point);
            }

            await Task.Delay(slave.ScanIntervalMs, ct);
        }
    }
}
```

#### 配置示例

```json
{
  "Protocols": {
    "Modbus": {
      "Enabled": true,
      "Slaves": [
        {
          "SlaveId": "PLC-001",
          "IpAddress": "192.168.1.100",
          "Port": 502,
          "UnitId": 1,
          "ScanIntervalMs": 500,
          "TimeoutMs": 3000,
          "Registers": [
            {
              "TagId": "Temperature",
              "Type": "HoldingRegister",
              "Address": 100,
              "Count": 2,
              "DataType": "Float32",
              "ByteOrder": "BigEndian",
              "Unit": "°C"
            },
            {
              "TagId": "Pressure",
              "Type": "InputRegister",
              "Address": 200,
              "Count": 2,
              "DataType": "Float32",
              "Unit": "MPa"
            }
          ]
        }
      ]
    }
  }
}
```

---

### 3.6 边缘 AI 推理 (P2)

#### 目标
- 在边缘侧进行实时故障检测
- 降低云端 AI 计算负载
- 支持离线 AI 推理

#### 架构设计

```
┌─────────────────────────────────────────────────────────────┐
│                     Edge AI Engine                           │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐  │
│  │ Feature      │───▶│ ONNX Runtime │───▶│ Inference    │  │
│  │ Extraction   │    │ (模型推理)    │    │ Results      │  │
│  └──────────────┘    └──────────────┘    └──────────────┘  │
│         ▲                   │                    │          │
│         │                   ▼                    ▼          │
│  ┌──────┴──────┐    ┌──────────────┐    ┌──────────────┐  │
│  │ Sliding     │    │ Model Store  │    │ Anomaly      │  │
│  │ Window      │    │ (模型版本)   │    │ Detector     │  │
│  └─────────────┘    └──────────────┘    └──────────────┘  │
│                                                              │
└─────────────────────────────────────────────────────────────┘
```

#### 实现方案

```csharp
// EdgeAiEngine.cs
public class EdgeAiEngine : IEdgeAiEngine
{
    private readonly OnnxModelRunner _modelRunner;
    private readonly FeatureExtractor _featureExtractor;
    private readonly SlidingWindowBuffer _windowBuffer;

    public async Task<AnomalyResult?> DetectAsync(TelemetryPoint point, CancellationToken ct)
    {
        // 1. 添加到滑动窗口
        _windowBuffer.Add(point);

        if (!_windowBuffer.IsFull)
            return null;

        // 2. 提取特征
        var features = _featureExtractor.Extract(_windowBuffer.GetWindow());

        // 3. 运行 ONNX 模型
        var tensor = new DenseTensor<float>(features);
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input", tensor)
        };

        using var results = _modelRunner.Run(inputs);
        var output = results.First().AsTensor<float>();

        // 4. 解析结果
        var anomalyScore = output[0];
        if (anomalyScore > _options.AnomalyThreshold)
        {
            return new AnomalyResult
            {
                DeviceId = point.DeviceId,
                TagId = point.TagId,
                Score = anomalyScore,
                DetectedAt = DateTimeOffset.UtcNow,
                Source = "EdgeAI"
            };
        }

        return null;
    }
}
```

---

## 四、实施路线图

### Phase 1 (1-2周) - 基础优化

```
Week 1:
├── [ ] 边缘数据预处理 (死区过滤)
├── [ ] 数据压缩传输 (Gzip)
└── [ ] 单元测试

Week 2:
├── [ ] 断网续传 (SQLite 存储)
├── [ ] 网络状态监控
└── [ ] 集成测试
```

### Phase 2 (2-3周) - 功能增强

```
Week 3:
├── [ ] 边缘告警引擎
├── [ ] 规则同步机制
└── [ ] 告警上报

Week 4-5:
├── [ ] Modbus TCP 协议支持
├── [ ] 配置管理
└── [ ] 测试验证
```

### Phase 3 (3-4周) - 高级功能

```
Week 6-7:
├── [ ] MQTT 发布支持
├── [ ] 边缘 AI 推理 (ONNX)
└── [ ] 性能优化

Week 8:
├── [ ] 文档完善
├── [ ] 部署指南
└── [ ] 发布 v2.0
```

---

## 五、预期收益

| 指标 | 优化前 | 优化后 | 提升 |
|------|--------|--------|------|
| 数据传输量 | 100% | 30% | -70% |
| 告警延迟 | ~500ms | <100ms | -80% |
| 断网数据丢失 | 100% | 0% | -100% |
| 协议覆盖 | 2种 | 4种 | +100% |
| 边缘 AI | 无 | 支持 | 新增 |

---

## 六、资源需求

### 开发资源
- 后端开发: 1人 × 8周
- 测试: 0.5人 × 4周

### 硬件需求 (边缘设备)
- CPU: 2核+ (AI 推理需要 4核)
- 内存: 2GB+ (AI 推理需要 4GB)
- 存储: 10GB+ (断网续传)

---

*文档由 Claude Code 生成*
