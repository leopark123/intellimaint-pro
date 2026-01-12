# IntelliMaint Pro v56 API 文档

## 概述

IntelliMaint Pro 提供 RESTful API 用于设备管理、数据采集、告警处理等功能。

**Base URL**: `http://localhost:5000`

## 认证

所有 API（除登录外）需要 JWT Bearer Token 认证：

```http
Authorization: Bearer <access_token>
```

## API 端点列表

| 模块 | 端点 | 说明 |
|------|------|------|
| [认证](./authentication.md) | `/api/auth/*` | 登录、刷新 Token |
| [设备](./devices.md) | `/api/devices/*` | 设备 CRUD |
| [标签](./tags.md) | `/api/tags/*` | 标签管理 |
| [遥测](./telemetry.md) | `/api/telemetry/*` | 数据查询 |
| [告警](./alarms.md) | `/api/alarms/*` | 告警管理 |
| [告警规则](./alarm-rules.md) | `/api/alarm-rules/*` | 规则配置 |
| [用户](./users.md) | `/api/users/*` | 用户管理 |
| [审计日志](./audit-logs.md) | `/api/audit-logs/*` | 操作审计 |

## 通用响应格式

```json
{
  "success": true,
  "data": { ... },
  "error": null
}
```

## 权限角色

| 角色 | 说明 |
|------|------|
| Admin | 完全访问权限 |
| Operator | 设备/告警管理（无删除权限） |
| Viewer | 只读权限 |

## v56 新增 API

- 告警规则支持 `conditionType: offline` (离线检测)
- 告警规则支持 `conditionType: roc_percent/roc_absolute` (变化率)
- 告警规则新增 `rocWindowMs` 字段
