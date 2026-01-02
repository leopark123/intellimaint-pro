# IntelliMaint Pro 代码审核指南

> **目的**：记录常见问题模式，指导 ChatGPT 代码生成和 Claude 审核流程

---

## 1. 协作流程

```
┌─────────────────────────────────────────────────────────────────┐
│                      问题解决工作流                              │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  1. 用户遇到问题（编译错误、运行时错误等）                        │
│     ↓                                                           │
│  2. Claude 分析问题，形成修复方案（含技术约束）                   │
│     ↓                                                           │
│  3. 用户将方案发给 ChatGPT 生成代码                              │
│     ↓                                                           │
│  4. Claude 按审核清单检查代码                                    │
│     ↓                                                           │
│  5. 发现问题则修正，无问题则打包                                 │
│     ↓                                                           │
│  6. 用户本地测试验证                                             │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## 2. v35 问题案例分析

### 案例 1：IDbExecutor 方法签名不匹配

**错误信息**：
```
error CS1061: "IDbExecutor"未包含"QuerySingleOrDefaultAsync"的定义
error CS1061: "IDbExecutor"未包含"ExecuteAsync"的定义
```

**根本原因**：ChatGPT 假设 IDbExecutor 有标准 Dapper 风格的方法

**实际接口**：
```csharp
// ✅ 正确方法
Task<int> ExecuteNonQueryAsync(string sql, object? parameters = null, CancellationToken ct = default);
Task<T?> ExecuteScalarAsync<T>(string sql, object? parameters = null, CancellationToken ct = default);
Task<List<T>> QueryAsync<T>(string sql, Func<SqliteDataReader, T> mapper, object? parameters = null, CancellationToken ct = default);
Task<T?> QuerySingleAsync<T>(string sql, Func<SqliteDataReader, T> mapper, object? parameters = null, CancellationToken ct = default);

// ❌ 不存在的方法
QuerySingleOrDefaultAsync  // 用 QuerySingleAsync 替代
ExecuteAsync               // 用 ExecuteNonQueryAsync 替代
```

**参数传递方式**：
```csharp
// ✅ 正确：匿名对象
await _db.QueryAsync(sql, mapper, new { Username = username }, ct);

// ❌ 错误：Action<SqliteCommand> 风格
await _db.QueryAsync(sql, mapper, cmd => cmd.Parameters.AddWithValue("@Username", username), ct);
```

**审核要点**：
- [ ] 检查所有 IDbExecutor 调用是否使用正确方法名
- [ ] 检查参数是否使用匿名对象传递

---

### 案例 2：IAuditLogRepository 方法签名不匹配

**错误信息**：
```
error CS1061: "IAuditLogRepository"未包含"AddAsync"的定义
```

**根本原因**：ChatGPT 假设有简化的 AddAsync 方法

**实际接口**：
```csharp
// ✅ 正确方法
Task<long> CreateAsync(AuditLogEntry entry, CancellationToken ct);

// ❌ 不存在的方法
AddAsync(string resourceType, string action, string status, string details, CancellationToken ct);
```

**正确用法**：
```csharp
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

**审核要点**：
- [ ] 检查 IAuditLogRepository 调用是否使用 CreateAsync
- [ ] 检查是否传递完整的 AuditLogEntry 对象

---

### 案例 3：数据库迁移未完整执行

**错误信息**：
```
SQLite Error 1: 'no such table: user'
```

**根本原因**：SchemaManager.InitializeAsync 在全新数据库时只执行 v1 Schema，未继续迁移到 v4

**错误代码**：
```csharp
if (version == 0)
{
    await ApplySchemaV1Async(conn, ct);  // 执行后就结束了
}
else if (version < CurrentVersion)  // 不会进入这个分支
{
    await MigrateAsync(conn, version, ct);
}
```

**正确代码**：
```csharp
if (version == 0)
{
    await ApplySchemaV1Async(conn, ct);
    version = 1;  // 关键：标记为 v1，继续执行后续迁移
}

if (version < CurrentVersion)  // 现在会进入这个分支
{
    await MigrateAsync(conn, version, ct);
}
```

**审核要点**：
- [ ] 新增数据库表时，检查迁移逻辑是否完整
- [ ] 全新数据库是否能正确创建所有表

---

### 案例 4：密码哈希错误

**错误信息**：
```
Login failed for user: admin (401)
```

**根本原因**：ChatGPT 生成的密码哈希值不正确

**验证方法**：
```python
# Python 验证
import hashlib, base64
print(base64.b64encode(hashlib.sha256(b'admin123').digest()).decode())
# 输出: JAvlGPq9JyTdtvBO6x2llnRI1+gxwIyPqCKAn3THIKk=
```

**正确值**：
| 密码 | SHA256 + Base64 |
|------|-----------------|
| admin123 | `JAvlGPq9JyTdtvBO6x2llnRI1+gxwIyPqCKAn3THIKk=` |

**审核要点**：
- [ ] 涉及密码哈希时，验证算法和编码是否正确
- [ ] 使用工具独立验证哈希值

---

## 3. ChatGPT 指令模板

### 修复编译错误模板

```markdown
## 问题描述

编译错误：
[粘贴完整错误信息]

## 项目约束

### IDbExecutor 接口（必须遵守）
```csharp
// 可用方法
Task<int> ExecuteNonQueryAsync(string sql, object? parameters = null, CancellationToken ct = default);
Task<T?> ExecuteScalarAsync<T>(string sql, object? parameters = null, CancellationToken ct = default);
Task<List<T>> QueryAsync<T>(string sql, Func<SqliteDataReader, T> mapper, object? parameters = null, CancellationToken ct = default);
Task<T?> QuerySingleAsync<T>(string sql, Func<SqliteDataReader, T> mapper, object? parameters = null, CancellationToken ct = default);

// 参数传递：使用匿名对象
new { Key = value }
```

### IAuditLogRepository 接口（必须遵守）
```csharp
Task<long> CreateAsync(AuditLogEntry entry, CancellationToken ct);
```

### 其他约束
- 不使用 `.WithOpenApi()`
- 保留所有现有 using 语句
- SQL 字段名使用小写 + 下划线

## 需要修复的文件

[列出文件路径]

## 期望输出

提供完整的修复后代码。
```

---

## 4. Claude 审核清单

### 4.1 编译前检查

```markdown
## 命名空间检查
- [ ] 新增 using 的命名空间是否存在
- [ ] 删除 using 前是否检查了所有依赖类型
- [ ] IAuditLogRepository → using IntelliMaint.Infrastructure.Sqlite
- [ ] IAlarmRuleRepository → using IntelliMaint.Infrastructure.Sqlite
- [ ] ISystemSettingRepository → using IntelliMaint.Infrastructure.Sqlite
- [ ] IUserRepository → using IntelliMaint.Infrastructure.Sqlite

## 方法签名检查
- [ ] IDbExecutor 方法名是否正确
- [ ] IDbExecutor 参数是否使用匿名对象
- [ ] IAuditLogRepository 是否使用 CreateAsync

## 技术约束检查
- [ ] 无 .WithOpenApi()
- [ ] SQL 字段名格式正确（小写 + 下划线）
```

### 4.2 运行时检查

```markdown
## 数据库检查
- [ ] 新表是否在迁移中创建
- [ ] 迁移逻辑是否完整（全新数据库也能正确执行）
- [ ] 默认数据是否正确插入

## 认证检查（如涉及）
- [ ] 密码哈希算法是否正确（SHA256 + Base64）
- [ ] 密码哈希值是否验证过
- [ ] Token 生成和验证是否正确
```

---

## 5. 常见错误速查表

| 错误类型 | 错误信息关键词 | 可能原因 | 解决方向 |
|----------|---------------|----------|----------|
| 方法不存在 | CS1061 未包含定义 | 方法名错误或接口不匹配 | 检查 PROJECT_KNOWLEDGE.md 中的接口定义 |
| 表不存在 | no such table | 迁移未执行 | 检查 SchemaManager 迁移逻辑 |
| 401 错误 | Login failed | 密码哈希错误或用户不存在 | 验证哈希值，检查用户数据 |
| 500 错误 | 看具体异常 | 运行时错误 | 查看后端日志 |
| 命名空间错误 | CS0246 找不到类型 | using 语句缺失或错误 | 检查命名空间映射表 |

---

## 6. 文档维护

每次修复问题后：
1. 更新 PROJECT_KNOWLEDGE.md 的踩坑记录
2. 如发现新的通用模式，添加到本文档
3. 更新审核清单

**最后更新**：v35
**维护者**：Claude + User 协作
