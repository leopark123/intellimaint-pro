# IntelliMaint Pro 项目深度分析

> 本文档帮助你从零开始理解这个工业预测性维护平台的设计与实现

---

## 目录

1. [项目概述](#一项目概述)
2. [架构设计](#二架构设计)
3. [后端详解](#三后端详解)
4. [前端详解](#四前端详解)
5. [数据流分析](#五数据流分析)
6. [核心设计模式](#六核心设计模式)
7. [关键代码解读](#七关键代码解读)
8. [学习路线图](#八学习路线图)

---

## 一、项目概述

### 1.1 这是什么项目？

**IntelliMaint Pro** 是一个**工业设备预测性维护平台**。

简单说：它从工业设备（如 PLC、变频器）采集数据，存储、分析，然后在设备可能出故障之前发出预警。

### 1.2 核心业务场景

```
┌─────────────────────────────────────────────────────────────────┐
│                        工厂现场                                  │
│                                                                 │
│   ┌─────┐  ┌─────┐  ┌─────┐                                    │
│   │电机1│  │电机2│  │水泵1│  ← 工业设备                          │
│   └──┬──┘  └──┬──┘  └──┬──┘                                    │
│      │        │        │                                        │
│      ▼        ▼        ▼                                        │
│   ┌───────────────────────┐                                     │
│   │     PLC / 变频器       │  ← 控制设备，已有数据               │
│   │  (KEPServerEX OPC UA) │                                     │
│   └───────────┬───────────┘                                     │
│               │                                                 │
│               │ OPC UA 协议                                      │
│               ▼                                                 │
│   ┌───────────────────────┐                                     │
│   │   IntelliMaint Pro    │  ← 我们的系统                        │
│   │   - 数据采集           │                                     │
│   │   - 存储分析           │                                     │
│   │   - 预警通知           │                                     │
│   └───────────────────────┘                                     │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### 1.3 核心价值

| 痛点 | 解决方案 | 价值 |
|------|----------|------|
| 设备突然坏了 | 提前 72 小时预警 | 减少停机损失 |
| 过度维护（没坏也修）| 按实际状态维护 | 降低维护成本 |
| 设备状态不透明 | 健康指数 0-100 | 一目了然 |

---

## 二、架构设计

### 2.1 整体架构图

```
┌─────────────────────────────────────────────────────────────────────────┐
│                              用户层                                      │
│                                                                         │
│    ┌─────────────────────────────────────────────────────────┐          │
│    │              React 前端 (端口 3000)                       │          │
│    │  ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐        │          │
│    │  │Dashboard│ │设备管理 │ │告警管理 │ │数据查询 │ ...     │          │
│    │  └────┬────┘ └────┬────┘ └────┬────┘ └────┬────┘        │          │
│    │       │           │           │           │              │          │
│    │       └───────────┴─────┬─────┴───────────┘              │          │
│    │                         │                                │          │
│    │                    ┌────┴────┐                           │          │
│    │                    │ SignalR │ ← 实时推送                 │          │
│    │                    │ HTTP API│ ← REST 请求                │          │
│    │                    └────┬────┘                           │          │
│    └─────────────────────────┼───────────────────────────────┘          │
│                              │                                          │
├──────────────────────────────┼──────────────────────────────────────────┤
│                              │           服务层                          │
│                              ▼                                          │
│    ┌─────────────────────────────────────────────────────────┐          │
│    │              Host.Api (.NET 8, 端口 5000)                │          │
│    │                                                         │          │
│    │  ┌─────────┐  ┌─────────┐  ┌─────────┐  ┌─────────┐    │          │
│    │  │认证授权  │  │遥测 API │  │告警 API │  │设备 API │    │          │
│    │  │JWT+RBAC │  │         │  │         │  │         │    │          │
│    │  └─────────┘  └─────────┘  └─────────┘  └─────────┘    │          │
│    │                                                         │          │
│    │  ┌─────────────────────────────────────────────────┐   │          │
│    │  │              SignalR Hub                         │   │          │
│    │  │         (实时数据广播)                            │   │          │
│    │  └─────────────────────────────────────────────────┘   │          │
│    └─────────────────────────┬───────────────────────────────┘          │
│                              │                                          │
├──────────────────────────────┼──────────────────────────────────────────┤
│                              │           数据层                          │
│                              ▼                                          │
│    ┌─────────────────────────────────────────────────────────┐          │
│    │                Infrastructure 层                        │          │
│    │                                                         │          │
│    │  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐     │          │
│    │  │   SQLite    │  │  Pipeline   │  │  Protocols  │     │          │
│    │  │  数据存储    │  │  数据管道   │  │  OPC UA     │     │          │
│    │  │             │  │             │  │  LibPlcTag  │     │          │
│    │  └─────────────┘  └─────────────┘  └─────────────┘     │          │
│    └─────────────────────────────────────────────────────────┘          │
│                                                                         │
├─────────────────────────────────────────────────────────────────────────┤
│                              采集层                                      │
│                                                                         │
│    ┌─────────────────────────────────────────────────────────┐          │
│    │              Host.Edge (边缘采集服务)                     │          │
│    │                                                         │          │
│    │  ┌─────────────┐  ┌─────────────┐                       │          │
│    │  │OPC UA采集器 │  │LibPlcTag   │                        │          │
│    │  │            │  │采集器      │                        │          │
│    │  └──────┬──────┘  └──────┬──────┘                       │          │
│    └─────────┼────────────────┼──────────────────────────────┘          │
│              │                │                                         │
│              ▼                ▼                                         │
│    ┌─────────────┐  ┌─────────────┐                                     │
│    │KEPServerEX  │  │ Allen-Bradley│  ← 工业设备/协议转换器              │
│    │(OPC UA服务器)│  │    PLC      │                                     │
│    └─────────────┘  └─────────────┘                                     │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

### 2.2 分层架构 (Clean Architecture)

```
                    ┌─────────────────────────┐
                    │       Host.Api          │  ← 最外层：HTTP 入口
                    │       Host.Edge         │     API、SignalR、采集
                    └───────────┬─────────────┘
                                │
                    ┌───────────▼─────────────┐
                    │     Infrastructure      │  ← 中间层：具体实现
                    │   SQLite, Pipeline      │     数据库、协议适配
                    │   OPC UA, LibPlcTag     │
                    └───────────┬─────────────┘
                                │
                    ┌───────────▼─────────────┐
                    │         Core            │  ← 最内层：业务核心
                    │   Contracts (DTO/实体)   │     纯 C# 代码
                    │   Abstractions (接口)   │     无外部依赖
                    └─────────────────────────┘

依赖方向：外层 → 内层（Core 不依赖任何外层）
```

### 2.3 为什么这样设计？

| 原则 | 体现 | 好处 |
|------|------|------|
| **依赖倒置** | Core 定义接口，Infrastructure 实现 | 可替换数据库（SQLite → TimescaleDB） |
| **关注点分离** | API/采集/存储 各自独立 | 改一处不影响其他 |
| **单一职责** | 每个 Repository 只管一类数据 | 代码清晰，易维护 |

---

## 三、后端详解

### 3.1 项目结构

```
src/
├── Core/                          # 核心层（无依赖）
│   ├── Abstractions/              # 接口定义
│   │   ├── Repositories.cs        # 数据仓储接口
│   │   ├── Pipeline.cs            # 管道接口
│   │   └── Collectors.cs          # 采集器接口
│   └── Contracts/                 # 数据契约
│       ├── Entities.cs            # 实体类（Device, Tag, Alarm...）
│       ├── TelemetryPoint.cs      # 遥测数据点
│       └── Auth.cs                # 认证相关
│
├── Infrastructure/                # 基础设施层
│   ├── Sqlite/                    # SQLite 实现
│   │   ├── DbExecutor.cs          # 数据库执行器
│   │   ├── TelemetryRepository.cs # 遥测仓储
│   │   ├── DeviceRepository.cs    # 设备仓储
│   │   └── ...
│   ├── Pipeline/                  # 数据管道
│   │   ├── TelemetryPipeline.cs   # 主管道
│   │   ├── TelemetryDispatcher.cs # 分发器
│   │   └── AlarmEvaluatorService.cs # 告警引擎
│   └── Protocols/                 # 协议实现
│       ├── OpcUa/                 # OPC UA 采集
│       └── LibPlcTag/             # Allen-Bradley 采集
│
├── Host.Api/                      # API 服务
│   ├── Endpoints/                 # Minimal API 端点
│   ├── Hubs/                      # SignalR Hub
│   ├── Services/                  # 后台服务
│   └── Program.cs                 # 启动配置
│
└── Host.Edge/                     # 边缘采集服务
    └── Program.cs                 # 采集启动
```

### 3.2 核心接口设计

**为什么用接口？**

```csharp
// ❌ 直接依赖具体类（耦合紧密）
public class TelemetryEndpoints
{
    private readonly SqliteTelemetryRepository _repo;  // 直接依赖 SQLite
}

// ✅ 依赖接口（松耦合）
public class TelemetryEndpoints
{
    private readonly ITelemetryRepository _repo;  // 依赖抽象
}
```

**好处**：
- 想换 PostgreSQL？只需新建 `PostgresTelemetryRepository` 实现同一接口
- 单元测试时可以用 Mock 对象替换真实数据库

### 3.3 关键接口一览

```csharp
// 遥测数据仓储
public interface ITelemetryRepository
{
    Task<int> AppendBatchAsync(IReadOnlyList<TelemetryPoint> batch, CancellationToken ct);
    Task<IReadOnlyList<TelemetryPoint>> QuerySimpleAsync(...);
    Task<IReadOnlyList<TelemetryPoint>> GetLatestAsync(...);
}

// 设备仓储
public interface IDeviceRepository
{
    Task<IReadOnlyList<DeviceDto>> ListAsync(CancellationToken ct);
    Task<DeviceDto?> GetAsync(string deviceId, CancellationToken ct);
    Task UpsertAsync(DeviceDto device, CancellationToken ct);
}

// 管道接口
public interface ITelemetryPipeline
{
    ValueTask<bool> WriteAsync(TelemetryPoint point, CancellationToken ct);
    long QueueDepth { get; }
}
```

### 3.4 API 设计 (Minimal API)

.NET 8 的 Minimal API 风格，比传统 Controller 更简洁：

```csharp
// 传统 Controller 方式
[ApiController]
[Route("api/[controller]")]
public class DevicesController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll() { ... }
}

// Minimal API 方式（本项目使用）
public static class DeviceEndpoints
{
    public static void MapDeviceEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/devices")
            .RequireAuthorization();

        group.MapGet("/", GetAll);
        group.MapGet("/{id}", GetById);
        group.MapPost("/", Create);
        group.MapPut("/{id}", Update);
        group.MapDelete("/{id}", Delete);
    }

    private static async Task<IResult> GetAll(
        IDeviceRepository repo,  // 依赖注入
        CancellationToken ct)
    {
        var devices = await repo.ListAsync(ct);
        return Results.Ok(new { success = true, data = devices });
    }
}
```

---

## 四、前端详解

### 4.1 技术栈

| 技术 | 用途 |
|------|------|
| **React 18** | UI 框架 |
| **TypeScript** | 类型安全 |
| **Ant Design 5** | UI 组件库 |
| **Recharts** | 图表 |
| **SignalR Client** | 实时通信 |
| **Axios** | HTTP 请求 |
| **React Context** | 状态管理 |

### 4.2 项目结构

```
intellimaint-ui/src/
├── api/                    # API 调用封装
│   ├── client.ts           # Axios 配置（拦截器）
│   ├── telemetry.ts        # 遥测 API
│   ├── device.ts           # 设备 API
│   ├── alarm.ts            # 告警 API
│   └── signalr.ts          # SignalR 连接
│
├── pages/                  # 页面组件
│   ├── Dashboard/          # 监控看板
│   ├── DeviceManagement/   # 设备管理
│   ├── AlarmManagement/    # 告警管理
│   ├── DataExplorer/       # 数据查询
│   └── Login/              # 登录页
│
├── types/                  # TypeScript 类型定义
│   ├── device.ts           # 设备类型
│   ├── telemetry.ts        # 遥测类型
│   └── auth.ts             # 认证类型
│
├── store/                  # 状态管理
│   └── authStore.tsx       # 认证状态（Context）
│
├── components/             # 公共组件
│   └── Layout.tsx          # 布局组件
│
└── App.tsx                 # 应用入口
```

### 4.3 API 客户端设计

```typescript
// api/client.ts - 统一的 HTTP 客户端

import axios from 'axios'

const apiClient = axios.create({
  baseURL: '/api',
  timeout: 30000
})

// 请求拦截器 - 自动添加 Token
apiClient.interceptors.request.use(async (config) => {
  // 1. 检查 Token 是否快过期
  if (isTokenExpiringSoon()) {
    await refreshTokenIfNeeded()  // 自动刷新
  }
  
  // 2. 添加 Authorization 头
  const token = getToken()
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  
  return config
})

// 响应拦截器 - 统一错误处理
apiClient.interceptors.response.use(
  (response) => response.data,  // 直接返回 data
  (error) => {
    if (error.response?.status === 401) {
      // Token 失效，跳转登录
      window.location.href = '/login'
    }
    return Promise.reject(error)
  }
)
```

### 4.4 实时数据 (SignalR)

```typescript
// api/signalr.ts

import * as signalR from '@microsoft/signalr'

export function createSignalRConnection() {
  return new signalR.HubConnectionBuilder()
    .withUrl('/hubs/telemetry', {
      accessTokenFactory: () => getToken() || ''
    })
    .withAutomaticReconnect()
    .build()
}

// 使用示例（Dashboard 页面）
useEffect(() => {
  const connection = createSignalRConnection()
  
  // 监听服务端推送
  connection.on('ReceiveData', (points: TelemetryDataPoint[]) => {
    // 更新图表数据
    setChartData(prev => [...prev, ...points])
  })
  
  // 启动连接
  connection.start()
  
  // 订阅所有数据
  connection.invoke('SubscribeAll')
  
  return () => connection.stop()
}, [])
```

### 4.5 认证状态管理 (React Context)

```typescript
// store/authStore.tsx

interface AuthState {
  token: string | null
  refreshToken: string | null
  username: string | null
  role: string | null
  isAuthenticated: boolean
}

// Context Provider
export function AuthProvider({ children }) {
  const [auth, setAuth] = useState<AuthState>(loadAuthState)
  
  const login = (response: LoginResponse) => {
    const newState = { ...response, isAuthenticated: true }
    setAuth(newState)
    saveAuthState(newState)  // 保存到 localStorage
  }
  
  const logout = () => {
    setAuth(initialState)
    clearAuth()
  }
  
  return (
    <AuthContext.Provider value={{ auth, login, logout }}>
      {children}
    </AuthContext.Provider>
  )
}

// 使用
function SomeComponent() {
  const { auth, logout } = useAuth()
  
  return auth.isAuthenticated 
    ? <button onClick={logout}>登出</button>
    : <Link to="/login">登录</Link>
}
```

---

## 五、数据流分析

### 5.1 数据采集流程

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           数据采集流程                                   │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  ┌─────────────┐     ┌─────────────┐     ┌─────────────┐               │
│  │ OPC UA 服务器│────►│ OpcUaCollector│────►│  Pipeline   │               │
│  │ (KEPServerEX)│     │   采集器     │     │   队列      │               │
│  └─────────────┘     └─────────────┘     └──────┬──────┘               │
│                                                  │                      │
│                           ┌──────────────────────┼──────────────────┐   │
│                           │                      │                  │   │
│                           ▼                      ▼                  ▼   │
│                    ┌──────────────┐      ┌──────────────┐   ┌───────────┐
│                    │TelemetryRepo │      │AlarmEvaluator│   │ SignalR   │
│                    │  (写入数据库) │      │ (告警判断)    │   │ (实时推送) │
│                    └──────────────┘      └──────────────┘   └───────────┘
│                                                                         │
│  时间线：                                                                │
│  ────────────────────────────────────────────────────────────────────►  │
│  │              │              │              │              │          │
│  采集         入队          分发          存储/告警       前端显示       │
│  (1秒/次)    (~1ms)       (~1ms)         (~5ms)         (即时)         │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

### 5.2 Pipeline 核心设计

**问题**：采集速度可能超过数据库写入速度，怎么办？

**解决方案**：有界队列 + DropOldest 策略

```csharp
// TelemetryPipeline.cs

public sealed class TelemetryPipeline : ITelemetryPipeline
{
    private readonly Channel<TelemetryPoint> _channel;
    
    public TelemetryPipeline()
    {
        // 创建有界队列（最多 10000 个数据点）
        _channel = Channel.CreateBounded<TelemetryPoint>(
            new BoundedChannelOptions(10000)
            {
                FullMode = BoundedChannelFullMode.Wait
            });
    }
    
    public async ValueTask<bool> WriteAsync(TelemetryPoint point)
    {
        // 尝试写入
        if (_channel.Writer.TryWrite(point))
            return true;
        
        // 队列满了 → 丢弃最旧的数据
        if (_channel.Reader.TryRead(out var oldest))
        {
            // 导出被丢弃的数据（不丢失）
            await _overflowExporter.ExportAsync(oldest);
        }
        
        // 重新写入新数据
        return _channel.Writer.TryWrite(point);
    }
}
```

### 5.3 告警触发流程

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           告警触发流程                                   │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  配置告警规则（UI）:                                                      │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │ 规则名: 温度过高                                                  │   │
│  │ 监控标签: Motor1.Temperature                                     │   │
│  │ 条件: > 80°C                                                     │   │
│  │ 持续时间: 30秒                                                    │   │
│  │ 严重级别: Warning                                                 │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                         │
│  数据到达时:                                                             │
│                                                                         │
│  TelemetryPoint { TagId: "Motor1.Temperature", Value: 85 }              │
│           │                                                             │
│           ▼                                                             │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │ AlarmEvaluatorService                                            │   │
│  │                                                                   │   │
│  │  1. 匹配规则: TagId == "Motor1.Temperature" ✓                     │   │
│  │  2. 判断条件: 85 > 80 ✓                                           │   │
│  │  3. 检查持续时间: 已持续 35秒 > 30秒 ✓                             │   │
│  │  4. 检查去重: 无未关闭的同规则告警 ✓                               │   │
│  │  5. 创建告警记录                                                  │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                         │
│           │                                                             │
│           ▼                                                             │
│  AlarmRecord { Message: "温度过高: Motor1.Temperature > 80, 当前=85" }  │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 六、核心设计模式

### 6.1 仓储模式 (Repository Pattern)

**定义**：封装数据访问逻辑，提供统一接口

```csharp
// 接口定义（Core 层）
public interface IDeviceRepository
{
    Task<IReadOnlyList<DeviceDto>> ListAsync(CancellationToken ct);
    Task<DeviceDto?> GetAsync(string deviceId, CancellationToken ct);
    Task UpsertAsync(DeviceDto device, CancellationToken ct);
}

// SQLite 实现（Infrastructure 层）
public class DeviceRepository : IDeviceRepository
{
    private readonly IDbExecutor _db;
    
    public async Task<IReadOnlyList<DeviceDto>> ListAsync(CancellationToken ct)
    {
        const string sql = "SELECT * FROM device ORDER BY created_utc DESC";
        return await _db.QueryAsync(sql, MapDevice, null, ct);
    }
}
```

### 6.2 依赖注入 (Dependency Injection)

```csharp
// Program.cs - 注册服务
builder.Services.AddSingleton<IDbExecutor, DbExecutor>();
builder.Services.AddSingleton<IDeviceRepository, DeviceRepository>();
builder.Services.AddSingleton<ITelemetryPipeline, TelemetryPipeline>();

// 使用时自动注入
public static async Task<IResult> GetDevices(
    IDeviceRepository repo,  // 自动注入
    CancellationToken ct)
{
    var devices = await repo.ListAsync(ct);
    return Results.Ok(devices);
}
```

### 6.3 生产者-消费者模式 (Producer-Consumer)

```
┌─────────────┐     ┌─────────────┐     ┌─────────────┐
│  生产者      │     │   Channel   │     │  消费者      │
│ (采集器)     │────►│   (队列)    │────►│ (存储/告警)  │
└─────────────┘     └─────────────┘     └─────────────┘

好处：
1. 解耦 - 生产者不关心消费者如何处理
2. 削峰 - 突发数据被队列缓冲
3. 并发 - 可以有多个消费者并行处理
```

### 6.4 拦截器模式 (Interceptor)

```typescript
// 前端 Axios 拦截器

// 请求拦截 - 自动添加 Token
apiClient.interceptors.request.use((config) => {
  config.headers.Authorization = `Bearer ${token}`
  return config
})

// 响应拦截 - 统一处理 401
apiClient.interceptors.response.use(
  (response) => response.data,
  (error) => {
    if (error.response?.status === 401) {
      redirect('/login')
    }
  }
)
```

---

## 七、关键代码解读

### 7.1 JWT 认证流程

```csharp
// 1. 登录 - 验证密码，生成 Token
public static async Task<IResult> Login(
    LoginRequest request,
    IUserRepository userRepo,
    JwtService jwtService)
{
    // 验证用户名密码
    var user = await userRepo.ValidateCredentialsAsync(
        request.Username, request.Password);
    
    if (user == null)
        return Results.Unauthorized();
    
    // 生成 JWT Token（有效期 15 分钟）
    var (response, refreshExpires) = jwtService.GenerateTokens(user);
    
    // 保存 Refresh Token（有效期 7 天）
    await userRepo.SaveRefreshTokenAsync(
        user.UserId, response.RefreshToken, refreshExpires);
    
    return Results.Ok(response);
}

// 2. JwtService - 生成 Token
public (LoginResponse, long) GenerateTokens(UserDto user)
{
    var claims = new[]
    {
        new Claim(ClaimTypes.NameIdentifier, user.UserId),
        new Claim(ClaimTypes.Name, user.Username),
        new Claim(ClaimTypes.Role, user.Role)  // 角色写入 Token
    };
    
    var token = new JwtSecurityToken(
        issuer: "IntelliMaint",
        audience: "IntelliMaint",
        claims: claims,
        expires: DateTime.UtcNow.AddMinutes(15),  // 15 分钟过期
        signingCredentials: credentials
    );
    
    return (new LoginResponse { Token = token, ... }, ...);
}

// 3. 前端自动刷新
async function refreshTokenIfNeeded() {
    if (!isTokenExpiringSoon()) return;  // 还没过期
    
    const response = await api.post('/auth/refresh', {
        refreshToken: getRefreshToken()
    });
    
    saveAuthState(response);  // 保存新 Token
}
```

### 7.2 RBAC 权限控制

```csharp
// 定义权限策略
builder.Services.AddAuthorization(options =>
{
    // 只有 Admin 可访问
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole("Admin"));
    
    // Admin 或 Operator 可访问
    options.AddPolicy("OperatorOrAbove", policy =>
        policy.RequireRole("Admin", "Operator"));
});

// 应用到端点
app.MapDelete("/api/devices/{id}", DeleteDevice)
    .RequireAuthorization("AdminOnly");  // 只有 Admin 能删除设备

app.MapPost("/api/alarms/{id}/ack", AckAlarm)
    .RequireAuthorization("OperatorOrAbove");  // Operator 可以确认告警
```

### 7.3 SignalR 实时推送

```csharp
// 后端广播服务
public class TelemetryBroadcastService : BackgroundService
{
    private readonly IHubContext<TelemetryHub> _hub;
    private readonly ChannelReader<TelemetryPoint> _reader;
    
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var point in _reader.ReadAllAsync(ct))
        {
            // 推送给订阅了该设备的客户端
            await _hub.Clients
                .Group($"device:{point.DeviceId}")
                .SendAsync("ReceiveData", new[] { point }, ct);
            
            // 也推送给订阅了全部数据的客户端
            await _hub.Clients
                .Group("all")
                .SendAsync("ReceiveData", new[] { point }, ct);
        }
    }
}

// 前端接收
connection.on('ReceiveData', (points) => {
    // 更新图表
    setChartData(prev => [...prev.slice(-100), ...points]);
});
```

---

## 八、学习路线图

### 8.1 推荐学习顺序

```
第 1 周：理解架构
├── 阅读本文档
├── 运行项目，熟悉 UI
├── 用 Swagger 测试 API
└── 理解分层架构

第 2 周：掌握后端
├── 阅读 Core 层代码（接口、实体）
├── 阅读一个 Repository 实现
├── 跟踪一次 API 请求的完整流程
└── 理解 JWT 认证流程

第 3 周：掌握前端
├── 理解 React Context 状态管理
├── 理解 Axios 拦截器
├── 理解 SignalR 实时通信
└── 修改一个页面，加深理解

第 4 周：深入数据流
├── 阅读 Pipeline 实现
├── 阅读 OPC UA 采集器
├── 理解告警触发流程
└── 尝试添加一个新功能
```

### 8.2 动手练习建议

| 练习 | 难度 | 收获 |
|------|------|------|
| 添加一个新的 API 端点 | ⭐ | 理解 Minimal API |
| 添加一个新的数据库表 | ⭐⭐ | 理解仓储模式 |
| 添加一个新的前端页面 | ⭐⭐ | 理解 React + API 集成 |
| 实现健康指数计算 | ⭐⭐⭐ | 理解数据流 + 算法 |
| 添加新的工业协议支持 | ⭐⭐⭐⭐ | 理解采集器设计 |

### 8.3 关键文件阅读清单

**必读**（按顺序）：

1. `Core/Contracts/Entities.cs` - 理解数据结构
2. `Core/Abstractions/Repositories.cs` - 理解接口设计
3. `Infrastructure/Sqlite/TelemetryRepository.cs` - 理解具体实现
4. `Host.Api/Endpoints/TelemetryEndpoints.cs` - 理解 API 设计
5. `Host.Api/Program.cs` - 理解启动配置
6. `intellimaint-ui/src/api/client.ts` - 理解前端 HTTP 客户端
7. `intellimaint-ui/src/store/authStore.tsx` - 理解状态管理

---

## 附录：常见问题

### Q1: 为什么用 SQLite 而不是 MySQL/PostgreSQL？

**答**：MVP 阶段快速验证。SQLite 单文件部署，无需安装数据库服务器。后续可切换到 TimescaleDB（时序数据库）。

### Q2: 为什么前端不用 Redux？

**答**：项目状态简单，React Context 足够。Redux 适合大型复杂状态管理。

### Q3: SignalR vs WebSocket？

**答**：SignalR 是 WebSocket 的封装，自动处理：
- 连接断开重连
- 消息序列化
- 多客户端管理（分组）
- 回退到长轮询（老浏览器）

### Q4: 为什么 Token 只有 15 分钟有效期？

**答**：安全考虑。Token 泄露后影响时间短。配合 Refresh Token（7天）实现无感刷新。

---

*有问题随时问我！*
