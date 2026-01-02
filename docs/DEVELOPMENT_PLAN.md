# IntelliMaint Pro 完整开发计划

## 项目概述

**项目名称**：IntelliMaint Pro - 工业数据采集与监控平台  
**当前版本**：v21  
**目标版本**：v1.0.0 (生产就绪)

---

## 当前完成状态

| 模块 | 状态 | 版本 |
|------|------|------|
| Core 层 (契约、抽象) | ✅ 完成 | v1 |
| SQLite 基础设施 | ✅ 完成 | v3 |
| Pipeline 数据流 | ✅ 完成 | v4 |
| LibPlcTag 采集器 | ✅ 完成 | v5 |
| OPC UA 采集器 | ✅ 完成 | v12 |
| 数据流修复 | ✅ 完成 | v14 |
| 数据查询 API | ✅ 完成 | v19 |
| React UI 脚手架 | ✅ 完成 | v20 |
| 实时监控 Dashboard | ✅ 完成 | v21 |
| 历史数据查询页面 | ✅ 完成 | v21 |
| 实时趋势图 | ✅ 完成 | v21 |

---

## 剩余开发计划

### Phase 2 补充：API 完善

| Batch | 功能 | 优先级 | 预计时间 | 说明 |
|-------|------|--------|----------|------|
| B-22 | WebSocket 实时推送 | 高 | 1天 | 消除轮询，真正实时 |
| B-23 | 设备管理 API | 高 | 1天 | CRUD 操作 |
| B-24 | 标签配置 API | 高 | 1天 | CRUD + 批量导入 |
| B-25 | 告警规则 API | 中 | 1天 | 阈值告警配置 |
| B-26 | 健康监控 API | 中 | 0.5天 | 系统状态查询 |
| B-27 | 数据导出 API | 中 | 0.5天 | CSV/Excel 导出 |

### Phase 3 补充：UI 完善

| Batch | 功能 | 优先级 | 预计时间 | 说明 |
|-------|------|--------|----------|------|
| B-28 | WebSocket 集成 | 高 | 0.5天 | UI 接收实时推送 |
| B-29 | 设备管理页面 | 高 | 1天 | 设备列表、添加、编辑 |
| B-30 | 标签配置页面 | 高 | 1天 | 标签列表、配置编辑 |
| B-31 | 告警管理页面 | 中 | 1天 | 告警列表、规则配置 |
| B-32 | 系统设置页面 | 中 | 0.5天 | 采集参数、数据库配置 |
| B-33 | 数据导出功能 | 中 | 0.5天 | 导出按钮、进度显示 |

### Phase 4：高级功能（可选）

| Batch | 功能 | 优先级 | 预计时间 | 说明 |
|-------|------|--------|----------|------|
| B-34 | 用户认证 | 低 | 1.5天 | JWT 登录、权限 |
| B-35 | 报表生成 | 低 | 1天 | 日报/周报 PDF |
| B-36 | Modbus TCP | 低 | 1.5天 | 新协议支持 |
| B-37 | MQTT 客户端 | 低 | 1天 | 云端同步 |

---

## 详细 Batch 说明

### Batch 22: WebSocket 实时推送

**目标**：实现服务端主动推送数据到 UI，消除轮询延迟

**技术方案**：
- 使用 ASP.NET Core WebSocket 中间件
- 或使用 SignalR（更简单）

**输出文件**：
```
src/Host.Api/Hubs/TelemetryHub.cs          # SignalR Hub
src/Host.Api/Services/TelemetryBroadcaster.cs  # 广播服务
src/Host.Api/Program.cs                    # 注册 SignalR
```

**API 契约**：
```
WebSocket: /hubs/telemetry
事件: OnDataChanged(TelemetryDataPoint[])
事件: OnAlarmTriggered(AlarmRecord)
事件: OnDeviceStatusChanged(DeviceStatus)
```

---

### Batch 23: 设备管理 API

**目标**：通过 API 管理设备配置

**输出文件**：
```
src/Host.Api/Endpoints/DeviceEndpoints.cs
src/Host.Api/Models/DeviceModels.cs
src/Infrastructure/Sqlite/DeviceRepository.cs
```

**API 契约**：
```
GET    /api/devices              # 获取设备列表
GET    /api/devices/{id}         # 获取单个设备
POST   /api/devices              # 创建设备
PUT    /api/devices/{id}         # 更新设备
DELETE /api/devices/{id}         # 删除设备
POST   /api/devices/{id}/test    # 测试连接
```

**数据模型**：
```typescript
interface Device {
  deviceId: string
  name: string
  protocol: 'opcua' | 'libplctag' | 'modbus'
  connectionString: string  // OPC UA endpoint 或 PLC 地址
  enabled: boolean
  status: 'online' | 'offline' | 'error'
  lastSeen: number
  metadata: Record<string, string>
}
```

---

### Batch 24: 标签配置 API

**目标**：通过 API 管理标签配置

**输出文件**：
```
src/Host.Api/Endpoints/TagEndpoints.cs
src/Host.Api/Models/TagModels.cs
src/Infrastructure/Sqlite/TagRepository.cs (完善)
```

**API 契约**：
```
GET    /api/tags                     # 获取标签列表（支持分页、过滤）
GET    /api/tags/{id}                # 获取单个标签
POST   /api/tags                     # 创建标签
PUT    /api/tags/{id}                # 更新标签
DELETE /api/tags/{id}                # 删除标签
POST   /api/tags/import              # 批量导入（CSV/JSON）
GET    /api/devices/{id}/tags        # 获取设备下所有标签
```

**数据模型**：
```typescript
interface Tag {
  tagId: string
  deviceId: string
  name: string
  description: string
  address: string           // OPC UA NodeId 或 PLC 地址
  dataType: string          // UInt16, Float32, etc.
  scanIntervalMs: number    // 采集周期
  enabled: boolean
  unit: string
  tagGroup: string          // 分组（如：温度、压力）
  alarmEnabled: boolean
  alarmHighHigh: number
  alarmHigh: number
  alarmLow: number
  alarmLowLow: number
}
```

---

### Batch 25: 告警规则 API

**目标**：配置和管理告警规则

**输出文件**：
```
src/Host.Api/Endpoints/AlarmEndpoints.cs
src/Host.Api/Models/AlarmModels.cs
src/Application/Services/AlarmEngine.cs     # 告警引擎
src/Infrastructure/Sqlite/AlarmRepository.cs (完善)
```

**API 契约**：
```
GET    /api/alarms                   # 获取告警列表（支持状态过滤）
GET    /api/alarms/active            # 获取活动告警
POST   /api/alarms/{id}/ack          # 确认告警
POST   /api/alarms/{id}/close        # 关闭告警
GET    /api/alarm-rules              # 获取告警规则
POST   /api/alarm-rules              # 创建规则
PUT    /api/alarm-rules/{id}         # 更新规则
DELETE /api/alarm-rules/{id}         # 删除规则
```

---

### Batch 26: 健康监控 API

**目标**：查询系统运行状态

**输出文件**：
```
src/Host.Api/Endpoints/HealthEndpoints.cs
src/Host.Api/Models/HealthModels.cs
```

**API 契约**：
```
GET /api/health/status               # 系统总体状态
GET /api/health/collectors           # 采集器状态
GET /api/health/pipeline             # Pipeline 状态
GET /api/health/database             # 数据库状态
GET /api/health/history              # 历史健康快照
```

**响应示例**：
```json
{
  "status": "healthy",
  "uptime": 86400,
  "collectors": [
    {
      "name": "OpcUaCollector",
      "status": "running",
      "connectedDevices": 1,
      "pointsPerSecond": 1.0,
      "lastError": null
    }
  ],
  "pipeline": {
    "queueDepth": 5,
    "droppedPoints": 0,
    "writeLatencyMs": 12
  },
  "database": {
    "totalPoints": 10000,
    "sizeBytes": 1048576,
    "oldestData": "2025-12-27T00:00:00Z"
  }
}
```

---

### Batch 27: 数据导出 API

**目标**：导出历史数据

**输出文件**：
```
src/Host.Api/Endpoints/ExportEndpoints.cs
src/Application/Services/DataExporter.cs
```

**API 契约**：
```
POST /api/export/csv                 # 导出 CSV
POST /api/export/excel               # 导出 Excel
GET  /api/export/status/{jobId}      # 查询导出状态
GET  /api/export/download/{jobId}    # 下载文件
```

---

### Batch 28-33: UI 完善

详见各 Batch 的 ChatGPT 指令文档（按需生成）。

---

## 协作工作流程

```
┌─────────────────────────────────────────────────────────────────┐
│                        开发工作流                                │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  1. Claude 生成 ChatGPT 指令                                     │
│     ↓                                                           │
│  2. 用户复制指令到 ChatGPT                                       │
│     ↓                                                           │
│  3. ChatGPT 生成代码                                            │
│     ↓                                                           │
│  4. 用户提交代码给 Claude 审核                                   │
│     ↓                                                           │
│  5. Claude 修复问题、集成到项目                                  │
│     ↓                                                           │
│  6. 用户编译测试                                                 │
│     ↓                                                           │
│  7. 确认通过，进入下一个 Batch                                   │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## ChatGPT 指令模板

每个 Batch 的 ChatGPT 指令包含：

1. **项目背景** - 当前进度、技术栈
2. **Batch 目标** - 本批次要完成什么
3. **技术约束** - 必须遵守的规范
4. **API 契约** - 接口定义（如适用）
5. **数据模型** - 类型定义
6. **输出文件** - 需要创建/修改的文件列表
7. **代码要求** - 格式、命名、错误处理等
8. **测试用例** - 验证方法

---

## 推荐开发顺序

### 第一阶段：核心功能完善（3-4 天）

```
Day 1: B-22 WebSocket + B-28 UI 集成
Day 2: B-23 设备管理 API + B-29 设备管理 UI
Day 3: B-24 标签配置 API + B-30 标签配置 UI
Day 4: B-26 健康监控 API + B-32 系统设置 UI
```

### 第二阶段：告警与导出（2 天）

```
Day 5: B-25 告警规则 API + B-31 告警管理 UI
Day 6: B-27 数据导出 API + B-33 导出功能
```

### 第三阶段：可选功能（按需）

```
Day 7+: B-34 用户认证（如需要）
Day 8+: B-35 报表生成（如需要）
Day 9+: B-36 Modbus（如需要）
```

---

## 质量保证

### 代码审核检查清单

- [ ] 命名规范符合 C#/.NET 标准
- [ ] 错误处理完整（try-catch、参数验证）
- [ ] 日志记录适当
- [ ] API 返回格式统一（ApiResponse<T>）
- [ ] 数据库操作使用参数化查询
- [ ] 前端组件无 TypeScript 错误
- [ ] 代码可编译通过

### 测试验证

每个 Batch 完成后验证：
1. 编译通过（`dotnet build`）
2. API 测试（Swagger 或 curl）
3. UI 测试（浏览器验证）
4. 集成测试（端到端流程）

---

## 里程碑

| 版本 | 内容 | 目标日期 |
|------|------|----------|
| v22-v24 | 核心 API 完善 | +2 天 |
| v25-v27 | UI 完善 | +2 天 |
| v28-v30 | 告警 + 导出 | +2 天 |
| v1.0.0-beta | 功能完整 | +6 天 |
| v1.0.0 | 生产就绪 | +7 天 |

---

## 立即开始

请回复以下选项之一：

**A** - 从 Batch 22 (WebSocket) 开始  
**B** - 从 Batch 23 (设备管理 API) 开始  
**C** - 自定义选择其他 Batch

我将生成对应的 ChatGPT 开发指令。
