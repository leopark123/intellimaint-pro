---
name: doc
description: 生成或更新项目文档，包括 API 文档、架构文档、用户指南
---

# 文档任务

请按照以下步骤生成/更新文档：

## 1. 确定文档范围

询问用户需要哪种文档：

- **API 文档** - REST API 端点文档
- **架构文档** - 系统架构设计文档
- **用户指南** - 使用说明文档
- **代码文档** - 代码注释和 README
- **更新日志** - CHANGELOG 更新

## 2. API 文档生成

使用 `docs-expert` 为每个端点生成文档：

### 扫描端点
```bash
# 查找所有端点文件
find src/Host.Api/Endpoints -name "*.cs"
```

### 生成文档结构
```markdown
# API 文档

## 认证
### 登录
### 刷新 Token

## 设备管理
### 获取设备列表
### 创建设备
...
```

### 文档模板
每个端点包含：
- 请求方法和路径
- 请求头
- 请求参数/请求体
- 响应格式
- 错误码
- 示例

## 3. 架构文档生成

### 系统概览
- 技术栈说明
- 模块划分
- 部署架构

### 数据流图
使用 Mermaid 生成数据流图

### 模块说明
每个模块的职责和接口

## 4. 用户指南生成

### 快速开始
- 环境要求
- 安装步骤
- 首次运行

### 功能说明
- 设备管理
- 数据监控
- 告警处理

### 常见问题
收集常见问题并提供解答

## 5. 代码文档

### XML 注释检查
检查公共 API 是否有完整的 XML 文档注释

### README 更新
确保 README 是最新的

## 6. CHANGELOG 更新

根据最近的变更更新 CHANGELOG：

```markdown
## [vXX] - YYYY-MM-DD

### 新增
- xxx

### 变更
- xxx

### 修复
- xxx
```

## 7. 输出

将生成的文档保存到 `docs/` 目录：

```
docs/
├── api/
│   ├── README.md
│   ├── authentication.md
│   ├── devices.md
│   ├── telemetry.md
│   └── alarms.md
├── architecture/
│   ├── overview.md
│   └── data-flow.md
├── user-guide/
│   ├── getting-started.md
│   └── features.md
└── CHANGELOG.md
```

---

**快捷选项**：
- `/doc api` - 仅生成 API 文档
- `/doc arch` - 仅生成架构文档
- `/doc user` - 仅生成用户指南
- `/doc changelog` - 仅更新 CHANGELOG
