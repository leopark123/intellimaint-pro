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
  -d '{"username":"viewer","password":"viewer123"}'

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

需要在数据库中添加不同角色的用户：

```sql
-- Operator 用户
INSERT INTO user (user_id, username, password_hash, role, display_name, enabled, created_utc)
VALUES ('op-001', 'operator', 'JAvlGPq9JyTdtvBO6x2llnRI1+gxwIyPqCKAn3THIKk=', 'Operator', 'Operator User', 1, 1735520000000);

-- Viewer 用户
INSERT INTO user (user_id, username, password_hash, role, display_name, enabled, created_utc)
VALUES ('vw-001', 'viewer', 'JAvlGPq9JyTdtvBO6x2llnRI1+gxwIyPqCKAn3THIKk=', 'Viewer', 'Viewer User', 1, 1735520000000);
```

**注意**: 上述密码哈希对应密码 `admin123`，实际使用时应修改。

---

**版本**: v38
**更新**: 2025-12-30
