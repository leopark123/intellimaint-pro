> Historical design/development note, retained for context. Claims of production readiness, performance, ROI and deployment are unverified. For current supported behavior and validation, see the repository README and docs/OSS_READINESS_REPORT.md.

# IntelliMaint Pro v38 - 角色权限矩阵

## 角色定义

| 角色 | 代码常量 | 说明 |
|------|----------|------|
| Admin | `UserRoles.Admin` | 系统管理员，拥有全部权限 |
| Operator | `UserRoles.Operator` | 操作员，可执行业务操作 |
| Viewer | `UserRoles.Viewer` | 查看者，只读权限 |

---

## 授权策略

| 策略名 | 允许的角色 | 用途 |
|--------|-----------|------|
| `AdminOnly` | Admin | 配置管理 |
| `OperatorOrAbove` | Admin, Operator | 业务操作 |
| `AllAuthenticated` | Admin, Operator, Viewer | 数据读取 |

---

## 权限矩阵

```
┌────────────────────────┬─────────┬──────────┬────────┐
│ 功能                   │ Admin   │ Operator │ Viewer │
├────────────────────────┼─────────┼──────────┼────────┤
│ 登录                   │ ✅      │ ✅       │ ✅     │
│ 查看遥测数据           │ ✅      │ ✅       │ ✅     │
│ 查看健康状态           │ ✅      │ ✅       │ ✅     │
│ 导出数据               │ ✅      │ ✅       │ ✅     │
│ 查看设备/标签列表       │ ✅      │ ✅       │ ✅     │
│ 查看告警列表           │ ✅      │ ✅       │ ✅     │
│ 查看告警规则列表       │ ✅      │ ✅       │ ✅     │
│ 查看系统设置           │ ✅      │ ✅       │ ✅     │
├────────────────────────┼─────────┼──────────┼────────┤
│ 确认/关闭告警          │ ✅      │ ✅       │ ❌     │
│ 创建告警               │ ✅      │ ✅       │ ❌     │
│ 查看审计日志           │ ✅      │ ✅       │ ❌     │
├────────────────────────┼─────────┼──────────┼────────┤
│ 设备 增删改            │ ✅      │ ❌       │ ❌     │
│ 标签 增删改            │ ✅      │ ❌       │ ❌     │
│ 告警规则 增删改        │ ✅      │ ❌       │ ❌     │
│ 系统设置 修改          │ ✅      │ ❌       │ ❌     │
│ 数据清理               │ ✅      │ ❌       │ ❌     │
└────────────────────────┴─────────┴──────────┴────────┘
```

---

## API 端点授权映射

| 端点 | GET | POST | PUT | DELETE | 其他 |
|------|-----|------|-----|--------|------|
| /api/telemetry/* | All | - | - | - | - |
| /api/health/* | All | - | - | - | - |
| /api/export/* | All | All | - | - | - |
| /api/devices | All | Admin | Admin | Admin | - |
| /api/tags | All | Admin | Admin | Admin | - |
| /api/alarms | All | Operator+ | - | - | ack/close: Operator+ |
| /api/alarm-rules | All | Admin | Admin | Admin | enable/disable: Admin |
| /api/settings | All | - | Admin | Admin | cleanup: Admin |
| /api/audit-logs | Operator+ | - | - | - | - |
| /api/auth/login | Anonymous | - | - | - | - |

**图例**: All = AllAuthenticated, Operator+ = OperatorOrAbove, Admin = AdminOnly

---

## 测试验证

### Viewer 测试
```bash
# 登录获取 Token
curl -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"viewer","password":"[removed legacy password]"}'

# 可以：查看遥测
curl http://localhost:5000/api/telemetry/latest \
  -H "Authorization: Bearer <token>"

# 不可以：创建设备 (403)
curl -X POST http://localhost:5000/api/devices \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"deviceId":"test","name":"Test"}'
```

### Operator 测试
```bash
# 可以：确认告警
curl -X POST http://localhost:5000/api/alarms/{id}/ack \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"ackedBy":"operator1"}'

# 可以：查看审计日志
curl http://localhost:5000/api/audit-logs \
  -H "Authorization: Bearer <token>"

# 不可以：创建设备 (403)
```

### Admin 测试
```bash
# 可以：创建设备
curl -X POST http://localhost:5000/api/devices \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"deviceId":"new-device","name":"New Device","protocol":"opcua"}'

# 可以：修改系统设置
curl -X PUT http://localhost:5000/api/settings/retention.telemetry.days \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"value":"60"}'
```

---

## 创建测试用户

历史文档曾提供可复制的固定密码哈希 INSERT，现已移除。不要直接写入用户表或复用历史哈希。

空数据库通过显式 ADMIN_USERNAME / ADMIN_PASSWORD 初始化首个管理员；其他角色由管理员通过已认证的用户管理 API 创建，并使用各自唯一的密码。
迁移中用于识别旧默认凭据并要求改密的安全逻辑仍须保留。

---

**版本**: v38
**更新**: 2025-12-30
