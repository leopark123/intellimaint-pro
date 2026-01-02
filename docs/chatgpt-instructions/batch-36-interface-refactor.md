# Batch 36: 接口位置重构 - ChatGPT 开发指令

## 项目背景

**IntelliMaint Pro** 工业数据采集平台正在进行技术债清理。

### 当前问题

4 个业务接口定义在 `Infrastructure.Sqlite` 层，违反依赖倒置原则：
- `IAlarmRuleRepository`
- `IAuditLogRepository` + `AuditLogQuery`
- `ISystemSettingRepository`
- `IUserRepository`

### 目标

将这些接口移动到 `Core.Abstractions` 层，使 Host 层不再直接依赖 Infrastructure.Sqlite。

---

## 技术约束（必须遵守）

### 1. 项目架构

```
Core (无外部依赖)
  ├── Abstractions/   ← 接口定义
  └── Contracts/      ← DTO、枚举、查询类

Infrastructure.Sqlite (依赖 Core)
  └── *Repository.cs  ← 接口实现

Host.Api (依赖 Core + Infrastructure)
  └── Endpoints/      ← 使用接口
```

### 2. 命名规范

- 命名空间: `IntelliMaint.Core.Abstractions` (接口)
- 命名空间: `IntelliMaint.Core.Contracts` (DTO/Query)
- 文件名: PascalCase

### 3. 已存在的类型（不要重复定义）

以下类型已在 `Core.Contracts` 中定义，接口可以直接使用：
- `AlarmRule`
- `AuditLogEntry`
- `SystemSetting`
- `UserDto`

---

## 任务清单

### 任务 1: 修改 `src/Core/Abstractions/Repositories.cs`

在文件末尾添加 4 个接口定义：

```csharp
// ========================================
// 以下接口从 Infrastructure.Sqlite 迁移
// ========================================

/// <summary>
/// 告警规则仓储接口
/// </summary>
public interface IAlarmRuleRepository
{
    Task<IReadOnlyList<AlarmRule>> ListAsync(CancellationToken ct);
    Task<IReadOnlyList<AlarmRule>> ListEnabledAsync(CancellationToken ct);
    Task<AlarmRule?> GetAsync(string ruleId, CancellationToken ct);
    Task UpsertAsync(AlarmRule rule, CancellationToken ct);
    Task DeleteAsync(string ruleId, CancellationToken ct);
    Task SetEnabledAsync(string ruleId, bool enabled, CancellationToken ct);
}

/// <summary>
/// 审计日志仓储接口
/// </summary>
public interface IAuditLogRepository
{
    Task<long> CreateAsync(AuditLogEntry entry, CancellationToken ct);
    Task<(IReadOnlyList<AuditLogEntry> Items, int TotalCount)> QueryAsync(AuditLogQuery query, CancellationToken ct);
    Task<IReadOnlyList<string>> GetDistinctActionsAsync(CancellationToken ct);
    Task<IReadOnlyList<string>> GetDistinctResourceTypesAsync(CancellationToken ct);
}

/// <summary>
/// 系统设置仓储接口
/// </summary>
public interface ISystemSettingRepository
{
    Task<IReadOnlyList<SystemSetting>> GetAllAsync(CancellationToken ct);
    Task<string?> GetAsync(string key, CancellationToken ct);
    Task SetAsync(string key, string value, CancellationToken ct);
}

/// <summary>
/// 用户仓储接口
/// </summary>
public interface IUserRepository
{
    Task<UserDto?> GetByUsernameAsync(string username, CancellationToken ct);
    Task<UserDto?> ValidateCredentialsAsync(string username, string password, CancellationToken ct);
    Task<UserDto?> CreateAsync(string username, string password, string role, string? displayName, CancellationToken ct);
    Task UpdateLastLoginAsync(string userId, CancellationToken ct);
    Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken ct);
}
```

---

### 任务 2: 创建 `src/Core/Contracts/Queries.cs`

新建文件，添加 `AuditLogQuery` 类：

```csharp
namespace IntelliMaint.Core.Contracts;

/// <summary>
/// 审计日志查询参数
/// </summary>
public sealed record AuditLogQuery
{
    public string? Action { get; init; }
    public string? ResourceType { get; init; }
    public string? ResourceId { get; init; }
    public string? UserId { get; init; }
    public long? StartTs { get; init; }
    public long? EndTs { get; init; }
    public int Limit { get; init; } = 50;
    public int Offset { get; init; } = 0;
}
```

---

### 任务 3: 修改 `src/Infrastructure/Sqlite/AlarmRuleRepository.cs`

**删除**文件开头的接口定义（第 10-18 行），只保留实现类。

修改后文件开头应该是：

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IntelliMaint.Core.Abstractions;  // ← 新增
using IntelliMaint.Core.Contracts;
using Microsoft.Data.Sqlite;

namespace IntelliMaint.Infrastructure.Sqlite;

public sealed class AlarmRuleRepository : IAlarmRuleRepository
{
    // ... 保持实现代码不变
}
```

---

### 任务 4: 修改 `src/Infrastructure/Sqlite/AuditLogRepository.cs`

**删除**文件中的 `IAuditLogRepository` 接口定义（第 11-17 行）和 `AuditLogQuery` 类定义（第 19-29 行），只保留实现类。

修改后文件开头应该是：

```csharp
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelliMaint.Core.Abstractions;  // ← 新增
using IntelliMaint.Core.Contracts;
using Microsoft.Data.Sqlite;

namespace IntelliMaint.Infrastructure.Sqlite;

public sealed class AuditLogRepository : IAuditLogRepository
{
    // ... 保持实现代码不变
}
```

---

### 任务 5: 修改 `src/Infrastructure/Sqlite/SystemSettingRepository.cs`

**删除**文件中的 `ISystemSettingRepository` 接口定义（第 10-15 行），只保留实现类。

修改后文件开头应该是：

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IntelliMaint.Core.Abstractions;  // ← 新增
using IntelliMaint.Core.Contracts;
using Microsoft.Data.Sqlite;

namespace IntelliMaint.Infrastructure.Sqlite;

public sealed class SystemSettingRepository : ISystemSettingRepository
{
    // ... 保持实现代码不变
}
```

---

### 任务 6: 修改 `src/Infrastructure/Sqlite/UserRepository.cs`

**删除**文件中的 `IUserRepository` 接口定义（第 11-18 行），只保留实现类。

修改后文件开头应该是：

```csharp
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelliMaint.Core.Abstractions;  // ← 新增
using IntelliMaint.Core.Contracts;

namespace IntelliMaint.Infrastructure.Sqlite;

public sealed class UserRepository : IUserRepository
{
    // ... 保持实现代码不变
}
```

---

### 任务 7: 修改 Host.Api 的 Endpoints

以下文件需要更新 using 语句：

#### 7.1 `src/Host.Api/Endpoints/DeviceEndpoints.cs`

**添加**（如果没有）：
```csharp
using IntelliMaint.Core.Abstractions;
```

**删除**（如果存在）：
```csharp
using IntelliMaint.Infrastructure.Sqlite;
```

#### 7.2 `src/Host.Api/Endpoints/SettingsEndpoints.cs`

同上。

#### 7.3 `src/Host.Api/Endpoints/AuthEndpoints.cs`

同上。

#### 7.4 `src/Host.Api/Endpoints/AuditLogEndpoints.cs`

同上，还需要确保 `AuditLogQuery` 使用 `IntelliMaint.Core.Contracts` 命名空间。

#### 7.5 `src/Host.Api/Endpoints/AlarmRuleEndpoints.cs`

同上。

---

### 任务 8: 修改 Infrastructure.Pipeline

以下文件需要更新 using 语句：

#### 8.1 `src/Infrastructure/Pipeline/PipelineServiceExtensions.cs`

**添加**（如果没有）：
```csharp
using IntelliMaint.Core.Abstractions;
```

**保留**（仍然需要）：
```csharp
using IntelliMaint.Infrastructure.Sqlite;  // 仍需要，因为要注册实现类
```

#### 8.2 `src/Infrastructure/Pipeline/AlarmEvaluatorService.cs`

**添加**（如果没有）：
```csharp
using IntelliMaint.Core.Abstractions;
```

**删除**（如果存在）：
```csharp
using IntelliMaint.Infrastructure.Sqlite;
```

---

## 输出要求

请提供以下文件的完整代码：

1. **`src/Core/Abstractions/Repositories.cs`** - 完整文件（包含新增的 4 个接口）
2. **`src/Core/Contracts/Queries.cs`** - 新文件（AuditLogQuery）
3. **`src/Infrastructure/Sqlite/AlarmRuleRepository.cs`** - 完整文件（移除接口定义）
4. **`src/Infrastructure/Sqlite/AuditLogRepository.cs`** - 完整文件（移除接口和Query定义）
5. **`src/Infrastructure/Sqlite/SystemSettingRepository.cs`** - 完整文件（移除接口定义）
6. **`src/Infrastructure/Sqlite/UserRepository.cs`** - 完整文件（移除接口定义）
7. **`src/Host.Api/Endpoints/DeviceEndpoints.cs`** - 仅显示需要修改的 using 部分
8. **`src/Host.Api/Endpoints/SettingsEndpoints.cs`** - 仅显示需要修改的 using 部分
9. **`src/Host.Api/Endpoints/AuthEndpoints.cs`** - 仅显示需要修改的 using 部分
10. **`src/Host.Api/Endpoints/AuditLogEndpoints.cs`** - 仅显示需要修改的 using 部分
11. **`src/Host.Api/Endpoints/AlarmRuleEndpoints.cs`** - 仅显示需要修改的 using 部分
12. **`src/Infrastructure/Pipeline/AlarmEvaluatorService.cs`** - 仅显示需要修改的 using 部分

---

## 验证方法

完成后执行：

```bash
dotnet build
```

应该编译通过，无错误。

---

## 注意事项

1. **不要修改实现代码**，只移动接口定义和更新 using 语句
2. **不要删除** `using IntelliMaint.Infrastructure.Sqlite` 如果文件中还使用了该命名空间的其他类型（如 `IDbExecutor`、`ConfigWatcherOptions` 等）
3. **保持所有方法签名不变**
4. Core 层不能有对 `Microsoft.Data.Sqlite` 的依赖
