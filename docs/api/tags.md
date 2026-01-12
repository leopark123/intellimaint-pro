# 标签 API

## 获取标签列表

**GET** `/api/tags`

**权限**: All Authenticated

### 查询参数

| 参数 | 类型 | 说明 |
|------|------|------|
| deviceId | string | 按设备筛选 |
| enabled | bool | 按启用状态筛选 |

### 响应

```json
{
  "success": true,
  "data": [
    {
      "tagId": "Temperature_01",
      "deviceId": "PLC-001",
      "name": "温度传感器1",
      "address": "Program:Main.Temperature",
      "dataType": "REAL",
      "unit": "℃",
      "scanRate": 1000,
      "enabled": true
    }
  ]
}
```

---

## 创建标签

**POST** `/api/tags`

**权限**: Admin, Operator

```json
{
  "tagId": "Pressure_01",
  "deviceId": "PLC-001",
  "name": "压力传感器",
  "address": "Program:Main.Pressure",
  "dataType": "REAL",
  "unit": "bar",
  "scanRate": 1000,
  "enabled": true
}
```

---

## 更新标签

**PUT** `/api/tags/{tagId}`

**权限**: Admin, Operator

---

## 删除标签

**DELETE** `/api/tags/{tagId}`

**权限**: Admin Only

---

## 支持的数据类型

| 类型 | 说明 |
|------|------|
| BOOL | 布尔值 |
| INT16 | 16 位整数 |
| INT32 | 32 位整数 |
| REAL | 32 位浮点数 |
| LREAL | 64 位浮点数 |
| STRING | 字符串 |
