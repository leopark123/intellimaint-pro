# IntelliMaint Pro v41 系列变更日志

## 版本信息
- **当前版本**: v41.2
- **发布日期**: 2025-12-30

---

## v41.2 - 模拟数据生成器

### 新增功能
- **SimulatedDataGenerator**: 自动生成模拟数据，解决实时曲线平坦问题
- 自动创建 3 个模拟设备
- 每秒生成随机游走数据，数据在合理范围内波动
- 优化图表 Y 轴配置

### 模拟设备配置

| 设备ID | 名称 | 标签 |
|--------|------|------|
| PUMP-001 | 水泵1号 | current, voltage, speed, temperature, vibration, pressure |
| MOTOR-001 | 电机1号 | current, voltage, speed, temperature, vibration |
| FAN-001 | 风机1号 | current, speed, temperature, airflow |

### 修改文件
- `src/Host.Api/Services/SimulatedDataGenerator.cs` - **新增**
- `src/Host.Api/Program.cs` - 注册服务
- `src/pages/Dashboard/index.tsx` - 优化图表配置

---

## v41.1 - TypeScript 类型修复

### 修复问题
| 文件 | 问题 | 修复 |
|------|------|------|
| `types/device.ts` | 缺少字段 | 添加 location, model, connectionString |
| `types/telemetry.ts` | valueType 类型不一致 | 改为可选 |
| `api/signalr.ts` | valueType 必填 | 改为可选 |
| `api/export.ts` | 缺少索引签名 | 添加 `[key: string]` |
| `pages/AlarmManagement` | 空值检查 | 添加 `?? 0` |
| `pages/Dashboard` | useEffect 依赖循环 | 使用 useRef |
| `pages/DataExplorer` | 类型错误 | 使用 TelemetryPoint |

---

## v41 - API 兼容性修复

### 1. Telemetry API 路径

| 修复前 | 修复后 |
|--------|--------|
| `/api/telemetry` | `/api/telemetry/query` |

### 2. Alarm API 参数

| 修复前 | 修复后 |
|--------|--------|
| `status=Active` (字符串) | `status=0` (整数) |
| `/{id}/acknowledge` | `/{id}/ack` |
| `/{id}/resolve` | `/{id}/close` |

**告警状态映射**：0=Open, 1=Acknowledged, 2=Closed

### 3. SignalR 方法名

| 修复前 | 修复后 |
|--------|--------|
| `receivedata` / `ReceiveTelemetry` | `ReceiveData` (大写 D！) |

---

## 部署说明

```bash
# 1. 启动后端
cd intellimaint-pro-v41-fixed/src/Host.Api
dotnet run

# 2. 启动前端 (另开终端)
cd intellimaint-pro-v41-fixed/intellimaint-ui
npm install
npm run dev

# 3. 访问
http://localhost:3000
```

**默认账号**: admin / admin123

---

## 验证清单

- [x] 浏览器控制台无 404/400 错误
- [x] SignalR 显示"实时连接"
- [x] 实时趋势图显示动态数据曲线
- [x] TypeScript 编译无错误
