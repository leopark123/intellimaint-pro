# Batch 22: WebSocket 实时推送 - ChatGPT 开发指令

## 项目背景

你正在为 **IntelliMaint Pro** 工业数据采集平台开发 WebSocket 实时推送功能。

### 当前架构
```
KEPServerEX (OPC UA) → Host.Edge (采集) → SQLite → Host.Api (REST) → React UI (轮询)
```

### 目标架构
```
KEPServerEX (OPC UA) → Host.Edge (采集) → SQLite → Host.Api (REST + SignalR) → React UI (实时推送)
```

### 技术栈
- 后端：ASP.NET Core 8.0 + SignalR
- 前端：React 18 + TypeScript + @microsoft/signalr

---

## 本批次目标

实现 SignalR 实时推送，让 UI 无需轮询即可接收最新数据。

---

## 后端实现

### 1. 添加 NuGet 包

在 `src/Host.Api/IntelliMaint.Host.Api.csproj` 中添加：

```xml
<PackageReference Include="Microsoft.AspNetCore.SignalR" Version="1.1.0" />
```

注意：ASP.NET Core 8.0 已内置 SignalR，可能不需要额外包。

---

### 2. 创建 `src/Host.Api/Hubs/TelemetryHub.cs`

```csharp
using Microsoft.AspNetCore.SignalR;

namespace IntelliMaint.Host.Api.Hubs;

/// <summary>
/// 遥测数据 SignalR Hub
/// </summary>
public class TelemetryHub : Hub
{
    private readonly ILogger<TelemetryHub> _logger;

    public TelemetryHub(ILogger<TelemetryHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}, Error: {Error}", 
            Context.ConnectionId, exception?.Message);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// 客户端订阅特定设备的数据
    /// </summary>
    public async Task SubscribeDevice(string deviceId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"device:{deviceId}");
        _logger.LogInformation("Client {ConnectionId} subscribed to device {DeviceId}", 
            Context.ConnectionId, deviceId);
    }

    /// <summary>
    /// 客户端取消订阅
    /// </summary>
    public async Task UnsubscribeDevice(string deviceId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"device:{deviceId}");
        _logger.LogInformation("Client {ConnectionId} unsubscribed from device {DeviceId}", 
            Context.ConnectionId, deviceId);
    }

    /// <summary>
    /// 订阅所有数据
    /// </summary>
    public async Task SubscribeAll()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "all");
        _logger.LogInformation("Client {ConnectionId} subscribed to all data", Context.ConnectionId);
    }
}
```

---

### 3. 创建 `src/Host.Api/Services/TelemetryBroadcastService.cs`

这是一个后台服务，定期从数据库读取最新数据并广播给客户端。

```csharp
using Microsoft.AspNetCore.SignalR;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Host.Api.Hubs;

namespace IntelliMaint.Host.Api.Services;

/// <summary>
/// 遥测数据广播服务
/// 定期读取最新数据并通过 SignalR 推送给客户端
/// </summary>
public class TelemetryBroadcastService : BackgroundService
{
    private readonly IHubContext<TelemetryHub> _hubContext;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TelemetryBroadcastService> _logger;
    
    // 广播间隔（毫秒）
    private const int BroadcastIntervalMs = 1000;
    
    // 记录上次广播的时间戳，避免重复推送
    private readonly Dictionary<string, long> _lastBroadcastTs = new();

    public TelemetryBroadcastService(
        IHubContext<TelemetryHub> hubContext,
        IServiceScopeFactory scopeFactory,
        ILogger<TelemetryBroadcastService> logger)
    {
        _hubContext = hubContext;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TelemetryBroadcastService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await BroadcastLatestDataAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting telemetry data");
            }

            await Task.Delay(BroadcastIntervalMs, stoppingToken);
        }

        _logger.LogInformation("TelemetryBroadcastService stopped");
    }

    private async Task BroadcastLatestDataAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();

        // 获取所有标签的最新值
        var latestData = await repository.GetLatestAsync(null, null, ct);

        if (latestData.Count == 0)
            return;

        // 过滤出有更新的数据点
        var updatedPoints = new List<object>();
        
        foreach (var point in latestData)
        {
            var key = $"{point.DeviceId}:{point.TagId}";
            
            if (!_lastBroadcastTs.TryGetValue(key, out var lastTs) || point.Ts > lastTs)
            {
                _lastBroadcastTs[key] = point.Ts;
                updatedPoints.Add(new
                {
                    deviceId = point.DeviceId,
                    tagId = point.TagId,
                    ts = point.Ts,
                    value = GetValue(point),
                    valueType = point.ValueType.ToString(),
                    quality = point.Quality,
                    unit = point.Unit
                });
            }
        }

        if (updatedPoints.Count > 0)
        {
            // 广播给所有订阅者
            await _hubContext.Clients.Group("all").SendAsync("ReceiveData", updatedPoints, ct);

            // 按设备分组广播
            var byDevice = updatedPoints.GroupBy(p => ((dynamic)p).deviceId);
            foreach (var group in byDevice)
            {
                var deviceId = group.Key;
                await _hubContext.Clients.Group($"device:{deviceId}")
                    .SendAsync("ReceiveData", group.ToList(), ct);
            }

            _logger.LogDebug("Broadcast {Count} updated points", updatedPoints.Count);
        }
    }

    private static object? GetValue(IntelliMaint.Core.Contracts.TelemetryPoint p)
    {
        return p.ValueType switch
        {
            IntelliMaint.Core.Contracts.TagValueType.Bool => p.BoolValue,
            IntelliMaint.Core.Contracts.TagValueType.Int8 => p.Int8Value,
            IntelliMaint.Core.Contracts.TagValueType.UInt8 => p.UInt8Value,
            IntelliMaint.Core.Contracts.TagValueType.Int16 => p.Int16Value,
            IntelliMaint.Core.Contracts.TagValueType.UInt16 => p.UInt16Value,
            IntelliMaint.Core.Contracts.TagValueType.Int32 => p.Int32Value,
            IntelliMaint.Core.Contracts.TagValueType.UInt32 => p.UInt32Value,
            IntelliMaint.Core.Contracts.TagValueType.Int64 => p.Int64Value,
            IntelliMaint.Core.Contracts.TagValueType.UInt64 => p.UInt64Value,
            IntelliMaint.Core.Contracts.TagValueType.Float32 => p.Float32Value,
            IntelliMaint.Core.Contracts.TagValueType.Float64 => p.Float64Value,
            IntelliMaint.Core.Contracts.TagValueType.String => p.StringValue,
            _ => null
        };
    }
}
```

---

### 4. 修改 `src/Host.Api/Program.cs`

添加 SignalR 服务注册和端点映射：

```csharp
// 在 builder.Services 部分添加：
builder.Services.AddSignalR();
builder.Services.AddHostedService<TelemetryBroadcastService>();

// 在 app.MapControllers() 之后添加：
app.MapHub<TelemetryHub>("/hubs/telemetry");
```

完整的 Program.cs 应该类似：

```csharp
using IntelliMaint.Core.Contracts;
using IntelliMaint.Host.Api.Endpoints;
using IntelliMaint.Host.Api.Hubs;
using IntelliMaint.Host.Api.Services;
using IntelliMaint.Infrastructure.Sqlite;
using Serilog;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .WriteTo.File("logs/api-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    Log.Information("Starting IntelliMaint API...");

    var builder = WebApplication.CreateBuilder(args);

    // Configure Serilog
    builder.Host.UseSerilog();

    // Bind configuration options
    builder.Services.Configure<EdgeOptions>(
        builder.Configuration.GetSection(EdgeOptions.SectionName));

    // Add infrastructure services
    builder.Services.AddSqliteInfrastructure();

    // Add SignalR
    builder.Services.AddSignalR();
    
    // Add broadcast service
    builder.Services.AddHostedService<TelemetryBroadcastService>();

    // Add controllers
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    // Add health checks
    builder.Services.AddHealthChecks();
    
    // Add CORS for SignalR
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.WithOrigins("http://localhost:3000")
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
    });

    var app = builder.Build();

    // Initialize database
    await app.Services.InitializeDatabaseAsync();

    // Configure pipeline
    app.UseSwagger();
    app.UseSwaggerUI();

    app.UseSerilogRequestLogging();
    
    // Enable CORS
    app.UseCors();

    // Health check endpoints
    app.MapHealthChecks("/health/live");
    app.MapHealthChecks("/health/ready");

    app.MapControllers();

    // Minimal API endpoints
    app.MapGet("/", () => "IntelliMaint API is running");
    
    // Telemetry API endpoints
    app.MapTelemetryEndpoints();
    
    // SignalR Hub
    app.MapHub<TelemetryHub>("/hubs/telemetry");

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
```

---

## 前端实现

### 1. 安装 SignalR 客户端

在 `intellimaint-ui` 目录执行：

```bash
npm install @microsoft/signalr
```

更新 `package.json`：

```json
{
  "dependencies": {
    "@microsoft/signalr": "^8.0.0",
    // ... 其他依赖
  }
}
```

---

### 2. 创建 `src/api/signalr.ts`

SignalR 连接管理：

```typescript
import * as signalR from '@microsoft/signalr'

export interface TelemetryDataPoint {
  deviceId: string
  tagId: string
  ts: number
  value: number | string | boolean | null
  valueType: string
  quality: number
  unit: string | null
}

type DataCallback = (data: TelemetryDataPoint[]) => void

class TelemetrySignalR {
  private connection: signalR.HubConnection | null = null
  private dataCallbacks: Set<DataCallback> = new Set()
  private reconnectAttempts = 0
  private maxReconnectAttempts = 10

  async connect(): Promise<void> {
    if (this.connection?.state === signalR.HubConnectionState.Connected) {
      return
    }

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/telemetry')
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (retryContext) => {
          // 重连延迟：1s, 2s, 4s, 8s, 最大30s
          return Math.min(1000 * Math.pow(2, retryContext.previousRetryCount), 30000)
        }
      })
      .configureLogging(signalR.LogLevel.Information)
      .build()

    // 接收数据事件
    this.connection.on('ReceiveData', (data: TelemetryDataPoint[]) => {
      this.dataCallbacks.forEach(callback => callback(data))
    })

    // 连接状态事件
    this.connection.onclose((error) => {
      console.log('SignalR connection closed', error)
    })

    this.connection.onreconnecting((error) => {
      console.log('SignalR reconnecting...', error)
    })

    this.connection.onreconnected((connectionId) => {
      console.log('SignalR reconnected:', connectionId)
      // 重新订阅
      this.subscribeAll()
    })

    try {
      await this.connection.start()
      console.log('SignalR connected')
      this.reconnectAttempts = 0
    } catch (error) {
      console.error('SignalR connection failed:', error)
      throw error
    }
  }

  async disconnect(): Promise<void> {
    if (this.connection) {
      await this.connection.stop()
      this.connection = null
    }
  }

  async subscribeAll(): Promise<void> {
    if (this.connection?.state === signalR.HubConnectionState.Connected) {
      await this.connection.invoke('SubscribeAll')
    }
  }

  async subscribeDevice(deviceId: string): Promise<void> {
    if (this.connection?.state === signalR.HubConnectionState.Connected) {
      await this.connection.invoke('SubscribeDevice', deviceId)
    }
  }

  async unsubscribeDevice(deviceId: string): Promise<void> {
    if (this.connection?.state === signalR.HubConnectionState.Connected) {
      await this.connection.invoke('UnsubscribeDevice', deviceId)
    }
  }

  onData(callback: DataCallback): () => void {
    this.dataCallbacks.add(callback)
    return () => {
      this.dataCallbacks.delete(callback)
    }
  }

  get isConnected(): boolean {
    return this.connection?.state === signalR.HubConnectionState.Connected
  }
}

// 单例导出
export const telemetrySignalR = new TelemetrySignalR()
```

---

### 3. 创建 `src/hooks/useRealTimeData.ts`

React Hook 封装：

```typescript
import { useEffect, useState, useCallback, useRef } from 'react'
import { telemetrySignalR, TelemetryDataPoint } from '../api/signalr'

interface UseRealTimeDataOptions {
  maxPoints?: number  // 保留的最大数据点数
  onData?: (data: TelemetryDataPoint[]) => void
}

export function useRealTimeData(options: UseRealTimeDataOptions = {}) {
  const { maxPoints = 100, onData } = options
  const [latestData, setLatestData] = useState<Map<string, TelemetryDataPoint>>(new Map())
  const [connected, setConnected] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const historyRef = useRef<Map<string, TelemetryDataPoint[]>>(new Map())

  // 连接 SignalR
  useEffect(() => {
    const connect = async () => {
      try {
        await telemetrySignalR.connect()
        await telemetrySignalR.subscribeAll()
        setConnected(true)
        setError(null)
      } catch (err) {
        setError('连接失败，将使用轮询模式')
        setConnected(false)
      }
    }

    connect()

    return () => {
      telemetrySignalR.disconnect()
    }
  }, [])

  // 处理接收到的数据
  useEffect(() => {
    const unsubscribe = telemetrySignalR.onData((data) => {
      // 更新最新值
      setLatestData(prev => {
        const newMap = new Map(prev)
        data.forEach(point => {
          const key = `${point.deviceId}:${point.tagId}`
          newMap.set(key, point)
        })
        return newMap
      })

      // 更新历史数据
      data.forEach(point => {
        const key = `${point.deviceId}:${point.tagId}`
        const history = historyRef.current.get(key) || []
        history.push(point)
        if (history.length > maxPoints) {
          history.shift()
        }
        historyRef.current.set(key, history)
      })

      // 回调
      onData?.(data)
    })

    return unsubscribe
  }, [maxPoints, onData])

  // 获取特定标签的历史数据
  const getHistory = useCallback((deviceId: string, tagId: string): TelemetryDataPoint[] => {
    const key = `${deviceId}:${tagId}`
    return historyRef.current.get(key) || []
  }, [])

  // 获取所有最新值数组
  const latestValues = Array.from(latestData.values())

  return {
    latestData: latestValues,
    connected,
    error,
    getHistory
  }
}
```

---

### 4. 更新 `src/pages/Dashboard/index.tsx`

使用 WebSocket 替代轮询：

```typescript
import { useEffect, useState, useRef, useCallback } from 'react'
import { Card, Row, Col, Statistic, Table, Tag, Spin, message, Select, Badge } from 'antd'
import { ReloadOutlined, CheckCircleOutlined, ClockCircleOutlined, WifiOutlined, DisconnectOutlined } from '@ant-design/icons'
import ReactECharts from 'echarts-for-react'
import { getLatestTelemetry, getTags, queryTelemetry } from '../../api/telemetry'
import { useRealTimeData } from '../../hooks/useRealTimeData'
import type { TelemetryDataPoint as ApiTelemetryDataPoint, TagInfo } from '../../types/telemetry'

// 趋势数据点
interface TrendPoint {
  time: string
  timestamp: number
  value: number
}

export default function Dashboard() {
  const [loading, setLoading] = useState(true)
  const [tags, setTags] = useState<TagInfo[]>([])
  const [lastUpdate, setLastUpdate] = useState<Date | null>(null)
  
  // 趋势图相关
  const [selectedTag, setSelectedTag] = useState<string>('')
  const [trendData, setTrendData] = useState<TrendPoint[]>([])
  const maxTrendPoints = 60
  const trendDataRef = useRef<TrendPoint[]>([])

  // 使用 WebSocket 实时数据
  const { latestData, connected, error } = useRealTimeData({
    maxPoints: maxTrendPoints,
    onData: (data) => {
      // 更新趋势图
      if (selectedTag) {
        const point = data.find(d => d.tagId === selectedTag)
        if (point && typeof point.value === 'number') {
          const now = new Date()
          const newPoint: TrendPoint = {
            time: now.toLocaleTimeString('zh-CN'),
            timestamp: point.ts,
            value: point.value
          }
          
          const lastPoint = trendDataRef.current[trendDataRef.current.length - 1]
          if (!lastPoint || lastPoint.timestamp !== newPoint.timestamp) {
            trendDataRef.current = [...trendDataRef.current, newPoint].slice(-maxTrendPoints)
            setTrendData([...trendDataRef.current])
          }
        }
      }
      setLastUpdate(new Date())
    }
  })

  // 加载标签列表（只需要一次）
  const loadTags = async () => {
    try {
      setLoading(true)
      const res = await getTags()
      if (res.success && res.data) {
        setTags(res.data)
        if (!selectedTag && res.data.length > 0) {
          setSelectedTag(res.data[0].tagId)
        }
      }
    } catch (err) {
      message.error('获取标签列表失败')
    } finally {
      setLoading(false)
    }
  }

  // 加载历史数据用于初始化趋势图
  const loadHistoricalData = async (tagId: string) => {
    try {
      const now = Date.now()
      const res = await queryTelemetry({
        tagId,
        startTs: now - 5 * 60 * 1000,
        endTs: now,
        limit: maxTrendPoints
      })
      
      if (res.success && res.data && res.data.length > 0) {
        const sorted = [...res.data].sort((a, b) => a.ts - b.ts)
        const points: TrendPoint[] = sorted.map(d => ({
          time: new Date(d.ts).toLocaleTimeString('zh-CN'),
          timestamp: d.ts,
          value: typeof d.value === 'number' ? d.value : 0
        }))
        trendDataRef.current = points
        setTrendData(points)
      }
    } catch (error) {
      console.error('Load historical data error:', error)
    }
  }

  useEffect(() => {
    loadTags()
  }, [])

  useEffect(() => {
    if (selectedTag) {
      trendDataRef.current = []
      setTrendData([])
      loadHistoricalData(selectedTag)
    }
  }, [selectedTag])

  // 表格列定义
  const columns = [
    {
      title: '设备',
      dataIndex: 'deviceId',
      key: 'deviceId',
      width: 120
    },
    {
      title: '标签',
      dataIndex: 'tagId',
      key: 'tagId',
      width: 150
    },
    {
      title: '当前值',
      dataIndex: 'value',
      key: 'value',
      width: 120,
      render: (value: number | string | boolean | null) => (
        <span style={{ fontWeight: 'bold', fontSize: 18, color: '#1890ff' }}>{String(value)}</span>
      )
    },
    {
      title: '类型',
      dataIndex: 'valueType',
      key: 'valueType',
      width: 100,
      render: (type: string) => <Tag color="blue">{type}</Tag>
    },
    {
      title: '质量',
      dataIndex: 'quality',
      key: 'quality',
      width: 100,
      render: (quality: number) => (
        quality === 192 ? (
          <Tag color="success" icon={<CheckCircleOutlined />}>Good</Tag>
        ) : (
          <Tag color="warning">Bad ({quality})</Tag>
        )
      )
    },
    {
      title: '更新时间',
      dataIndex: 'ts',
      key: 'ts',
      render: (ts: number) => new Date(ts).toLocaleString('zh-CN')
    }
  ]

  // ECharts 配置
  const getChartOption = () => {
    const times = trendData.map(d => d.time)
    const values = trendData.map(d => d.value)
    
    const minVal = values.length > 0 ? Math.min(...values) : 0
    const maxVal = values.length > 0 ? Math.max(...values) : 100
    const padding = (maxVal - minVal) * 0.1 || 10
    
    return {
      title: {
        text: `${selectedTag || '请选择标签'} 实时趋势`,
        left: 'center',
        textStyle: { fontSize: 16, fontWeight: 'normal' }
      },
      tooltip: {
        trigger: 'axis',
        formatter: (params: any) => {
          const data = params[0]
          if (!data) return ''
          return `${data.name}<br/>值: <b>${data.value}</b>`
        }
      },
      grid: {
        left: '3%', right: '4%', bottom: '3%', top: '15%',
        containLabel: true
      },
      xAxis: {
        type: 'category',
        boundaryGap: false,
        data: times,
        axisLabel: { rotate: 45, fontSize: 10 }
      },
      yAxis: {
        type: 'value',
        min: Math.floor(minVal - padding),
        max: Math.ceil(maxVal + padding),
        axisLabel: { formatter: (val: number) => val.toFixed(0) }
      },
      series: [{
        name: selectedTag,
        type: 'line',
        smooth: true,
        symbol: 'circle',
        symbolSize: 6,
        itemStyle: { color: '#1890ff' },
        lineStyle: { width: 2, color: '#1890ff' },
        areaStyle: {
          color: {
            type: 'linear', x: 0, y: 0, x2: 0, y2: 1,
            colorStops: [
              { offset: 0, color: 'rgba(24, 144, 255, 0.3)' },
              { offset: 1, color: 'rgba(24, 144, 255, 0.05)' }
            ]
          }
        },
        data: values
      }],
      animation: true,
      animationDuration: 300
    }
  }

  const totalPoints = tags.reduce((sum, t) => sum + t.pointCount, 0)
  const tagOptions = tags.map(t => ({ label: `${t.tagId} (${t.deviceId})`, value: t.tagId }))

  return (
    <div>
      {/* 统计卡片 */}
      <Row gutter={16} style={{ marginBottom: 16 }}>
        <Col span={6}>
          <Card>
            <Statistic
              title="在线设备"
              value={new Set(tags.map(t => t.deviceId)).size}
              prefix={<CheckCircleOutlined style={{ color: '#52c41a' }} />}
            />
          </Card>
        </Col>
        <Col span={6}>
          <Card>
            <Statistic
              title="监控点位"
              value={tags.length}
            />
          </Card>
        </Col>
        <Col span={6}>
          <Card>
            <Statistic
              title="总数据量"
              value={totalPoints}
            />
          </Card>
        </Col>
        <Col span={6}>
          <Card>
            <Statistic
              title={
                <span>
                  连接状态{' '}
                  {connected ? (
                    <Badge status="success" />
                  ) : (
                    <Badge status="error" />
                  )}
                </span>
              }
              value={lastUpdate ? lastUpdate.toLocaleTimeString('zh-CN') : '-'}
              prefix={connected ? <WifiOutlined style={{ color: '#52c41a' }} /> : <DisconnectOutlined style={{ color: '#ff4d4f' }} />}
            />
          </Card>
        </Col>
      </Row>

      {/* 实时趋势图 */}
      <Card 
        title="实时趋势" 
        style={{ marginBottom: 16 }}
        extra={
          <Select
            style={{ width: 200 }}
            placeholder="选择标签"
            value={selectedTag || undefined}
            onChange={setSelectedTag}
            options={tagOptions}
          />
        }
      >
        <ReactECharts option={getChartOption()} style={{ height: 300 }} notMerge={true} />
      </Card>

      {/* 实时数据表格 */}
      <Card
        title="实时数据"
        extra={
          connected ? (
            <Tag color="success" icon={<WifiOutlined />}>实时连接</Tag>
          ) : (
            <Tag color="error" icon={<DisconnectOutlined />}>连接断开</Tag>
          )
        }
      >
        <Spin spinning={loading}>
          <Table
            dataSource={latestData}
            columns={columns}
            rowKey={(record) => `${record.deviceId}-${record.tagId}`}
            pagination={false}
            size="middle"
          />
        </Spin>
      </Card>
    </div>
  )
}
```

---

## Vite 代理配置

确保 `vite.config.ts` 包含 WebSocket 代理：

```typescript
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 3000,
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true
      },
      '/hubs': {
        target: 'http://localhost:5000',
        changeOrigin: true,
        ws: true  // 启用 WebSocket 代理
      }
    }
  }
})
```

---

## 测试步骤

1. 启动 Host.Api：`dotnet run --project src/Host.Api`
2. 启动 Host.Edge（采集数据）：`dotnet run --project src/Host.Edge`
3. 启动前端：`cd intellimaint-ui && npm run dev`
4. 访问 http://localhost:3000
5. 观察：
   - 连接状态应显示"实时连接"
   - 数据应自动更新，无需手动刷新
   - 趋势图应实时变化

---

## 输出文件清单

后端：
1. `src/Host.Api/Hubs/TelemetryHub.cs` - **新建**
2. `src/Host.Api/Services/TelemetryBroadcastService.cs` - **新建**
3. `src/Host.Api/Program.cs` - **修改**

前端：
1. `intellimaint-ui/package.json` - **修改**（添加 @microsoft/signalr）
2. `intellimaint-ui/src/api/signalr.ts` - **新建**
3. `intellimaint-ui/src/hooks/useRealTimeData.ts` - **新建**
4. `intellimaint-ui/src/pages/Dashboard/index.tsx` - **修改**
5. `intellimaint-ui/vite.config.ts` - **修改**

---

## 注意事项

1. SignalR 需要 CORS 配置允许 credentials
2. Vite 代理需要配置 `ws: true` 支持 WebSocket
3. 广播服务需要使用 `IServiceScopeFactory` 创建 scope 访问 scoped 服务
4. 前端需要处理重连逻辑
