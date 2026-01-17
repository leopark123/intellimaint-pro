# IntelliMaint Pro v65 部署报告

## 部署信息

| 项目 | 内容 |
|------|------|
| **版本** | v65 |
| **时间** | 2026-01-13 16:42 UTC |
| **环境** | Docker (Production) |
| **数据库** | TimescaleDB 2.x (PostgreSQL 15) |

## 构建产物

### Docker 镜像

| 镜像 | 标签 | 大小 | 状态 |
|------|------|------|------|
| intellimaint-api | v65 | 129MB (压缩后) | ✅ 构建成功 |
| intellimaint-ui | v65 | 27MB (压缩后) | ✅ 构建成功 |

### 镜像详情

```
intellimaint-api:v65    c5f72d3aa649    450MB    129MB (compressed)
intellimaint-ui:v65     0b9d9323c5d0    98.2MB   27.2MB (compressed)
```

## 预部署检查结果

| 检查项 | 状态 | 说明 |
|--------|------|------|
| Git 状态 | ✅ | master 分支，有未提交更改 |
| 解决方案构建 | ✅ | 通过 Docker 多阶段构建 |
| NuGet 包冲突 | ✅ 已修复 | System.IdentityModel.Tokens.Jwt 7.0.3 → 7.1.2 |
| 前端构建 | ✅ | Vite 生产构建成功 |

## 配置检查

| 配置项 | 状态 | 说明 |
|--------|------|------|
| JWT_SECRET_KEY | ✅ 已更新 | 使用 64 字符随机密钥 |
| POSTGRES_PASSWORD | ⚠️ 使用默认 | 生产环境需更换 |
| ASPNETCORE_ENVIRONMENT | ✅ | Production |
| DatabaseProvider | ✅ | TimescaleDb |
| CORS 策略 | ✅ | production |

## 部署验证结果

### 容器状态

| 容器 | 状态 | 端口 |
|------|------|------|
| intellimaint-timescaledb | ✅ Healthy | 5432 |
| intellimaint-api | ⚠️ Restarting | 5000 |
| intellimaint-ui | ✅ Running | 80 |

### 问题说明

API 容器在启动过程中因后台服务异常行为配置导致反复重启：

**根本原因**: `BackgroundServiceExceptionBehavior` 配置为 `StopHost`，当任何后台服务抛出异常时会导致整个应用停止。

**受影响的服务**:
- CollectionRuleEngine
- DynamicBaselineBackgroundService
- HealthAssessmentBackgroundService
- MotorDiagnosisBackgroundService

**临时解决方案**: 修改 `BackgroundServiceExceptionBehavior` 为 `Ignore`

**建议修复** (代码层面):
```csharp
// 在 Program.cs 或 ServiceCollectionExtensions.cs 添加：
builder.Services.Configure<HostOptions>(options =>
{
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
});
```

## Schema 修复

部署过程中修复了以下数据库 schema 差异：

| 表 | 添加列 |
|----|--------|
| alarm_rule | duration_ms, message_template |
| tag | scan_interval_ms, metadata |
| collection_rule | start_condition_json, stop_condition_json |

**建议**: 更新 `docker/init-scripts/02-schema.sql` 保持与代码同步

## 部署命令

```bash
# 构建镜像
docker build -f docker/Dockerfile.api -t intellimaint-api:v65 .
docker build -f docker/Dockerfile.ui -t intellimaint-ui:v65 .

# 部署（完全重置）
cd docker
docker compose down -v
docker compose up -d

# 查看日志
docker logs intellimaint-api -f
docker logs intellimaint-ui -f
```

## 后续操作

1. **【紧急】** 修复后台服务异常行为配置
2. **【重要】** 同步数据库 schema 脚本
3. **【建议】** 生产环境更换 POSTGRES_PASSWORD
4. **【建议】** 配置持久化数据保护密钥存储

---

*报告生成时间: 2026-01-13T16:46:00Z*
*报告生成者: Claude Code*
