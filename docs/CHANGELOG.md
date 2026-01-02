# IntelliMaint Pro 变更日志

## v45 (2025-12-30) - 健康评估引擎

### 新增功能

1. **特征提取服务**
   - 从遥测数据提取统计特征（均值、标准差、趋势等）
   - 支持单设备和批量提取
   - 可配置时间窗口

2. **健康评分计算器**
   - 基于特征和基线计算健康指数 (0-100)
   - 四维评分：偏差(40%) + 趋势(30%) + 稳定性(20%) + 告警(10%)
   - 健康等级：Healthy/Attention/Warning/Critical

3. **健康基线管理**
   - 设备正常运行时的特征基线
   - 自动学习（需足够数据量）
   - 持久化存储

4. **健康评估 API**
   - `GET /api/health-assessment/devices/{id}` - 获取设备健康评分
   - `GET /api/health-assessment/devices` - 获取所有设备健康评分
   - `GET /api/health-assessment/baselines/{id}` - 获取设备基线
   - `POST /api/health-assessment/baselines/{id}/learn` - 学习基线
   - `DELETE /api/health-assessment/baselines/{id}` - 删除基线

### 新增文件
- `src/Core/Abstractions/HealthAssessment.cs` - 接口和类型定义
- `src/Application/Services/FeatureExtractor.cs` - 特征提取服务
- `src/Application/Services/HealthScoreCalculator.cs` - 健康评分计算器
- `src/Application/Services/HealthAssessmentService.cs` - 健康评估服务
- `src/Infrastructure/Sqlite/HealthBaselineRepository.cs` - 基线仓储
- `src/Host.Api/Endpoints/HealthAssessmentEndpoints.cs` - API 端点
- `intellimaint-ui/src/api/healthAssessment.ts` - 前端 API 客户端

### Schema 变更 (v5 → v6)
```sql
CREATE TABLE health_baseline (
    device_id TEXT PRIMARY KEY,
    created_utc INTEGER NOT NULL,
    updated_utc INTEGER NOT NULL,
    sample_count INTEGER NOT NULL DEFAULT 0,
    learning_hours INTEGER NOT NULL DEFAULT 0,
    tag_baselines_json TEXT NOT NULL DEFAULT '{}'
);
```

### 健康评分算法

```
健康指数 = 偏差评分 × 0.4 + 趋势评分 × 0.3 + 稳定性评分 × 0.2 + 告警评分 × 0.1

偏差评分：基于 Z-Score（当前值与基线的标准差距离）
趋势评分：基于线性回归斜率
稳定性评分：基于变异系数(CV)与基线对比
告警评分：基于未关闭告警数量
```

### 使用示例

```bash
# 获取设备健康评分（30分钟窗口）
curl -H "Authorization: Bearer $TOKEN" \
  "http://localhost:5000/api/health-assessment/devices/device-001?windowMinutes=30"

# 学习设备基线（24小时数据）
curl -X POST -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"learningHours": 24}' \
  "http://localhost:5000/api/health-assessment/baselines/device-001/learn"
```

---

## v44 (2025-12-30) - 请求限流 + 审计增强

### 新增功能

1. **请求限流中间件**
   - 默认配置：60 秒内最多 100 次请求
   - 按 IP 地址限制
   - 支持 X-Forwarded-For 代理场景
   - 自动清理不活跃的 IP 记录

2. **审计服务增强**
   - 新增 `AuditService` 统一审计日志
   - 自动记录客户端 IP 地址
   - 审计动作常量类 `AuditActions`

3. **Dashboard SignalR 修复**
   - 添加 Token 认证到 SignalR 连接
   - 修复登录后 "SignalR 未连接" 问题

### 新增文件
- `src/Host.Api/Middleware/RateLimitingMiddleware.cs`
- `src/Host.Api/Services/AuditService.cs`

### 修改文件
- `src/Host.Api/Program.cs` - 注册服务和中间件
- `src/Host.Api/Endpoints/AuthEndpoints.cs` - 使用审计服务
- `intellimaint-ui/src/pages/Dashboard/index.tsx` - SignalR Token 认证

---

## v43 (2025-12-30) - SignalR 授权 + JWT 密钥外置

### 新增功能

1. **SignalR Hub 授权**
   - TelemetryHub 添加 `[Authorize]` 特性
   - 未认证用户无法连接实时数据推送

2. **JWT 密钥支持环境变量**
   - 优先读取环境变量 `JWT_SECRET_KEY`
   - 回退到 `appsettings.json` 中的配置
   - 密钥长度验证（至少 32 字符）

3. **SignalR JWT 认证配置**
   - 支持通过 Query String 传递 Token
   - 前端 SignalR 连接自动刷新 Token

### 修改文件
- `src/Host.Api/Hubs/TelemetryHub.cs` - 添加授权
- `src/Host.Api/Program.cs` - JWT 配置增强
- `src/Host.Api/Services/JwtService.cs` - 环境变量支持
- `intellimaint-ui/src/api/signalr.ts` - Token 自动刷新

### 生产环境配置
```bash
export JWT_SECRET_KEY="your-production-secret-key-at-least-32-chars"
```

---

## v42 (2025-12-30) - TypeScript 修复

### 问题修复
- AlarmManagement 空值检查
- Dashboard Recharts formatter 类型
- TelemetryQueryParams 索引签名

---

## v41 (2025-12-30) - API 兼容性修复

### 问题修复

| 问题 | 修复前 | 修复后 |
|------|--------|--------|
| Telemetry API 路径 | `/api/telemetry` | `/api/telemetry/query` |
| Alarm 状态参数 | `status=Active` (字符串) | `status=0` (整数) |
| Alarm 确认路径 | `/{id}/acknowledge` | `/{id}/ack` |
| Alarm 关闭路径 | `/{id}/resolve` | `/{id}/close` |
| SignalR 方法名 | `receivedata` | `ReceiveData` |

---

## v40 (2025-12) - 用户管理

### 新增功能
- 用户管理 API (CRUD)
- 用户管理前端页面
- 密码修改/重置功能

---

## v39 (2025-12) - Token 刷新机制

### 新增功能
- Access Token 有效期 15 分钟
- Refresh Token 有效期 7 天
- Token Rotation 安全机制
- 前端无感刷新

### Schema 变更 (v4 → v5)
```sql
ALTER TABLE user ADD COLUMN refresh_token TEXT;
ALTER TABLE user ADD COLUMN refresh_token_expires_utc INTEGER;
```

---

## v38 (2025-12) - 角色授权

### 新增功能
- RBAC 三级权限: Admin / Operator / Viewer
- 端点级别权限控制
- 前端路由守卫

---

## v37 (2025-12) - 审计日志完善

### 新增功能
- 17 个审计点全覆盖
- JWT 用户信息提取
- 操作追溯能力

---

## v36 (2025-12) - 接口重构

### 架构优化
- 4 个业务接口迁移到 Core.Abstractions
- 解除循环依赖

---

## v35 (2025-12) - JWT 认证

### 新增功能
- JWT 认证
- 用户表 + 密码哈希
- 前端登录页面
- 受保护路由

**默认账号**: admin / admin123

---

## 安全检查清单

| 安全项 | 状态 | 版本 |
|--------|------|------|
| JWT 认证 | ✅ | v35 |
| RBAC 权限 | ✅ | v38 |
| Token 刷新 | ✅ | v39 |
| SignalR 授权 | ✅ | v43 |
| JWT 密钥外置 | ✅ | v43 |
| 请求限流 | ✅ | v44 |
| 审计日志 IP | ✅ | v44 |

---

**当前版本**: v44  
**更新日期**: 2025-12-30
