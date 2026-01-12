# 用户管理 API

**权限**: Admin Only

## 获取用户列表

**GET** `/api/users`

### 响应

```json
{
  "success": true,
  "data": [
    {
      "userId": "admin0000000001",
      "username": "admin",
      "displayName": "Administrator",
      "role": "Admin",
      "enabled": true,
      "createdUtc": 1704067200000
    }
  ]
}
```

---

## 创建用户

**POST** `/api/users`

```json
{
  "username": "newuser",
  "password": "password123",
  "displayName": "New User",
  "role": "Operator"
}
```

---

## 更新用户

**PUT** `/api/users/{userId}`

```json
{
  "displayName": "Updated Name",
  "role": "Viewer",
  "enabled": true
}
```

---

## 重置密码

**POST** `/api/users/{userId}/reset-password`

```json
{
  "newPassword": "newpassword123"
}
```

---

## 删除用户

**DELETE** `/api/users/{userId}`
