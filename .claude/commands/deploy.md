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
