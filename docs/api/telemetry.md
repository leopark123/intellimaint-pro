# 遥测数据 API

## 查询历史数据

**GET** `/api/telemetry/query`

**权限**: All Authenticated

### 查询参数

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| tagId | string | 是 | 标签 ID |
| startTs | long | 是 | 开始时间戳 (ms) |
| endTs | long | 否 | 结束时间戳 (ms) |
| limit | int | 否 | 返回数量 (默认 1000) |

### 响应

```json
{
  "success": true,
  "data": {
    "items": [
      {
        "tagId": "Temperature_01",
        "ts": 1704067200000,
        "value": 65.5
      }
    ]
  }
}
```

---

## 获取最新数据

**GET** `/api/telemetry/latest`

### 查询参数

| 参数 | 类型 | 说明 |
|------|------|------|
| deviceId | string | 按设备筛选 |
| tagIds | string | 逗号分隔的标签 ID |

### 响应

```json
{
  "success": true,
  "data": [
    {
      "tagId": "Temperature_01",
      "deviceId": "PLC-001",
      "ts": 1704067200000,
      "value": 65.5
    }
  ]
}
```

---

## SignalR 实时推送

**Hub 端点**: `/hubs/telemetry`

### 订阅方法

```javascript
// 订阅所有设备
connection.invoke("SubscribeAll");

// 订阅特定设备
connection.invoke("SubscribeDevice", "PLC-001");

// 取消订阅
connection.invoke("UnsubscribeAll");
```

### 接收事件

```javascript
// 接收实时数据
connection.on("ReceiveData", (data) => {
  console.log(data);
});

// 接收告警
connection.on("ReceiveAlarm", (alarm) => {
  console.log(alarm);
});
```
