# IntelliMaint Pro 变更日志

## v48 (2025-12-31) - 安全增强 + 性能优化 + 代码质量

### P0 安全修复

#### 1. 密码哈希升级 (BCrypt)

**修改前**: SHA256 无盐值哈希
```csharp
// 旧实现 - 容易被彩虹表攻击
var hash = SHA256.ComputeHash(Encoding.UTF8.GetBytes(password));
```

**修改后**: BCrypt 加盐哈希
```csharp
// 新实现 - 自动加盐，抗暴力破解
return BCrypt.Net.BCrypt.HashPassword(password, BCrypt.Net.BCrypt.GenerateSalt(12));
```

**兼容性**: 
- 旧用户首次登录时自动升级密码哈希格式
- 无需重置现有用户密码

#### 2. 账号锁定机制

| 配置 | 值 |
|------|-----|
| 最大失败次数 | 5 次 |
| 锁定时长 | 15 分钟 |

**新增字段 (user 表)**:
```sql
ALTER TABLE user ADD COLUMN failed_login_count INTEGER NOT NULL DEFAULT 0;
ALTER TABLE user ADD COLUMN lockout_until_utc INTEGER;
```

### P1 代码优化

#### 3. 授权策略常量化

**新增常量类**:
```csharp
public static class AuthPolicies
{
    public const string AdminOnly = "AdminOnly";
    public const string OperatorOrAbove = "OperatorOrAbove";
    public const string AllAuthenticated = "AllAuthenticated";
}
```

#### 4. 遥测数据游标分页

**响应格式**:
```json
{
  "success": true,
  "data": [...],
  "count": 1000,
  "hasMore": true,
  "nextCursor": "1704067199000:3"
}
```

#### 5. 单元测试补充

- `SecurityTests.cs` - JWT、密码哈希、授权策略测试
- `TelemetryApiTests.cs` - 遥测 API 模型测试

### P2 架构改进

#### 6. 缓存服务 (CacheService)

封装 IMemoryCache 提供类型安全的缓存操作：

```csharp
// 使用示例
var devices = await cache.GetOrCreateAsync(
    CacheService.Keys.DeviceList,
    () => repo.ListAsync(ct),
    TimeSpan.FromMinutes(2));

// 创建/更新/删除时使缓存失效
cache.InvalidateDevice(deviceId);
```

**缓存键**:
- `devices:all` - 设备列表（2分钟）
- `device:{id}` - 单个设备（5分钟）
- `tags:all` - 标签列表
- `settings:all` - 系统设置

#### 7. 全局异常处理中间件

统一处理未捕获异常，返回一致的错误响应格式：

```json
{
  "success": false,
  "error": "资源不存在",
  "errorCode": "NOT_FOUND",
  "timestamp": 1735689600000,
  "details": null  // 仅开发环境显示
}
```

**错误代码映射**:
| 异常类型 | HTTP 状态码 | 错误代码 |
|---------|-------------|----------|
| ArgumentException | 400 | INVALID_ARGUMENT |
| KeyNotFoundException | 404 | NOT_FOUND |
| UnauthorizedAccessException | 403 | FORBIDDEN |
| 其他 | 500 | INTERNAL_ERROR |

#### 8. 前端错误处理增强

新增 `useErrorHandler` Hook：
```typescript
const { handleError, handleSuccess } = useErrorHandler()

try {
  await api.doSomething()
  handleSuccess('操作成功')
} catch (error) {
  handleError(error)  // 自动显示错误提示
}
```

### 修改文件清单

| 文件 | 修改内容 |
|------|----------|
| `Infrastructure.Sqlite.csproj` | 添加 BCrypt.Net-Next 包 |
| `UserRepository.cs` | BCrypt 哈希 + 锁定逻辑 |
| `Repositories.cs` | 添加锁定检查方法 + 游标分页接口 |
| `AuthEndpoints.cs` | 添加锁定检查 + 友好错误提示 |
| `SchemaManager.cs` | v9 迁移（锁定字段） |
| `Auth.cs` | 添加 AuthPolicies 常量 |
| `Program.cs` | 缓存 + 异常中间件 |
| `CacheService.cs` | 新增缓存服务 |
| `GlobalExceptionMiddleware.cs` | 新增异常处理 |
| `DeviceEndpoints.cs` | 添加缓存支持 |
| `TelemetryModels.cs` | 游标分页模型 |
| `TelemetryRepository.cs` | 游标分页实现 |
| `TelemetryEndpoints.cs` | 分页响应 |
| `client.ts` | 增强错误处理 |
| `useErrorHandler.ts` | 新增错误处理 Hook |
| `Tests/Unit/*.cs` | 新增测试 |

### 数据库 Schema 变更

**版本**: v8 → v9

```sql
ALTER TABLE user ADD COLUMN failed_login_count INTEGER NOT NULL DEFAULT 0;
ALTER TABLE user ADD COLUMN lockout_until_utc INTEGER;
```

### 新增 NuGet 包

| 包名 | 版本 | 用途 |
|------|------|------|
| BCrypt.Net-Next | 4.0.3 | 密码哈希 |

### 迁移说明

1. **更新 NuGet 包**:
   ```bash
   dotnet restore
   ```

2. **数据库自动迁移**: 启动时自动执行 v9 迁移

3. **现有用户密码**: 首次登录时自动升级为 BCrypt 格式

### 测试验证

```bash
# 运行单元测试
dotnet test tests/Unit

# 测试账号锁定
for i in {1..6}; do
  curl -X POST http://localhost:5000/api/auth/login \
    -H "Content-Type: application/json" \
    -d '{"username":"admin","password":"wrong"}'
  echo ""
done

# 测试游标分页
curl "http://localhost:5000/api/telemetry/query?deviceId=device-1&limit=100" \
  -H "Authorization: Bearer <token>"
```

---

**影响范围**: 认证系统、遥测查询、设备管理、错误处理  
**风险等级**: 低（向后兼容）  
**需要重启**: 是
