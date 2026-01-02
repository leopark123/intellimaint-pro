# IntelliMaint Pro 变更日志

## v55 (2025-01-01) - LibPlcTag 完整支持 + 模拟模式

### 🎯 核心目标

1. 添加 LibPlcTag 模拟模式，无需真实 PLC 即可测试
2. 支持从数据库加载 LibPlcTag 配置（与 OPC UA 一致）
3. 前端完整支持 LibPlcTag 协议配置

---

### ✨ Phase 1: 后端模拟模式

| 文件 | 说明 |
|------|------|
| `SimulatedTagReader.cs` | 新增 - 模拟数据生成器 |
| `LibPlcTagCollector.cs` | 修改 - 支持模拟模式 |
| `ProtocolOptions.cs` | 修改 - 添加 SimulationMode |
| `LibPlcTagServiceExtensions.cs` | 修改 - 注册模拟器 |

**模拟数据类型：**

| 模式 | 适用场景 | 示例标签名 |
|------|----------|-----------|
| **正弦波** | 温度、电流、速度 | `*_TEMP`, `*_CURRENT`, `*_SPEED` |
| **锯齿波** | 设定值、斜坡 | `*_SETPOINT`, `*_RAMP` |
| **随机波动** | 液位、压力、流量 | `*_LEVEL`, `*_PRESSURE`, `*_FLOW` |
| **Bool 切换** | 开关状态 | CipType = BOOL |
| **递增计数** | 产量、计数器 | `*_COUNT`, `*_TOTAL`, `*_PROD` |

**配置示例：**
```json
"LibPlcTag": {
  "Enabled": true,
  "SimulationMode": true,  // ← 启用模拟模式
  "Plcs": [...]
}
```

---

### ✨ Phase 2: 数据库配置适配器

| 文件 | 说明 |
|------|------|
| `LibPlcTagConfigAdapter.cs` | 新增 - 从数据库加载配置 |
| `LibPlcTagCollector.cs` | 修改 - 支持 DB 配置 + 热重载 |

**Device Metadata 字段：**

| 字段 | 说明 | 默认值 |
|------|------|--------|
| `PlcType` | PLC 类型 | ControlLogix |
| `Path` | 路径 | 1,0 |
| `Slot` | 槽位 | 0 |
| `MaxConnections` | 最大连接数 | 4 |
| `TimeoutMs` | 超时 | 5000 |
| `ReadMode` | 读取模式 | BatchRead |

**Tag Metadata 字段：**

| 字段 | 说明 |
|------|------|
| `CipType` | CIP 数据类型 (BOOL/DINT/REAL 等) |
| `ArrayLength` | 数组长度 |

---

### ✨ Phase 3: 前端支持

| 文件 | 修改内容 |
|------|----------|
| `types/device.ts` | 添加 LibPlcTag 协议 + PlcType 选项 |
| `types/tag.ts` | 添加 CipType 选项 |
| `DeviceManagement/index.tsx` | PlcType、Path、Slot 表单字段 |
| `TagManagement/index.tsx` | CipType 下拉框（LibPlcTag 设备时显示） |

**协议选项更新：**
```typescript
export const ProtocolOptions = [
  { value: 'LibPlcTag', label: 'Allen-Bradley (LibPlcTag)' },  // 新增
  { value: 'OpcUa', label: 'OPC UA' },
  { value: 'ModbusTcp', label: 'Modbus TCP' },
  { value: 'S7', label: 'Siemens S7' },
  { value: 'Mqtt', label: 'MQTT' }
]
```

---

### 📁 新增文件清单

**后端 (4个)：**
```
src/Infrastructure/Protocols/LibPlcTag/
├── SimulatedTagReader.cs       # 模拟数据生成器 (~220行)
└── LibPlcTagConfigAdapter.cs   # 数据库配置适配器 (~160行)
```

**前端 (修改2个)：**
```
intellimaint-ui/src/types/
├── device.ts  # 添加 LibPlcTag 协议
└── tag.ts     # 添加 CipType 选项
```

---

### 🚀 快速测试

```bash
# 1. 修改 Host.Edge/appsettings.json
# 设置 LibPlcTag.Enabled = true, SimulationMode = true

# 2. 启动 Edge 服务
dotnet run --project src/Host.Edge

# 3. 启动 API 服务
dotnet run --project src/Host.Api

# 4. 启动前端
npm run dev --prefix intellimaint-ui

# 5. 访问 Dashboard 查看模拟数据
http://localhost:3000
```

---

### ✅ 验收清单

**后端：**
- [x] SimulatedTagReader 生成 5 种模拟数据
- [x] LibPlcTagCollector 支持 SimulationMode
- [x] LibPlcTagConfigAdapter 从数据库加载配置
- [x] 配置热重载（设备/标签变更时自动重载）
- [x] 日志输出模拟模式警告

**前端：**
- [x] 协议选项包含 LibPlcTag
- [x] 设备表单显示 PlcType/Path/Slot（LibPlcTag 时）
- [x] 标签表单显示 CipType（LibPlcTag 设备时）
- [x] CipType 自动映射到 DataType

---

### 📋 下一步

| 版本 | 内容 |
|------|------|
| v56 | Dashboard 接入真实健康评估 API |
| v57 | Modbus TCP 协议实现 |
| v58 | Docker 部署配置 |

---

**版本**: 0.0.55  
**日期**: 2025-01-01  
**主题**: LibPlcTag 完整支持 + 模拟模式
