# 审计日志 API

**权限**: Admin Only

## 查询审计日志

**GET** `/api/audit-logs`

### 查询参数

| 参数 | 类型 | 说明 |
|------|------|------|
| userId | string | 按用户筛选 |
| action | string | 按操作类型筛选 |
| resourceType | string | 按资源类型筛选 |
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
        "logId": "log123",
        "userId": "admin0000000001",
        "username": "admin",
        "action": "device.create",
        "resourceType": "device",
        "resourceId": "PLC-001",
        "details": "Created device: 1号 PLC",
        "ipAddress": "192.168.1.100",
        "createdUtc": 1704067200000
      }
    ],
    "nextCursor": "abc...",
    "totalCount": 100
  }
}
```

---

## 获取操作类型列表

**GET** `/api/audit-logs/actions`

### 响应

```json
{
  "success": true,
  "data": [
    "device.create",
    "device.update",
    "device.delete",
    "tag.create",
    "alarmrule.create",
    "user.create",
    "auth.login"
  ]
}
```

---

## 获取资源类型列表

**GET** `/api/audit-logs/resource-types`

### 响应

```json
{
  "success": true,
  "data": [
    "device",
    "tag",
    "alarm",
    "alarmrule",
    "user"
  ]
}
```
