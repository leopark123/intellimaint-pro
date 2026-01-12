# 告警规则 API

## 获取规则列表

**GET** `/api/alarm-rules`

**权限**: All Authenticated

### 响应

```json
{
  "success": true,
  "data": [
    {
      "ruleId": "temp-high-001",
      "name": "温度过高告警",
      "tagId": "Temperature_01",
      "deviceId": "PLC-001",
      "conditionType": "gt",
      "threshold": 80,
      "durationMs": 5000,
      "severity": 3,
      "messageTemplate": "温度超过 {threshold}℃",
      "enabled": true,
      "ruleType": "threshold",
      "rocWindowMs": 0,
      "createdUtc": 1704067200000,
      "updatedUtc": 1704067200000
    }
  ]
}
```

---

## 创建规则

**POST** `/api/alarm-rules`

**权限**: Admin Only

### 阈值规则示例

```json
{
  "ruleId": "temp-high-002",
  "name": "温度过高告警",
  "tagId": "Temperature_01",
  "deviceId": "PLC-001",
  "conditionType": "gt",
  "threshold": 80,
  "durationMs": 5000,
  "severity": 3,
  "messageTemplate": "温度超过阈值",
  "enabled": true
}
```

### 离线检测规则 (v56 新增)

```json
{
  "ruleId": "offline-pump-001",
  "name": "水泵离线检测",
  "tagId": "Pump_Status",
  "conditionType": "offline",
  "threshold": 60,
  "severity": 4,
  "messageTemplate": "[离线] {tagId} 超过 {threshold} 秒无数据",
  "enabled": true
}
```

**说明**: `threshold` 为超时秒数

### 变化率规则 (v56 新增)

```json
{
  "ruleId": "roc-temp-001",
  "name": "温度骤变告警",
  "tagId": "Temperature_01",
  "conditionType": "roc_percent",
  "threshold": 15,
  "rocWindowMs": 60000,
  "severity": 3,
  "messageTemplate": "[变化率] {tagId} 变化超过 {threshold}%",
  "enabled": true
}
```

**说明**:
- `conditionType: roc_percent` - 百分比变化率
- `conditionType: roc_absolute` - 绝对值变化率
- `rocWindowMs` - 时间窗口（毫秒），最大 3600000 (1小时)

---

## 条件类型

| 类型 | 说明 | 规则类型 |
|------|------|----------|
| `gt` | 大于 | threshold |
| `gte` | 大于等于 | threshold |
| `lt` | 小于 | threshold |
| `lte` | 小于等于 | threshold |
| `eq` | 等于 | threshold |
| `ne` | 不等于 | threshold |
| `offline` | 离线检测 (v56) | offline |
| `roc_percent` | 变化率百分比 (v56) | roc |
| `roc_absolute` | 变化率绝对值 (v56) | roc |

---

## 更新规则

**PUT** `/api/alarm-rules/{ruleId}`

**权限**: Admin Only

### 请求

```json
{
  "name": "更新后的名称",
  "threshold": 90,
  "enabled": false
}
```

---

## 删除规则

**DELETE** `/api/alarm-rules/{ruleId}`

**权限**: Admin Only

---

## 启用/禁用规则

**PUT** `/api/alarm-rules/{ruleId}/enable`

**PUT** `/api/alarm-rules/{ruleId}/disable`

**权限**: Admin Only
