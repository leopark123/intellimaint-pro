# Batch 37: 审计日志完善 - ChatGPT 开发指令

## ⚠️ 重要提示

**本指令要求你输出完整代码，禁止以下行为：**
- ❌ 输出 `// ... existing code ...` 或 `// 其他代码保持不变`
- ❌ 只给出代码片段或"关键部分"
- ❌ 假设不存在的方法或接口
- ❌ 省略 using 语句

**每个文件必须是可直接复制使用的完整代码。**

---

## 1. 任务背景

### 1.1 当前问题

| 问题 | 严重性 | 说明 |
|------|--------|------|
| AuditLogHelper 用户写死 | 🔴 严重 | 始终记录 `UserId="system"`, `UserName="System"`，无法追溯真实操作者 |
| TagEndpoints 无审计 | 🔴 严重 | Create/Update/Delete 操作无记录 |
| AlarmEndpoints 无审计 | 🟡 中等 | Create/Ack/Close 操作无记录 |
| AlarmRuleEndpoints 无审计 | 🔴 严重 | Create/Update/Delete/Enable/Disable 无记录 |

### 1.2 目标

1. 修复 `AuditLogHelper`：从 JWT Claims 提取真实用户信息
2. 为 `TagEndpoints` 添加完整审计（3 个写操作）
3. 为 `AlarmEndpoints` 添加完整审计（3 个写操作）
4. 为 `AlarmRuleEndpoints` 添加完整审计（5 个写操作）

---

## 2. 技术约束（必须遵守）

### 2.1 已存在的类型（直接使用，禁止重新定义）

```csharp
// 位置: IntelliMaint.Core.Contracts
public sealed record AuditLogEntry
{
    public long Id { get; init; }
    public required long Ts { get; init; }
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public required string Action { get; init; }
    public required string ResourceType { get; init; }
    public string? ResourceId { get; init; }
    public string? Details { get; init; }
    public string? IpAddress { get; init; }
}

// 位置: IntelliMaint.Core.Abstractions
public interface IAuditLogRepository
{
    Task<long> CreateAsync(AuditLogEntry entry, CancellationToken ct);
    Task<PagedResult<AuditLogEntry>> QueryAsync(AuditLogQuery query, CancellationToken ct);
}
```

### 2.2 JWT Claims 结构（已在 JwtService 中设置）

```csharp
// JwtService.GenerateToken 中设置的 Claims:
new Claim(ClaimTypes.NameIdentifier, user.UserId),   // 用户 ID
new Claim(ClaimTypes.Name, user.Username),           // 用户名
new Claim(ClaimTypes.Role, user.Role)                // 角色
```

### 2.3 提取用户信息的正确方式

```csharp
// 从 HttpContext 提取用户信息
var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
var userName = httpContext.User.FindFirstValue(ClaimTypes.Name) ?? "Anonymous";
```

**必须添加的 using：**
```csharp
using System.Security.Claims;
```

### 2.4 禁止事项

| 禁止行为 | 原因 |
|----------|------|
| 使用 `httpContext.User.Identity.Name` | 不可靠，可能为 null |
| 硬编码 `UserId = "system"` | 这正是要修复的 bug |
| 使用不存在的 `IAuditLogRepository.AddAsync` | 正确方法是 `CreateAsync` |
| 省略 `CancellationToken` 参数 | 项目规范要求 |

---

## 3. 文件变更清单

| 文件路径 | 操作 | 变更说明 |
|----------|------|----------|
| `src/Host.Api/Endpoints/AuditLogEndpoints.cs` | 修改 | 修复 AuditLogHelper，从 JWT 提取用户 |
| `src/Host.Api/Endpoints/TagEndpoints.cs` | 修改 | 添加 3 个操作的审计 |
| `src/Host.Api/Endpoints/AlarmEndpoints.cs` | 修改 | 添加 3 个操作的审计 |
| `src/Host.Api/Endpoints/AlarmRuleEndpoints.cs` | 修改 | 添加 5 个操作的审计 |

---

## 4. 详细实现要求

### 4.1 修改 AuditLogHelper（在 AuditLogEndpoints.cs 底部）

**当前错误代码：**
```csharp
public static class AuditLogHelper
{
    public static async Task LogAsync(
        IAuditLogRepository repo,
        HttpContext httpContext,
        string action,
        string resourceType,
        string? resourceId,
        string? details,
        CancellationToken ct)
    {
        var userId = "system";           // ❌ 错误：硬编码
        var userName = "System";         // ❌ 错误：硬编码
        // ...
    }
}
```

**修复后的完整代码：**
```csharp
public static class AuditLogHelper
{
    public static async Task LogAsync(
        IAuditLogRepository repo,
        HttpContext httpContext,
        string action,
        string resourceType,
        string? resourceId,
        string? details,
        CancellationToken ct)
    {
        // 从 JWT Claims 提取真实用户信息
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
        var userName = httpContext.User.FindFirstValue(ClaimTypes.Name) ?? "Anonymous";
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();

        var entry = new AuditLogEntry
        {
            Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UserId = userId,
            UserName = userName,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Details = details,
            IpAddress = ipAddress
        };

        await repo.CreateAsync(entry, ct);
    }
}
```

**必须在 AuditLogEndpoints.cs 顶部添加：**
```csharp
using System.Security.Claims;
```

---

### 4.2 修改 TagEndpoints.cs

**需要修改的方法：**
1. `CreateAsync` - 添加审计
2. `UpdateAsync` - 添加审计
3. `DeleteAsync` - 添加审计

**修改要点：**

1. 在方法参数中添加：
```csharp
[FromServices] IAuditLogRepository auditRepo,
HttpContext httpContext,
```

2. 在写操作成功后添加审计调用：
```csharp
await AuditLogHelper.LogAsync(auditRepo, httpContext, "tag.create", "tag",
    request.TagId, $"Created tag: {request.Name ?? request.TagId}", ct);
```

**审计 Action 命名规范：**
- `tag.create` - 创建标签
- `tag.update` - 更新标签
- `tag.delete` - 删除标签

**完整的 CreateAsync 方法示例（作为参考模板）：**
```csharp
private static async Task<IResult> CreateAsync(
    [FromServices] ITagRepository repo,
    [FromServices] IAuditLogRepository auditRepo,
    [FromServices] IConfigRevisionProvider revisionProvider,
    HttpContext httpContext,
    [FromBody] CreateTagRequest request,
    CancellationToken ct)
{
    if (request is null)
        return Results.BadRequest(new ApiResponse<TagDto> { Success = false, Error = "请求体不能为空" });

    if (string.IsNullOrWhiteSpace(request.TagId))
        return Results.BadRequest(new ApiResponse<TagDto> { Success = false, Error = "TagId 必填" });

    if (string.IsNullOrWhiteSpace(request.DeviceId))
        return Results.BadRequest(new ApiResponse<TagDto> { Success = false, Error = "DeviceId 必填" });

    if (!Enum.IsDefined(typeof(TagValueType), request.DataType))
        return Results.BadRequest(new ApiResponse<TagDto> { Success = false, Error = $"DataType 非法: {request.DataType}" });

    var existing = await repo.GetAsync(request.TagId, ct);
    if (existing is not null)
        return Results.BadRequest(new ApiResponse<TagDto> { Success = false, Error = $"TagId 已存在: {request.TagId}" });

    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    var tag = new TagDto
    {
        TagId = request.TagId,
        DeviceId = request.DeviceId,
        Name = request.Name,
        Description = request.Description,
        Unit = request.Unit,
        DataType = (TagValueType)request.DataType,
        Enabled = request.Enabled,
        Address = request.Address,
        ScanIntervalMs = request.ScanIntervalMs,
        TagGroup = request.TagGroup,
        Metadata = request.Metadata,
        CreatedUtc = now,
        UpdatedUtc = now
    };

    await repo.UpsertAsync(tag, ct);

    Log.Information("Created tag {TagId} for device {DeviceId}", request.TagId, request.DeviceId);
    
    await revisionProvider.IncrementRevisionAsync(ct);
    
    // 审计日志
    await AuditLogHelper.LogAsync(auditRepo, httpContext, "tag.create", "tag",
        request.TagId, $"Created tag: {request.Name ?? request.TagId}", ct);

    var saved = await repo.GetAsync(request.TagId, ct);
    return Results.Ok(new ApiResponse<TagDto> { Data = saved ?? tag });
}
```

---

### 4.3 修改 AlarmEndpoints.cs

**需要修改的方法：**
1. `CreateAsync` - 添加审计
2. `AckAsync` - 添加审计
3. `CloseAsync` - 添加审计

**审计 Action 命名规范：**
- `alarm.create` - 创建告警
- `alarm.ack` - 确认告警
- `alarm.close` - 关闭告警

**修改要点（与 TagEndpoints 相同）：**
1. 方法参数添加 `IAuditLogRepository auditRepo` 和 `HttpContext httpContext`
2. 成功操作后调用 `AuditLogHelper.LogAsync`

**特别注意 AckAsync：**
- AckAlarmRequest 中的 `AckedBy` 字段是用户自行填写的
- 审计日志应使用 JWT 中的真实用户，而非 request.AckedBy
- Details 中可以包含 request.AckedBy 作为记录

```csharp
// AckAsync 中的审计示例
await AuditLogHelper.LogAsync(auditRepo, httpContext, "alarm.ack", "alarm",
    alarmId, $"Alarm acknowledged. Note: {request.AckNote ?? "无"}", ct);
```

**必须在 AlarmEndpoints.cs 顶部添加：**
```csharp
using IntelliMaint.Core.Abstractions;  // 如果尚未存在（为 IAuditLogRepository）
```

---

### 4.4 修改 AlarmRuleEndpoints.cs

**需要修改的方法：**
1. `CreateAsync` - 添加审计
2. `UpdateAsync` - 添加审计
3. `DeleteAsync` - 添加审计
4. `EnableAsync` - 添加审计
5. `DisableAsync` - 添加审计

**审计 Action 命名规范：**
- `alarmrule.create` - 创建规则
- `alarmrule.update` - 更新规则
- `alarmrule.delete` - 删除规则
- `alarmrule.enable` - 启用规则
- `alarmrule.disable` - 禁用规则

**必须在 AlarmRuleEndpoints.cs 顶部添加：**
```csharp
using IntelliMaint.Core.Abstractions;  // 如果尚未存在（为 IAuditLogRepository）
```

---

## 5. 输出要求

### 5.1 你必须提供以下 4 个文件的完整代码

1. **`src/Host.Api/Endpoints/AuditLogEndpoints.cs`** - 完整文件
2. **`src/Host.Api/Endpoints/TagEndpoints.cs`** - 完整文件
3. **`src/Host.Api/Endpoints/AlarmEndpoints.cs`** - 完整文件
4. **`src/Host.Api/Endpoints/AlarmRuleEndpoints.cs`** - 完整文件

### 5.2 每个文件必须满足

- ✅ 包含完整的 using 语句（包括 `System.Security.Claims`）
- ✅ 包含正确的命名空间
- ✅ 包含所有方法的完整实现（不省略任何代码）
- ✅ 所有写操作后都有审计日志调用
- ✅ 审计使用 JWT 中的真实用户信息

### 5.3 禁止输出

- ❌ `// ... 其余代码不变 ...`
- ❌ `// existing code`
- ❌ 只给"关键修改部分"
- ❌ 框架代码

---

## 6. 验证清单

完成后请自检：

| 检查项 | 文件 | 要求 |
|--------|------|------|
| AuditLogHelper 使用 JWT | AuditLogEndpoints.cs | `httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)` |
| TagEndpoints 有审计 | TagEndpoints.cs | Create/Update/Delete 都有 |
| AlarmEndpoints 有审计 | AlarmEndpoints.cs | Create/Ack/Close 都有 |
| AlarmRuleEndpoints 有审计 | AlarmRuleEndpoints.cs | Create/Update/Delete/Enable/Disable 都有 |
| using 完整 | 所有文件 | 包含 `System.Security.Claims` |

---

## 7. 现有代码参考

### 7.1 DeviceEndpoints.cs 审计写法（已正确实现，作为参考）

```csharp
private static async Task<IResult> CreateAsync(
    [FromServices] IDeviceRepository repo,
    [FromServices] IAuditLogRepository auditRepo,
    [FromServices] IConfigRevisionProvider revisionProvider,
    HttpContext httpContext,
    [FromBody] CreateDeviceRequest request,
    CancellationToken ct)
{
    // ... 业务逻辑 ...
    
    await repo.UpsertAsync(device, ct);
    
    Log.Information("Created device {DeviceId}", request.DeviceId);
    
    await revisionProvider.IncrementRevisionAsync(ct);
    
    await AuditLogHelper.LogAsync(auditRepo, httpContext, "device.create", "device", 
        request.DeviceId, $"Created device: {request.Name}", ct);

    // ... 返回结果 ...
}
```

### 7.2 当前 TagEndpoints.cs 需要修改的方法签名

**修改前（当前）：**
```csharp
private static async Task<IResult> CreateAsync(
    [FromServices] ITagRepository repo,
    [FromServices] IConfigRevisionProvider revisionProvider,
    [FromBody] CreateTagRequest request,
    CancellationToken ct)
```

**修改后（目标）：**
```csharp
private static async Task<IResult> CreateAsync(
    [FromServices] ITagRepository repo,
    [FromServices] IAuditLogRepository auditRepo,
    [FromServices] IConfigRevisionProvider revisionProvider,
    HttpContext httpContext,
    [FromBody] CreateTagRequest request,
    CancellationToken ct)
```

---

## 8. 审计日志统计

完成后，系统应覆盖以下所有写操作：

| Endpoint | 操作 | Action 值 |
|----------|------|-----------|
| AuthEndpoints | Login Success | Login |
| AuthEndpoints | Login Failure | Login |
| DeviceEndpoints | Create | device.create |
| DeviceEndpoints | Update | device.update |
| DeviceEndpoints | Delete | device.delete |
| TagEndpoints | Create | tag.create |
| TagEndpoints | Update | tag.update |
| TagEndpoints | Delete | tag.delete |
| AlarmEndpoints | Create | alarm.create |
| AlarmEndpoints | Ack | alarm.ack |
| AlarmEndpoints | Close | alarm.close |
| AlarmRuleEndpoints | Create | alarmrule.create |
| AlarmRuleEndpoints | Update | alarmrule.update |
| AlarmRuleEndpoints | Delete | alarmrule.delete |
| AlarmRuleEndpoints | Enable | alarmrule.enable |
| AlarmRuleEndpoints | Disable | alarmrule.disable |
| SettingsEndpoints | Update | setting.update |
| SettingsEndpoints | Cleanup | data.cleanup |

**共计 17 个审计点。**

---

## 9. 完成后执行

```bash
# 编译验证
cd intellimaint-pro
dotnet build

# 应无编译错误
```

---

## 10. 最终提醒

**你必须提供 4 个文件的完整代码。**

如果你的输出包含 `// ...` 或 `// existing code` 或任何省略标记，则视为任务失败。

每个文件从第一行 `using` 到最后一个 `}` 都必须完整。
