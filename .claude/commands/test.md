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

## ⚠️ 质量门禁 (必须满足)

测试执行必须有完整的执行记录和证据：

### 阶段 1: 环境验证
- [ ] **SDK 版本** - `dotnet --version` 输出记录
- [ ] **测试项目** - 测试项目存在且可编译
- [ ] **依赖完整** - `dotnet restore` 成功

### 阶段 2: 测试执行
- [ ] **命令记录** - 完整的 `dotnet test` 命令
- [ ] **输出捕获** - 测试执行的完整输出
- [ ] **结果统计** - 通过/失败/跳过数量

### 阶段 3: 结果分析
- [ ] **失败定位** - 失败测试的具体位置
- [ ] **错误信息** - 完整的错误堆栈
- [ ] **修复建议** - 可行的修复方案

### 测试执行记录表（必须填写）

```markdown
## 测试执行记录

### 环境信息
- SDK: .NET x.x.x
- 测试框架: xUnit x.x.x
- 执行时间: xxxx-xx-xx xx:xx:xx

### 执行结果
| 项目 | 总数 | 通过 | 失败 | 跳过 | 耗时 |
|------|------|------|------|------|------|
| Unit | xx | xx | xx | xx | xx.xs |
| Integration | xx | xx | xx | xx | xx.xs |
| **总计** | xx | xx | xx | xx | xx.xs |
```

### ❌ 不合格示例
```markdown
测试执行完成:
- 运行了测试          ← 没有执行输出
- 有几个失败         ← 没有具体信息
```

### ✅ 合格示例
```markdown
# 测试报告

## 环境信息
```bash
$ dotnet --version
8.0.100
```

## 执行记录
```bash
$ dotnet test tests/Unit --verbosity normal

Starting test execution...
Passed!  - Failed: 0, Passed: 24, Skipped: 0, Total: 24, Duration: 3.2s
```

## 结果汇总
| 项目 | 总数 | 通过 | 失败 | 跳过 | 耗时 |
|------|------|------|------|------|------|
| Unit | 24 | 24 | 0 | 0 | 3.2s |
| Integration | 12 | 11 | 1 | 0 | 8.5s |
| **总计** | 36 | 35 | 1 | 0 | 11.7s |

## 失败测试分析

### 1. DeviceApiTests.DeleteDevice_WithInvalidId_Returns404
- **位置**: `tests/Integration/DeviceApiTests.cs:89`
- **错误**: `Expected 404, got 400`
- **原因**: API 返回了 BadRequest 而非 NotFound
- **建议**: 检查 `DeviceEndpoints.cs:120` 的验证逻辑

## 覆盖率
| 模块 | 行覆盖率 | 分支覆盖率 |
|------|----------|------------|
| Infrastructure | 78% | 65% |
| Application | 82% | 71% |
```
