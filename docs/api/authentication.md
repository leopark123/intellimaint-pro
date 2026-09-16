> Historical API note: may contain stale routes or payloads. Use the running Development Swagger and the source-backed [API index](README.md). Not production or accuracy evidence.

# 认证 API

## 登录

**POST** `/api/auth/login`

### 请求

```json
{
  "username": "admin",
  "password": "<ADMIN_PASSWORD from your environment>"
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

## 账户初始化

没有内置共享账号。空数据库需要显式配置 ADMIN_USERNAME 和唯一的 ADMIN_PASSWORD 来初始化管理员。
Operator / Viewer 由管理员通过已认证的用户管理 API 分别创建；不会共享 bootstrap 密码。
