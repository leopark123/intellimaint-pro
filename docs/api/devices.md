# 设备 API

## 获取设备列表

**GET** `/api/devices`

**权限**: All Authenticated

### 响应

```json
{
  "success": true,
  "data": [
    {
      "deviceId": "PLC-001",
      "name": "1号 PLC",
      "protocol": "LibPlcTag",
      "address": "192.168.1.100",
      "port": 44818,
      "enabled": true,
      "status": "online",
      "createdUtc": 1704067200000
    }
  ]
}
```

---

## 获取单个设备

**GET** `/api/devices/{deviceId}`

---

## 创建设备

**POST** `/api/devices`

**权限**: Admin, Operator

```json
{
  "deviceId": "PLC-002",
  "name": "2号 PLC",
  "protocol": "LibPlcTag",
  "address": "192.168.1.101",
  "port": 44818,
  "plcType": "ControlLogix",
  "path": "1,0",
  "enabled": true
}
```

---

## 更新设备

**PUT** `/api/devices/{deviceId}`

**权限**: Admin, Operator

---

## 删除设备

**DELETE** `/api/devices/{deviceId}`

**权限**: Admin Only
