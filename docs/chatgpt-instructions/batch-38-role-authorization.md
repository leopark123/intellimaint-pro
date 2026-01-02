# Batch 38: 角色授权 - ChatGPT 开发指令

## 项目背景

**IntelliMaint Pro** 工业数据采集平台，v37 已完成审计日志完善。

### 当前状态
- JWT 认证 ✅ 已实现
- 角色定义 ✅ 已有 (Admin/Operator/Viewer)
- Token 包含角色 ✅ `ClaimTypes.Role`
- 授权策略 ❌ **未配置**（所有端点只检查登录，不检查角色）

### 本批次目标
实现基于角色的细粒度授权控制。

---

## 权限矩阵（必须严格遵守）

```
┌─────────────────────────────────────────────────────────────────────┐
│                         权限矩阵                                     │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  角色层级: Admin > Operator > Viewer                                │
│                                                                     │
│  ┌────────────────────┬─────────┬──────────┬────────┐              │
│  │ 功能               │ Admin   │ Operator │ Viewer │              │
│  ├────────────────────┼─────────┼──────────┼────────┤              │
│  │ 登录               │ ✅      │ ✅       │ ✅     │              │
│  │ 查看遥测数据       │ ✅      │ ✅       │ ✅     │              │
│  │ 查看健康状态       │ ✅      │ ✅       │ ✅     │              │
│  │ 导出数据           │ ✅      │ ✅       │ ✅     │              │
│  │ 查看设备/标签列表   │ ✅      │ ✅       │ ✅     │              │
│  │ 查看告警列表       │ ✅      │ ✅       │ ✅     │              │
│  │ 查看告警规则列表   │ ✅      │ ✅       │ ✅     │              │
│  │ 查看系统设置       │ ✅      │ ✅       │ ✅     │              │
│  ├────────────────────┼─────────┼──────────┼────────┤              │
│  │ 确认/关闭告警      │ ✅      │ ✅       │ ❌     │              │
│  │ 创建告警           │ ✅      │ ✅       │ ❌     │              │
│  │ 查看审计日志       │ ✅      │ ✅       │ ❌     │              │
│  ├────────────────────┼─────────┼──────────┼────────┤              │
│  │ 设备 增删改        │ ✅      │ ❌       │ ❌     │              │
│  │ 标签 增删改        │ ✅      │ ❌       │ ❌     │              │
│  │ 告警规则 增删改    │ ✅      │ ❌       │ ❌     │              │
│  │ 系统设置 修改      │ ✅      │ ❌       │ ❌     │              │
│  │ 数据清理           │ ✅      │ ❌       │ ❌     │              │
│  └────────────────────┴─────────┴──────────┴────────┘              │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 技术约束（必须遵守）

### 1. 已存在的角色常量（直接使用，不要重新定义）

```csharp
// 位置: IntelliMaint.Core.Contracts.Auth
public static class UserRoles
{
    public const string Admin = "Admin";
    public const string Operator = "Operator";
    public const string Viewer = "Viewer";
}
```

### 2. JWT 已包含角色 Claim

```csharp
// JwtService.GenerateToken 已添加
new Claim(ClaimTypes.Role, user.Role)
```

### 3. 授权策略命名规范

| 策略名 | 允许的角色 | 用途 |
|--------|-----------|------|
| `AdminOnly` | Admin | 配置管理（设备/标签/规则/设置） |
| `OperatorOrAbove` | Admin, Operator | 业务操作（告警确认/审计查看） |
| `AllAuthenticated` | Admin, Operator, Viewer | 数据读取（默认） |

### 4. 不要修改的内容

- `UserRoles` 常量定义
- `JwtService` 实现
- `UserRepository` 实现
- 任何 Repository 层代码

---

## 需要修改的文件

### 文件 1: `src/Host.Api/Program.cs`

在 `builder.Services.AddAuthorization();` 处添加策略配置：

```csharp
// 修改前
builder.Services.AddAuthorization();

// 修改后
builder.Services.AddAuthorization(options =>
{
    // AdminOnly: 只有 Admin 可访问
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole(UserRoles.Admin));

    // OperatorOrAbove: Admin 或 Operator 可访问
    options.AddPolicy("OperatorOrAbove", policy =>
        policy.RequireRole(UserRoles.Admin, UserRoles.Operator));

    // AllAuthenticated: 所有已认证用户（默认策略）
    options.AddPolicy("AllAuthenticated", policy =>
        policy.RequireAuthenticatedUser());

    // 设置默认策略为 AllAuthenticated
    options.DefaultPolicy = options.GetPolicy("AllAuthenticated")!;
});
```

**注意**：需要添加 using：
```csharp
using IntelliMaint.Core.Contracts;
```

---

### 文件 2: `src/Host.Api/Endpoints/DeviceEndpoints.cs`

修改授权配置，读操作允许所有用户，写操作仅 Admin：

```csharp
public static void MapDeviceEndpoints(this IEndpointRouteBuilder app)
{
    var group = app.MapGroup("/api/devices")
        .WithTags("Device");

    // 读操作 - 所有已认证用户
    group.MapGet("/", ListAsync).RequireAuthorization("AllAuthenticated");
    group.MapGet("/{deviceId}", GetAsync).RequireAuthorization("AllAuthenticated");

    // 写操作 - 仅 Admin
    group.MapPost("/", CreateAsync).RequireAuthorization("AdminOnly");
    group.MapPut("/{deviceId}", UpdateAsync).RequireAuthorization("AdminOnly");
    group.MapDelete("/{deviceId}", DeleteAsync).RequireAuthorization("AdminOnly");
    group.MapPost("/{deviceId}/test", TestConnectionAsync).RequireAuthorization("AdminOnly");
}
```

---

### 文件 3: `src/Host.Api/Endpoints/TagEndpoints.cs`

同样模式：读操作所有用户，写操作仅 Admin：

```csharp
public static void MapTagEndpoints(this IEndpointRouteBuilder app)
{
    var group = app.MapGroup("/api/tags")
        .WithTags("Tag");

    // 读操作 - 所有已认证用户
    group.MapGet("/", ListAsync).RequireAuthorization("AllAuthenticated");
    group.MapGet("/{tagId}", GetAsync).RequireAuthorization("AllAuthenticated");

    // 写操作 - 仅 Admin
    group.MapPost("/", CreateAsync).RequireAuthorization("AdminOnly");
    group.MapPut("/{tagId}", UpdateAsync).RequireAuthorization("AdminOnly");
    group.MapDelete("/{tagId}", DeleteAsync).RequireAuthorization("AdminOnly");
}
```

---

### 文件 4: `src/Host.Api/Endpoints/AlarmEndpoints.cs`

读操作所有用户，业务操作（创建/确认/关闭）Operator 及以上：

```csharp
public static void MapAlarmEndpoints(this IEndpointRouteBuilder app)
{
    var group = app.MapGroup("/api/alarms")
        .WithTags("Alarm");

    // 读操作 - 所有已认证用户
    group.MapGet("/", ListAsync).RequireAuthorization("AllAuthenticated");
    group.MapGet("/active", GetActiveAsync).RequireAuthorization("AllAuthenticated");
    group.MapGet("/{alarmId}", GetAsync).RequireAuthorization("AllAuthenticated");

    // 业务操作 - Operator 及以上
    group.MapPost("/", CreateAsync).RequireAuthorization("OperatorOrAbove");
    group.MapPost("/{alarmId}/ack", AckAsync).RequireAuthorization("OperatorOrAbove");
    group.MapPost("/{alarmId}/close", CloseAsync).RequireAuthorization("OperatorOrAbove");
}
```

---

### 文件 5: `src/Host.Api/Endpoints/AlarmRuleEndpoints.cs`

读操作所有用户，写操作仅 Admin：

```csharp
public static void MapAlarmRuleEndpoints(this IEndpointRouteBuilder app)
{
    var group = app.MapGroup("/api/alarm-rules")
        .WithTags("AlarmRule");

    // 读操作 - 所有已认证用户
    group.MapGet("/", ListAsync).RequireAuthorization("AllAuthenticated");
    group.MapGet("/{ruleId}", GetAsync).RequireAuthorization("AllAuthenticated");

    // 写操作 - 仅 Admin
    group.MapPost("/", CreateAsync).RequireAuthorization("AdminOnly");
    group.MapPut("/{ruleId}", UpdateAsync).RequireAuthorization("AdminOnly");
    group.MapDelete("/{ruleId}", DeleteAsync).RequireAuthorization("AdminOnly");
    group.MapPost("/{ruleId}/enable", EnableAsync).RequireAuthorization("AdminOnly");
    group.MapPost("/{ruleId}/disable", DisableAsync).RequireAuthorization("AdminOnly");
}
```

---

### 文件 6: `src/Host.Api/Endpoints/SettingsEndpoints.cs`

读操作所有用户，写操作仅 Admin：

```csharp
public static void MapSettingsEndpoints(this IEndpointRouteBuilder app)
{
    var group = app.MapGroup("/api/settings")
        .WithTags("Settings");

    // 读操作 - 所有已认证用户
    group.MapGet("/", ListAsync).RequireAuthorization("AllAuthenticated");
    group.MapGet("/{key}", GetAsync).RequireAuthorization("AllAuthenticated");

    // 写操作 - 仅 Admin
    group.MapPut("/{key}", SetAsync).RequireAuthorization("AdminOnly");
    group.MapDelete("/{key}", DeleteAsync).RequireAuthorization("AdminOnly");
    group.MapPost("/data/cleanup", CleanupDataAsync).RequireAuthorization("AdminOnly");
}
```

---

### 文件 7: `src/Host.Api/Endpoints/AuditLogEndpoints.cs`

审计日志查看 - Operator 及以上：

```csharp
public static void MapAuditLogEndpoints(this IEndpointRouteBuilder app)
{
    var group = app.MapGroup("/api/audit-logs")
        .WithTags("AuditLog")
        .RequireAuthorization("OperatorOrAbove");  // 整个组都需要 Operator 权限

    group.MapGet("/", QueryAsync);
    group.MapGet("/actions", GetActionsAsync);
    group.MapGet("/resource-types", GetResourceTypesAsync);
}
```

---

### 文件 8: `src/Host.Api/Endpoints/TelemetryEndpoints.cs`

数据读取 - 所有已认证用户：

```csharp
public static void MapTelemetryEndpoints(this IEndpointRouteBuilder app)
{
    var group = app.MapGroup("/api/telemetry")
        .WithTags("Telemetry")
        .RequireAuthorization("AllAuthenticated");  // 整个组都允许已认证用户

    group.MapGet("/query", QueryAsync);
    group.MapGet("/latest", GetLatestAsync);
    group.MapGet("/tags", GetTagsAsync);
    group.MapGet("/aggregate", AggregateAsync);
}
```

---

### 文件 9: `src/Host.Api/Endpoints/HealthEndpoints.cs`

健康状态读取 - 所有已认证用户：

```csharp
public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
{
    var group = app.MapGroup("/api/health")
        .WithTags("Health")
        .RequireAuthorization("AllAuthenticated");  // 整个组都允许已认证用户

    group.MapGet("/status", GetStatusAsync);
    group.MapGet("/collectors", GetCollectorsAsync);
    group.MapGet("/pipeline", GetPipelineAsync);
    group.MapGet("/database", GetDatabaseAsync);
    group.MapGet("/history", GetHistoryAsync);
}
```

---

### 文件 10: `src/Host.Api/Endpoints/ExportEndpoints.cs`

数据导出 - 所有已认证用户：

```csharp
public static void MapExportEndpoints(this IEndpointRouteBuilder app)
{
    var group = app.MapGroup("/api/export")
        .WithTags("Export")
        .RequireAuthorization("AllAuthenticated");  // 整个组都允许已认证用户

    group.MapPost("/csv", ExportCsvAsync);
    group.MapPost("/excel", ExportExcelAsync);
    group.MapGet("/status/{jobId}", GetStatusAsync);
    group.MapGet("/download/{jobId}", DownloadAsync);
}
```

---

### 文件 11: `src/Host.Api/Endpoints/AuthEndpoints.cs`

**无需修改**，保持 `AllowAnonymous()`。

---

## 代码要求

### 必须遵守

1. **保留所有现有代码逻辑**，只修改授权配置
2. **保留所有现有 using 语句**
3. **Program.cs 需要添加** `using IntelliMaint.Core.Contracts;`
4. **不要修改方法实现**，只修改路由注册部分
5. **每个端点方法必须有明确的授权策略**

### 禁止事项

1. ❌ 不要删除任何现有代码
2. ❌ 不要修改方法签名
3. ❌ 不要重新定义 UserRoles
4. ❌ 不要使用 `[Authorize]` 属性（使用 Minimal API 的 `.RequireAuthorization()`）
5. ❌ 不要输出 `// ... 其余代码不变`

---

## 输出要求

请提供以下文件的**完整代码**（不是片段）：

1. **`src/Host.Api/Program.cs`** - 完整文件
2. **`src/Host.Api/Endpoints/DeviceEndpoints.cs`** - 完整文件
3. **`src/Host.Api/Endpoints/TagEndpoints.cs`** - 完整文件
4. **`src/Host.Api/Endpoints/AlarmEndpoints.cs`** - 完整文件
5. **`src/Host.Api/Endpoints/AlarmRuleEndpoints.cs`** - 完整文件
6. **`src/Host.Api/Endpoints/SettingsEndpoints.cs`** - 完整文件
7. **`src/Host.Api/Endpoints/AuditLogEndpoints.cs`** - 完整文件
8. **`src/Host.Api/Endpoints/TelemetryEndpoints.cs`** - 完整文件
9. **`src/Host.Api/Endpoints/HealthEndpoints.cs`** - 完整文件
10. **`src/Host.Api/Endpoints/ExportEndpoints.cs`** - 完整文件

每个文件必须：
- 包含完整的 using 语句
- 包含完整的命名空间声明
- 包含所有方法的完整实现
- 可直接复制使用

---

## 验证清单

完成后验证：

1. **编译检查**
   ```bash
   dotnet build
   ```

2. **策略配置检查**
   - [ ] Program.cs 包含 3 个授权策略定义
   - [ ] 使用了 `using IntelliMaint.Core.Contracts;`

3. **端点授权检查**
   - [ ] Device: GET 用 AllAuthenticated，POST/PUT/DELETE 用 AdminOnly
   - [ ] Tag: GET 用 AllAuthenticated，POST/PUT/DELETE 用 AdminOnly
   - [ ] Alarm: GET 用 AllAuthenticated，POST/ack/close 用 OperatorOrAbove
   - [ ] AlarmRule: GET 用 AllAuthenticated，其他用 AdminOnly
   - [ ] Settings: GET 用 AllAuthenticated，PUT/DELETE/cleanup 用 AdminOnly
   - [ ] AuditLog: 全部用 OperatorOrAbove
   - [ ] Telemetry/Health/Export: 全部用 AllAuthenticated
   - [ ] Auth: 保持 AllowAnonymous

4. **运行时测试**
   - Viewer 登录后能查看数据，不能创建设备
   - Operator 登录后能确认告警，不能创建设备
   - Admin 登录后能执行所有操作

---

## 总结

本批次只做一件事：**给已有端点添加角色授权策略**。

不修改任何业务逻辑，只修改授权配置。
