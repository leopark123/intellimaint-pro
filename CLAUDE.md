# IntelliMaint Pro - Claude Code 项目知识库

> 这是 Claude Code 的项目知识文件，帮助 Claude 理解项目上下文。

## 项目概述

**IntelliMaint Pro** 是一个工业 AI 预测性维护平台，用于实时监控工业设备、采集传感器数据、评估设备健康状态、预测潜在故障。

### 核心价值
- 🔍 **实时监控** - 毫秒级数据采集与推送
- 🏥 **健康评估** - 设备健康指数 0-100
- ⚠️ **智能告警** - 多级阈值告警引擎
- 🔮 **故障预测** - 72+ 小时提前预警

## 技术栈

### 后端
- **.NET 8** - Minimal API
- **SQLite** - 开发/MVP 数据库（生产迁移 TimescaleDB）
- **SignalR** - 实时双向通信
- **Dapper** - 高性能 ORM

### 前端
- **React 18** + TypeScript
- **Ant Design 5** - UI 组件库
- **Zustand** - 状态管理
- **Recharts** - 数据可视化

### 工业协议
- **OPC UA** - 工业标准协议
- **LibPlcTag** - Allen-Bradley PLC 通信

## 项目结构

```
intellimaint-pro-v56/
├── src/
│   ├── Core/                    # 核心层 - 接口与契约
│   │   ├── Abstractions/        # 接口定义
│   │   └── Contracts/           # DTO、实体、枚举
│   │
│   ├── Infrastructure/          # 基础设施层
│   │   ├── Sqlite/              # SQLite 仓储实现
│   │   ├── Pipeline/            # 数据采集管道
│   │   └── Protocols/           # 工业协议
│   │       ├── OpcUa/           # OPC UA 实现
│   │       └── LibPlcTag/       # LibPlcTag 实现
│   │
│   ├── Application/             # 应用层 - 业务服务
│   │   └── Services/            # 健康评估、周期分析等
│   │
│   ├── Host.Api/                # API 宿主 (端口 5000)
│   │   ├── Program.cs           # 入口点
│   │   ├── Endpoints/           # Minimal API 端点
│   │   ├── Hubs/                # SignalR Hub
│   │   ├── Services/            # 后台服务
│   │   └── Middleware/          # 中间件
│   │
│   └── Host.Edge/               # 边缘采集服务
│
├── intellimaint-ui/             # React 前端 (端口 3000)
│   ├── src/
│   │   ├── api/                 # API 调用
│   │   ├── components/          # 通用组件
│   │   ├── pages/               # 页面组件
│   │   ├── hooks/               # 自定义 Hooks
│   │   ├── store/               # 状态管理
│   │   └── types/               # TypeScript 类型
│   └── package.json
│
├── tests/                       # 测试项目
│   ├── Unit/                    # 单元测试
│   └── Integration/             # 集成测试
│
├── docs/                        # 项目文档
│
└── .claude/                     # Claude Code 配置
    ├── agents/                  # Agent 配置
    └── commands/                # 自定义命令
```

## 核心模块说明

### 数据采集管道
```
PLC/传感器 → Collector → Channel → Pipeline → DB + SignalR + AlarmEngine
```

### 告警引擎
- 支持多级阈值（Info/Warning/Error/Critical）
- 实时评估每个数据点
- 防抖动机制避免告警风暴

### 认证授权
- JWT Bearer Token（15分钟有效）
- Refresh Token（7天有效）
- RBAC 三角色：Admin / Operator / Viewer

## API 端点概览

| 端点 | 方法 | 说明 | 权限 |
|------|------|------|------|
| /api/auth/login | POST | 登录 | 公开 |
| /api/auth/refresh | POST | 刷新 Token | 公开 |
| /api/devices | GET/POST | 设备管理 | All/Admin,Op |
| /api/devices/{id} | GET/PUT/DELETE | 设备操作 | All/Admin,Op/Admin |
| /api/tags | GET/POST | 标签管理 | All/Admin,Op |
| /api/telemetry/query | GET | 数据查询 | All |
| /api/telemetry/latest | GET | 最新数据 | All |
| /api/alarms | GET | 告警列表 | All |
| /api/alarms/{id}/ack | POST | 确认告警 | Admin,Op |
| /api/alarm-rules | GET/POST | 告警规则 | All/Admin,Op |
| /api/users | GET/POST | 用户管理 | Admin |
| /api/audit-logs | GET | 审计日志 | Admin |
| /api/health | GET | 健康检查 | 公开 |

## SignalR Hub

**端点**: `/hubs/telemetry`

**方法**:
- `SubscribeAll()` - 订阅所有设备
- `SubscribeDevice(int deviceId)` - 订阅指定设备
- `UnsubscribeAll()` - 取消订阅

**事件**:
- `ReceiveData(List<TelemetryPoint> data)` - 接收实时数据

## 开发指南

### 启动后端
```bash
cd src/Host.Api
dotnet run
# 访问 http://localhost:5000
```

### 启动前端
```bash
cd intellimaint-ui
npm install
npm run dev
# 访问 http://localhost:3000
```

### 默认账号
- Admin: `admin` / `admin123`
- Operator: `operator` / `operator123`
- Viewer: `viewer` / `viewer123`

## 开发规范

### C# 规范
- 异步方法以 `Async` 结尾
- 使用 `CancellationToken`
- 私有字段 `_camelCase`
- 方法不超过 30 行

### React 规范
- 函数组件 + Hooks
- TypeScript 严格模式
- 组件文件 PascalCase
- 自定义 Hook 以 `use` 开头

### Git 提交规范
```
<type>(<scope>): <description>

feat(api): add device batch import endpoint
fix(ui): fix chart color not updating on theme change
docs(api): add authentication documentation
```

## 当前开发状态

### ✅ 已完成
- 数据采集管道（OPC UA + LibPlcTag）
- SignalR 实时推送
- 告警引擎（规则配置 + 实时评估）
- JWT + RBAC 认证授权
- 审计日志
- PLC 模拟器

### 🚧 开发中
- 健康评估引擎（0-100 指数）
- 故障预测模型

### 📋 规划中
- 知识图谱
- Modbus TCP 协议
- Docker 部署
- TimescaleDB 迁移

## Agent 使用指南

项目配置了 13 个专业 Agent：

| Agent | 用途 |
|-------|------|
| architect | 架构决策、任务协调 |
| backend-expert | .NET 后端开发 |
| frontend-expert | React 前端开发 |
| database-expert | 数据库设计优化 |
| realtime-expert | SignalR 实时通信 |
| industrial-expert | 工业协议开发 |
| security-expert | 安全相关开发 |
| performance-expert | 性能优化 |
| ai-ml-expert | AI/算法开发 |
| testing-expert | 测试相关 |
| devops-expert | 部署运维 |
| code-reviewer | 代码审查 |
| docs-expert | 文档编写 |

### 使用方式
```
# 自动选择（推荐）
优化 TelemetryEndpoints.cs 的性能

# 手动指定
使用 backend-expert 重构 DeviceRepository
使用 architect 评估添加 Modbus 支持的方案
```

## 常用命令

```bash
# 自定义命令
/optimize          # 执行性能优化流程
/review            # 执行代码审查流程
/test              # 运行完整测试
/deploy            # 部署流程

# 查看 Agent
/agents
```

## 性能目标

| 指标 | 目标值 |
|------|--------|
| API P95 响应时间 | < 100ms |
| SignalR 推送延迟 | < 50ms |
| 页面首次加载 | < 2s |
| 数据库查询 | < 20ms |
