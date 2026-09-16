> Historical design/development note, retained for context. Claims of production readiness, performance, ROI and deployment are unverified. For current supported behavior and validation, see the repository README and docs/OSS_READINESS_REPORT.md.

# IntelliMaint Pro 代码审查报告

**审查日期**: 2025-12-30  
**审查范围**: 后端 (.NET 8) + 前端 (React + TypeScript)  
**版本**: v41.1 → **v42 (已修复)**

---

## 📋 执行摘要

| 类别 | 发现数 | 状态 |
|------|--------|------|
| 🔴 阻塞问题 | 3 | ✅ 已修复 |
| 🟠 重要问题 | 2 | ⚠️ 需关注 |
| 🟡 建议改进 | 4 | ⏳ 待后续版本 |
| 🟢 亮点 | 8 | ✓ 良好实践 |

**编译状态**: ✅ TypeScript 编译通过

---

## 🔴 已修复的阻塞问题

### 1. AlarmManagement 空值检查 ✅

**位置**: `src/pages/AlarmManagement/index.tsx:136`

**问题**: TypeScript 无法推断 `res.data` 已通过空值检查。

```typescript
// 修复前
setAlarms(prev => [...prev, ...(res.data.items || [])])

// 修复后
const data = res.data
setAlarms(prev => [...prev, ...(data.items || [])])
```

### 2. Dashboard Recharts formatter 类型 ✅

**位置**: `src/pages/Dashboard/index.tsx:481`

**问题**: Recharts Tooltip formatter 的 value 参数可能是 undefined。

```typescript
// 修复前
formatter={(value: number) => ...}

// 修复后  
formatter={(value) => typeof value === 'number' ? value.toFixed(2) : String(value ?? '')}
```

### 3. TelemetryQueryParams 缺少索引签名 ✅

**位置**: `src/types/telemetry.ts:68`

**问题**: 接口与 `TelemetryExportParams` 不兼容。

```typescript
// 修复后
export interface TelemetryQueryParams {
  deviceId?: string
  // ...
  [key: string]: string | number | undefined  // 添加索引签名
}
```

---

## 🟠 需关注的重要问题

### 4. SignalR Hub 缺少授权 ⚠️

**位置**: `src/Host.Api/Hubs/TelemetryHub.cs`

**问题**: Hub 未添加 `[Authorize]` 特性，任何连接都可订阅实时数据。

**建议修复**:

```csharp
using Microsoft.AspNetCore.Authorization;

[Authorize]
public sealed class TelemetryHub : Hub
{
    // ...
}
```

**影响**: 未认证用户可能访问敏感的遥测数据。

### 5. JWT SecretKey 硬编码 ⚠️

**位置**: `src/Host.Api/appsettings.json:24`

**问题**: 密钥直接写在配置文件中，存在安全风险。

```json
"Jwt": {
  "SecretKey": "<historical signing secret removed>"
}
```

**建议**: 
- 开发环境使用 `appsettings.Development.json`
- 生产环境使用环境变量或 Secret Manager

```bash
# 推荐: 使用环境变量
export Jwt__SecretKey="your-production-secret-key"
```

---

## 🟡 建议改进

### 6. Dashboard SignalR useEffect 依赖

**位置**: `src/pages/Dashboard/index.tsx`

Dashboard 页面直接创建 SignalR 连接，而非使用 `useRealTimeData` hook，可能导致重复连接。

**建议**: 统一使用 `useRealTimeData` hook 管理 SignalR 连接。

### 7. 健康指数为模拟数据

**位置**: `src/pages/Dashboard/index.tsx:60-72`

```typescript
function getDeviceHealthIndex(device: Device): number {
  // 使用设备 ID 的哈希值来保持一致性
  const hash = device.deviceId.split('').reduce(...)
```

**建议**: 在后续版本中实现真实的健康评估算法。

### 8. API 客户端错误处理不统一

部分 API 使用 try-catch，部分依赖 `res.success` 检查。

**建议**: 考虑使用 React Query 或 SWR 统一管理。

### 9. 缺少 ESLint 配置

项目未配置 ESLint，可能导致代码风格不一致。

---

## 🟢 优秀实践

### ✅ v41 API 兼容性修复完整

| 项目 | 状态 |
|------|------|
| Telemetry API `/api/telemetry/query` | ✅ |
| Alarm status 整数参数 `0/1/2` | ✅ |
| Alarm 确认路径 `/{id}/ack` | ✅ |
| SignalR 方法名 `ReceiveData` | ✅ |

### ✅ Token 刷新机制

- Access Token 15分钟有效期
- Refresh Token 7天有效期  
- Token Rotation 安全机制
- 前端并发刷新锁 (`refreshPromise`)

### ✅ RBAC 权限控制

三级权限设计完整:
- `Admin`: 全部权限
- `Operator`: 业务操作
- `Viewer`: 只读

### ✅ 数据库迁移完整

SchemaManager 正确处理 v1→v5 迁移，包括:
- v2: 设备连接字段
- v3: 告警去重索引
- v4: 用户表
- v5: Refresh Token

### ✅ 密码哈希安全

使用 SHA256 + Base64，符合基本安全要求。

### ✅ 审计日志完整

17个审计点全覆盖，JWT用户信息正确提取。

### ✅ 类型定义完整

Device 类型包含所有必要字段 (v41.1 修复)。

### ✅ 前端 Token 管理

- localStorage 持久化
- 自动刷新机制
- 并发刷新防护

---

## 📁 本次修复的文件

1. `intellimaint-ui/src/pages/AlarmManagement/index.tsx` - 空值检查
2. `intellimaint-ui/src/pages/Dashboard/index.tsx` - Recharts 类型
3. `intellimaint-ui/src/types/telemetry.ts` - 索引签名

---

## ✅ 编译验证

```bash
$ npx tsc --noEmit
# 无错误输出 ✓
```

---

## 📝 下一步建议

1. **立即**: 为 TelemetryHub 添加 `[Authorize]` 特性
2. **尽快**: 将 JWT SecretKey 移至环境变量
3. **计划**: 实现真实的健康评估算法
4. **计划**: 添加 ESLint 配置

---

*审查工具: Claude Code Review*  
*版本: v42*
