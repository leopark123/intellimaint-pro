# 认证 API

## 登录

**POST** `/api/auth/login`

### 请求

```json
{
  "username": "admin",
  "password": "admin123"
}
```

### 响应

```json
{
  "success": true,
  "data": {
    "token": "eyJhbGciOiJIUzI1NiIs...",
    "refreshToken": "abc123...",
    "username": "admin",
    "role": "Admin",
    "expiresAt": 1704345600000,
    "refreshExpiresAt": 1704950400000
  }
}
```

### Token 有效期

- Access Token: 15 分钟
- Refresh Token: 7 天

---

## 刷新 Token

**POST** `/api/auth/refresh`

### 请求

```json
{
  "refreshToken": "abc123..."
}
```

### 响应

```json
{
  "success": true,
  "data": {
    "token": "eyJhbGciOiJIUzI1NiIs...",
    "refreshToken": "xyz789...",
    "expiresAt": 1704345600000
  }
}
```

---

## 默认账号

| 用户名 | 密码 | 角色 |
|--------|------|------|
| admin | admin123 | Admin |
| operator | operator123 | Operator |
| viewer | viewer123 | Viewer |
