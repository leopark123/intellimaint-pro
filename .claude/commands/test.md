---
name: test
description: 运行完整测试套件，包括单元测试、集成测试，并生成覆盖率报告
---

# 测试任务

请按照以下步骤执行完整的测试流程：

## 1. 环境检查

首先检查测试环境：

```bash
# 检查 .NET SDK
dotnet --version

# 检查测试项目
ls tests/
```

## 2. 运行单元测试

```bash
cd tests/Unit
dotnet test --verbosity normal
```

如果测试失败：
1. 分析失败原因
2. 提出修复建议
3. 询问用户是否需要自动修复

## 3. 运行集成测试

```bash
cd tests/Integration
dotnet test --verbosity normal
```

集成测试注意事项：
- 确保 API 服务可访问
- 检查数据库连接
- 验证测试数据准备

## 4. 代码覆盖率（可选）

如果用户需要覆盖率报告：

```bash
dotnet test --collect:"XPlat Code Coverage"
```

## 5. 测试结果分析

分析测试结果并生成报告：

```markdown
# 测试报告

## 概要
- **运行时间**: xxx
- **总测试数**: xxx
- **通过**: xxx ✅
- **失败**: xxx ❌
- **跳过**: xxx ⏭️

## 失败测试详情

### [测试名称]
- **文件**: xxx
- **错误**: xxx
- **建议修复**: xxx

## 覆盖率（如有）

| 模块 | 覆盖率 | 目标 |
|------|--------|------|
| Core | xx% | >90% |
| Infrastructure | xx% | >80% |
| Host.Api | xx% | >80% |

## 建议
- xxx
```

## 6. 修复建议

如果有测试失败，使用 `testing-expert` 的知识：

1. 分析失败原因（断言失败、异常、超时）
2. 检查被测代码是否正确
3. 检查测试本身是否正确
4. 提供修复方案

---

**快捷选项**：

用户可以指定：
- `/test unit` - 仅运行单元测试
- `/test integration` - 仅运行集成测试
- `/test coverage` - 包含覆盖率报告
- `/test fix` - 自动修复失败的测试
