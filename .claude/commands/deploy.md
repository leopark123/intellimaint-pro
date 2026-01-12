---
name: deploy
description: 执行部署流程，包括构建、测试、打包、部署
---

# 部署任务

请按照以下步骤执行部署流程：

## 1. 预部署检查

### 代码检查
```bash
# 确保在主分支且代码最新
git status
git pull origin main
```

### 测试检查
```bash
# 运行所有测试
dotnet test
```

如果测试失败，停止部署并报告问题。

## 2. 构建

### 后端构建
```bash
cd src/Host.Api
dotnet publish -c Release -o ./publish
```

### 前端构建
```bash
cd intellimaint-ui
npm ci
npm run build
```

## 3. Docker 构建（如果使用容器）

检查 Dockerfile 是否存在，如果存在：

```bash
# 构建 API 镜像
docker build -f Dockerfile.api -t intellimaint-api:latest .

# 构建 UI 镜像
docker build -f Dockerfile.ui -t intellimaint-ui:latest .
```

如果 Dockerfile 不存在，使用 `devops-expert` 的模板创建。

## 4. 配置检查

确认生产环境配置：

- [ ] JWT_SECRET_KEY 已设置（非默认值）
- [ ] 数据库连接字符串正确
- [ ] ASPNETCORE_ENVIRONMENT=Production
- [ ] 日志级别适当

## 5. 部署方式

根据环境选择部署方式：

### 方式 A: Docker Compose
```bash
docker-compose -f docker-compose.yml up -d
```

### 方式 B: 直接部署
```bash
# 停止旧服务
# 复制新文件
# 启动新服务
```

### 方式 C: 生成部署包
```bash
# 打包所有文件供手动部署
```

## 6. 部署验证

部署后验证：

```bash
# 健康检查
curl http://localhost:5000/api/health

# 检查日志
docker logs intellimaint-api --tail 50
```

## 7. 生成部署报告

```markdown
# 部署报告

## 部署信息
- **版本**: vXX
- **时间**: xxx
- **环境**: Production/Staging

## 构建产物
- API: xxx
- UI: xxx

## 验证结果
- [x] 健康检查通过
- [x] 登录功能正常
- [x] 数据采集正常

## 注意事项
- xxx
```

---

**快捷选项**：

- `/deploy build` - 仅构建，不部署
- `/deploy docker` - Docker 方式部署
- `/deploy check` - 仅检查，不执行
- `/deploy rollback` - 回滚到上一版本

## ⚠️ 质量门禁 (必须满足)

部署必须严格按流程执行，每个阶段都有明确的检查点：

### 阶段 1: 预部署检查
- [ ] **代码状态** - 主分支，无未提交变更
- [ ] **测试通过** - `dotnet test` 100% 通过
- [ ] **构建成功** - `dotnet build` 无错误
- [ ] **安全检查** - 无硬编码密钥/敏感信息

### 阶段 2: 构建验证
- [ ] **后端构建** - `dotnet publish` 成功
- [ ] **前端构建** - `npm run build` 成功
- [ ] **产物完整** - 所有必要文件存在

### 阶段 3: 配置验证
- [ ] **环境变量** - JWT_SECRET_KEY 已设置
- [ ] **数据库连接** - 连接字符串正确
- [ ] **日志配置** - 生产级别日志

### 阶段 4: 部署执行
- [ ] **备份当前版本** - 可回滚
- [ ] **停止旧服务** - 优雅关闭
- [ ] **部署新版本** - 按计划执行
- [ ] **启动服务** - 无启动错误

### 阶段 5: 部署验证
- [ ] **健康检查** - `/api/health` 返回 200
- [ ] **冒烟测试** - 核心功能可用
- [ ] **日志检查** - 无异常错误
- [ ] **监控确认** - 指标正常

### ❌ 不合格示例
```markdown
部署完成:
- 已部署新版本     ← 没有说明执行了哪些步骤
- 应该没问题       ← 没有验证证据
```

### ✅ 合格示例
```markdown
## 部署报告

### 预部署检查
- [x] 代码状态: master 分支, commit abc123
- [x] 测试: 48/48 通过
- [x] 构建: 成功

### 构建产物
- API: `publish/IntelliMaint.Host.Api.dll` (2.3MB)
- UI: `dist/` (1.8MB)

### 配置验证
- [x] JWT_SECRET_KEY: 已设置 (长度 64)
- [x] 数据库: PostgreSQL 连接测试成功
- [x] 环境: Production

### 部署执行
- 备份: `backup-20240101-120000.tar.gz`
- 停止旧服务: 成功
- 部署新版本: 成功
- 启动服务: 成功

### 部署验证
- [x] 健康检查: 200 OK (响应时间 45ms)
- [x] 登录测试: 成功
- [x] 数据采集: 正常
- [x] 日志检查: 无错误

### 回滚方案
如需回滚，执行:
\`\`\`bash
docker-compose down
tar -xzf backup-20240101-120000.tar.gz
docker-compose up -d
\`\`\`
```
