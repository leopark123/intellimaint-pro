# IntelliMaint Pro 项目知识库

> **重要**: 每个 Batch 开发前必须先读取此文件，开发后必须更新此文件。

---

## 1. 命名空间与接口映射

### IntelliMaint.Core.Abstractions（接口定义层）

| 接口 | 用途 | 添加版本 |
|------|------|----------|
| `ICollector` | 采集器接口 | v1 |
| `ITelemetrySource` | 遥测数据源 | v1 |
| `ITagTypeMapper` | 类型映射器 | v1 |
| `ISystemClock` | 系统时钟 | v1 |
| `ITelemetryPipeline` | 遥测管道 | v1 |
| `ITelemetryDispatcher` | 遥测分发器 | v1 |
| `IOverflowExporter` | 溢出导出器 | v1 |
| `IHealthProbe` | 健康探针 | v1 |
| `IDatabaseHealthChecker` | 数据库健康检查 | v1 |
| `ITelemetryRepository` | 遥测数据仓储 | v1 |
| `IDeviceRepository` | 设备仓储 | v1 |
| `ITagRepository` | 标签仓储 | v1 |
| `IAlarmRepository` | 告警仓储 | v1 |
| `IHealthSnapshotRepository` | 健康快照仓储 | v1 |
| `IMqttOutboxRepository` | MQTT Outbox 仓储 | v1 |
| `IDbConfigProvider` | 数据库配置提供者 | **v33** |
| `IConfigRevisionProvider` | 配置版本提供者 | **v33** |

### IntelliMaint.Infrastructure.Sqlite（⚠️ 接口仍在此处）

| 接口 | 用途 | 备注 |
|------|------|------|
| `IDbExecutor` | 数据库执行器 | 基础设施，保留 |
| `ISchemaManager` | Schema 管理器 | 基础设施，保留 |
| `ISqliteConnectionFactory` | 连接工厂 | 基础设施，保留 |

> **v36 更新**：`IAlarmRuleRepository`、`IAuditLogRepository`、`ISystemSettingRepository`、`IUserRepository` 已迁移到 `Core.Abstractions`

### 使用规则

```
修改 using 时的检查清单：
1. 删除 using IntelliMaint.Infrastructure.Sqlite 前，检查是否使用了：
   - IAuditLogRepository
   - IAlarmRuleRepository  
   - ISystemSettingRepository
   - IUserRepository
   - IDbExecutor
   - ConfigWatcherOptions (如果绑定配置)

2. 如果上述任一接口在使用，必须保留该 using
```

### IDbExecutor 方法签名（v35 新增）

```csharp
// 写操作（串行化）
Task<int> ExecuteNonQueryAsync(string sql, object? parameters = null, CancellationToken ct = default);

// 标量查询
Task<T?> ExecuteScalarAsync<T>(string sql, object? parameters = null, CancellationToken ct = default);

// 查询列表
Task<List<T>> QueryAsync<T>(string sql, Func<SqliteDataReader, T> mapper, object? parameters = null, CancellationToken ct = default);

// 查询单条
Task<T?> QuerySingleAsync<T>(string sql, Func<SqliteDataReader, T> mapper, object? parameters = null, CancellationToken ct = default);

// 参数传递方式：匿名对象
await _db.ExecuteNonQueryAsync(sql, new { Key1 = value1, Key2 = value2 }, ct);
await _db.QueryAsync(sql, mapper, new { DeviceId = deviceId }, ct);
```

### IAuditLogRepository 方法签名（v35 新增）

```csharp
// 创建审计日志（需传完整 AuditLogEntry）
Task<long> CreateAsync(AuditLogEntry entry, CancellationToken ct);

// 使用示例
await auditRepo.CreateAsync(new AuditLogEntry
{
    Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
    UserId = user.UserId,
    UserName = user.Username,
    Action = "Login",
    ResourceType = "Auth",
    Details = "登录成功"
}, ct);
```

### IAuditLogRepository 方法签名（v35 新增）

```csharp
// 创建审计日志（需传完整 AuditLogEntry）
Task<long> CreateAsync(AuditLogEntry entry, CancellationToken ct);

// 使用示例
await auditRepo.CreateAsync(new AuditLogEntry
{
    Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
    UserId = user.UserId,
    UserName = user.Username,
    Action = "Login",
    ResourceType = "Auth",
    Details = "登录成功"
}, ct);
```

---

## 2. 项目结构

```
intellimaint-pro/
├── src/
│   ├── Core/                           # 核心层（接口 + DTO）
│   │   ├── Abstractions/               # 所有接口定义
│   │   └── Contracts/                  # DTO、枚举、配置类
│   │
│   ├── Infrastructure/
│   │   ├── Sqlite/                     # SQLite 实现
│   │   ├── Pipeline/                   # 数据管道
│   │   └── Protocols/
│   │       ├── OpcUa/                  # OPC UA 协议（不依赖 Sqlite）
│   │       └── LibPlcTag/              # LibPlcTag 协议
│   │
│   ├── Host.Api/                       # API 服务
│   │   ├── Endpoints/                  # Minimal API 端点
│   │   ├── Hubs/                       # SignalR Hub
│   │   └── Services/                   # 后台服务
│   │
│   └── Host.Edge/                      # 边缘采集服务
│
├── tests/
│   └── Unit/                           # 单元测试
│
├── intellimaint-ui/                    # React 前端
│
└── docs/
    ├── PROJECT_KNOWLEDGE.md            # 本文件
    └── chatgpt-instructions/           # ChatGPT 开发指令
```

---

## 3. 技术约束（必须遵守）

### 3.1 代码规范

| 约束 | 说明 | 原因 |
|------|------|------|
| 不使用 `.WithOpenApi()` | Minimal API 端点不加此方法 | 项目未启用 OpenAPI |
| `ExecuteScalarAsync<T>` 返回值处理 | 使用条件判断，不用 `?? 0` | 避免空值警告 |
| SQL 字段名格式 | 小写 + 下划线（`device_id`） | 统一风格 |
| 日志级别 | Debug=调试，Info=关键事件，Warning=可恢复异常 | 生产环境日志量控制 |

### 3.2 架构约束

| 约束 | 说明 |
|------|------|
| Core 层无外部依赖 | 只能依赖 .NET BCL |
| 协议层不依赖 Sqlite | OpcUa/LibPlcTag 只依赖 Core |
| Host 层负责 DI 组装 | 所有服务注册在 Host 层 |
| 接口定义在 Core | 实现在 Infrastructure |

### 3.3 数据库约束

| 约束 | 说明 |
|------|------|
| Schema 版本控制 | 当前版本: **v4** |
| 迁移方法命名 | `ApplyMigrationV{N}Async` |
| 全局写锁 | `SemaphoreSlim(1,1)` 保护并发写入 |

---

## 4. 踩坑记录

### v33 踩坑

| 问题 | 原因 | 解决方案 |
|------|------|----------|
| 删除 `using Infrastructure.Sqlite` 导致编译失败 | `IAuditLogRepository` 仍在该命名空间 | 删除 using 前检查所有依赖类型 |
| ChatGPT 假设 `ISystemSettingRepository` 在 Core | 实际在 Sqlite 命名空间 | 审核时核对命名空间映射表 |

### v35 踩坑

| 问题 | 原因 | 解决方案 |
|------|------|----------|
| UserRepository 使用不存在的 IDbExecutor 方法 | ChatGPT 假设有 `QuerySingleOrDefaultAsync`、`ExecuteAsync` | 使用正确方法：`QuerySingleAsync`、`ExecuteNonQueryAsync` |
| QueryAsync 参数签名不匹配 | ChatGPT 使用 `Action<SqliteCommand>` 风格 | 使用匿名对象传参 `new { Key = value }` |
| IAuditLogRepository.AddAsync 不存在 | ChatGPT 假设有简化方法 | 使用 `CreateAsync(AuditLogEntry entry, CancellationToken ct)` |
| 全新数据库无 user 表 | SchemaManager v1 后未继续迁移到 v4 | 修复 InitializeAsync：v1 后继续执行 MigrateAsync |
| admin 登录 401 | ChatGPT 提供的密码哈希错误 | 正确哈希：`JAvlGPq9JyTdtvBO6x2llnRI1+gxwIyPqCKAn3THIKk=` |
| IAuditLogRepository.AddAsync 不存在 | ChatGPT 假设有简化方法 | 使用 `CreateAsync(AuditLogEntry entry, CancellationToken ct)` |

---

## 5. 版本历史

| 版本 | 主要变更 | Schema |
|------|----------|--------|
| v1-v31 | 基础功能开发 | v1-v2 |
| v32 | OPC UA 数据库配置 + 热重载 | v2 |
| v33 | 架构解耦 + revision 机制 + Partial Index | v3 |
| v34 | SignalR UnsubscribeAll + 集成测试骨架 | v3 |
| v35 | JWT 认证授权 + 用户表 + 前端登录 | v4 |
| v36 | 接口位置重构（4个接口迁移到 Core） | v4 |
| v37 | 审计日志完善（17个审计点 + JWT用户提取） | v4 |
| v38 | 角色授权（Admin/Operator/Viewer 三级权限） | v4 |
| v36 | 接口位置重构（4个业务接口迁移到 Core） | v4 |
| v37 | 审计日志完善（17个审计点全覆盖 + JWT用户提取） | v4 |

---

## 6. 协作流程

```
┌─────────────────────────────────────────────────────────┐
│  Step 1: Claude 读取 PROJECT_KNOWLEDGE.md              │
│          ↓                                              │
│  Step 2: Claude 生成开发指令（含命名空间检查）           │
│          ↓ 用户确认                                     │
│  Step 3: 用户转交 ChatGPT 执行                          │
│          ↓                                              │
│  Step 4: Claude 按审核清单检查代码                      │
│          ↓ 审核通过                                     │
│  Step 5: 用户本地 dotnet build 验证                     │
│          ↓ 编译通过                                     │
│  Step 6: Claude 更新 PROJECT_KNOWLEDGE.md              │
│          ↓                                              │
│  Step 7: 打包发布                                       │
└─────────────────────────────────────────────────────────┘
```

---

## 7. 审核清单模板

```markdown
## Batch N 代码审核

### 文件完整性
- [ ] 所有新建文件已提供
- [ ] 所有修改文件已处理

### 命名空间检查
- [ ] 新增 using 的命名空间存在
- [ ] 删除 using 前已检查依赖（参考第1节映射表）

### 架构合规
- [ ] 分层正确
- [ ] 无循环依赖

### 技术约束
- [ ] 无 .WithOpenApi()
- [ ] SQL 字段名正确
- [ ] 日志级别合理

### 结果
- [ ] ✅ 通过
- [ ] ⚠️ 需修改：[问题列表]
```

---

## 8. 待办事项（技术债）

| 项目 | 优先级 | 状态 |
|------|--------|------|
| 将业务接口移到 Core | 高 | ✅ v36 完成 |
| 添加集成测试 | 高 | ✅ v34 完成 |
| SignalR 分组推送 | 高 | ✅ v34 完成 |
| JWT 认证授权 | 高 | ✅ v35 完成 |
| 审计日志完善（各操作） | 高 | ✅ v37 完成（17个审计点） |
| 角色授权（Admin/Operator/Viewer） | 高 | ✅ v38 完成 |
| Token 刷新机制 | 中 | v39 计划 |
| 补集成测试覆盖率 | 中 | 待定 |
| 压测基线 | 低 | 待定 |

---

**最后更新**: v38
**维护者**: Claude + ChatGPT 协作

---

## v39 更新

### 版本历史（补充）

| 版本 | 主要变更 | Schema |
|------|----------|--------|
| v36-v38 | 接口重构 + 审计完善 + 角色授权 | v4 |
| v39 | **Token 刷新机制** | **v5** |

### v39 新增接口

| 接口/方法 | 命名空间 | 说明 |
|-----------|----------|------|
| `IUserRepository.SaveRefreshTokenAsync` | Core.Abstractions | 保存 Refresh Token |
| `IUserRepository.GetByRefreshTokenAsync` | Core.Abstractions | 通过 Refresh Token 获取用户 |
| `IUserRepository.ClearRefreshTokenAsync` | Core.Abstractions | 清除 Refresh Token（登出） |

### v39 API 变更

| 端点 | 方法 | 说明 |
|------|------|------|
| `/api/auth/login` | POST | 新增返回 refreshToken、refreshExpiresAt |
| `/api/auth/refresh` | POST | **新增** - 刷新 Token |
| `/api/auth/logout` | POST | **新增** - 登出（需认证） |

### Schema v5 变更

```sql
ALTER TABLE user ADD COLUMN refresh_token TEXT;
ALTER TABLE user ADD COLUMN refresh_token_expires_utc INTEGER;
```

### Token 配置

```json
{
  "Jwt": {
    "AccessTokenMinutes": 15,
    "RefreshTokenDays": 7
  }
}
```

---

**最后更新**: v39
**维护者**: Claude + ChatGPT 协作

---

## v40-v44 更新

### 版本历史（补充）

| 版本 | 主要变更 | Schema |
|------|----------|--------|
| v40 | 用户管理 API + 前端 | v5 |
| v41 | API 兼容性修复（路径/参数/SignalR方法名） | v5 |
| v42 | TypeScript 编译修复 | v5 |
| v43 | SignalR 授权 + JWT 密钥外置 | v5 |
| v44 | 请求限流 + 审计增强 | v5 |

### v43-v44 新增文件

| 文件 | 用途 |
|------|------|
| `Host.Api/Middleware/RateLimitingMiddleware.cs` | 请求限流中间件 |
| `Host.Api/Services/AuditService.cs` | 审计辅助服务 |

### v43 SignalR 授权配置

```csharp
// TelemetryHub.cs
[Authorize]  // v43 新增
public sealed class TelemetryHub : Hub

// Program.cs - SignalR JWT 配置
options.Events = new JwtBearerEvents
{
    OnMessageReceived = context =>
    {
        var accessToken = context.Request.Query["access_token"];
        if (!string.IsNullOrEmpty(accessToken) && 
            context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
        {
            context.Token = accessToken;
        }
        return Task.CompletedTask;
    }
};
```

### v43 JWT 密钥环境变量

```bash
# 优先级: 环境变量 > appsettings.json
export JWT_SECRET_KEY="your-secret-key-at-least-32-chars"
```

### v44 限流配置

```csharp
// Program.cs
app.UseRateLimiting(options =>
{
    options.WindowSeconds = 60;   // 时间窗口
    options.MaxRequests = 100;    // 最大请求数
});
```

### v44 审计服务使用

```csharp
// 注入 AuditService
public async Task<IResult> SomeEndpoint(AuditService auditService)
{
    await auditService.LogAsync(
        AuditActions.DeviceCreate,  // 动作
        "Device",                    // 资源类型
        deviceId,                    // 资源 ID
        "创建设备",                   // 详情
        ct);
}

// 审计动作常量
AuditActions.Login
AuditActions.LoginFailed
AuditActions.Logout
AuditActions.TokenRefresh
AuditActions.DeviceCreate / DeviceUpdate / DeviceDelete
AuditActions.AlarmAck / AlarmClose
// ... 等
```

### 前端 SignalR Token 传递

```typescript
// Dashboard/index.tsx
const connection = new HubConnectionBuilder()
  .withUrl('/hubs/telemetry', {
    accessTokenFactory: async () => {
      if (isTokenExpiringSoon()) {
        await refreshTokenIfNeeded()
      }
      return getToken() || ''
    }
  })
  .build()
```

### 待办事项更新

| 项目 | 优先级 | 状态 |
|------|--------|------|
| Token 刷新机制 | 高 | ✅ v39 完成 |
| 用户管理 | 高 | ✅ v40 完成 |
| API 兼容性修复 | 高 | ✅ v41 完成 |
| SignalR 授权 | 高 | ✅ v43 完成 |
| JWT 密钥外置 | 高 | ✅ v43 完成 |
| 请求限流 | 中 | ✅ v44 完成 |
| 审计日志 IP 记录 | 中 | ✅ v44 完成 |
| LibPlcTag 模拟模式 | 高 | ✅ v55 完成 |
| LibPlcTag 前端支持 | 高 | ✅ v55 完成 |
| 健康评估引擎 | 高 | 🚧 规划中 |
| 故障预测模型 | 高 | 📋 规划中 |

---

## 12. LibPlcTag 协议支持 (v55)

### 模拟模式配置

```json
"Protocols": {
  "LibPlcTag": {
    "Enabled": true,
    "SimulationMode": true,  // 启用模拟模式
    "Plcs": [...]
  }
}
```

### 模拟数据类型

| 标签名关键字 | 模式 | 特征 |
|-------------|------|------|
| `TEMP/CURRENT/SPEED` | 正弦波 | 周期30s |
| `LEVEL/PRESSURE/FLOW` | 随机游走 | 平滑波动 |
| `COUNT/TOTAL/PROD` | 计数器 | 随机递增 |
| `SETPOINT/RAMP` | 锯齿波 | 周期60s |
| CipType=BOOL | 切换 | 5%概率翻转 |

### 前端支持

- **设备管理**：选择 LibPlcTag 协议时显示 PlcType、Path、Slot 字段
- **标签管理**：LibPlcTag 设备显示 CipType 选择，自动映射到 DataType

### 新增文件

| 文件 | 说明 |
|------|------|
| `SimulatedTagReader.cs` | 模拟数据生成器 |
| `LibPlcTagConfigAdapter.cs` | 数据库配置适配器 |

---

**最后更新**: v55
**维护者**: Claude
