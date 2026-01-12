# 告警 API

## 查询告警列表

**GET** `/api/alarms`

**权限**: All Authenticated

### 查询参数

| 参数 | 类型 | 说明 |
|------|------|------|
| deviceId | string | 按设备筛选 |
| status | int | 0=Open, 1=Acknowledged, 2=Closed |
| minSeverity | int | 最小严重级别 (1-5) |
| startTs | long | 开始时间戳 (ms) |
| endTs | long | 结束时间戳 (ms) |
| limit | int | 返回数量 (默认 50) |
| after | string | 分页游标 |

### 响应

```json
{
  "success": true,
  "data": {
    "items": [
      {
        "alarmId": "a123456",
        "deviceId": "PLC-001",
        "tagId": "Temperature_01",
        "ts": 1704067200000,
        "severity": 3,
        "code": "TEMP_HIGH",
        "message": "温度过高",
        "status": 0,
        "ackedBy": null,
        "ackedUtc": null
      }
    ],
    "nextCursor": "abc..."
  }
}
```

---

## 获取告警统计

**GET** `/api/alarms/stats`

### 响应

```json
{
  "success": true,
  "data": {
    "openCount": 5
  }
}
```

---

## 确认告警

**POST** `/api/alarms/{alarmId}/ack`

**权限**: Admin, Operator

### 请求

```json
{
  "ackedBy": "admin",
  "ackNote": "已处理"
}
```

---

## 关闭告警

**POST** `/api/alarms/{alarmId}/close`

**权限**: Admin, Operator
