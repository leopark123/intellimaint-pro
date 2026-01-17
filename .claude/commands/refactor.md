---
name: refactor
description: 执行代码重构，改善代码结构而不改变功能
---

# 重构任务

请按照以下步骤执行代码重构：

## 1. 重构范围确定

询问用户重构范围：
- 特定文件/类/方法
- 特定模块
- 全局重构（某种模式）

## 2. 重构前准备

### 确保测试覆盖
```bash
# 运行测试确保现有功能正常
dotnet test
```

⚠️ **重要**: 如果没有足够的测试覆盖，先添加测试再重构！

### 记录当前状态
- 记录要重构的代码
- 记录期望的改进

## 3. 识别重构模式

使用 `code-reviewer` 识别适用的重构：

### 方法级重构
- **提取方法** - 长方法拆分
- **内联方法** - 过度拆分合并
- **重命名** - 改善命名
- **引入参数对象** - 过多参数
- **移除死代码** - 未使用的代码

### 类级重构
- **提取类** - 职责过多
- **内联类** - 类过小
- **移动方法** - 方法放错位置
- **提取接口** - 抽象依赖

### 架构级重构
- **分层重构** - 违反分层
- **依赖倒置** - 依赖具体类
- **引入工厂** - 创建逻辑复杂

## 4. 制定重构计划

```markdown
## 重构计划

### 目标
[重构目标，期望改进什么]

### 步骤
1. [具体重构步骤 1]
2. [具体重构步骤 2]
3. ...

### 风险
- [可能的风险]

### 验证方式
- [如何验证重构成功]
```

## 5. 执行重构

### 小步重构
每次只做一个小的重构：
1. 执行重构
2. 运行测试
3. 确认通过
4. 继续下一步

### 保持功能不变
- 不添加新功能
- 不修复 Bug（除非与重构相关）
- 只改善结构

## 6. 重构后验证

```bash
# 运行所有测试
dotnet test

# 检查是否有编译警告
dotnet build --warnaserror
```

## 7. 代码审查

使用 `code-reviewer` 审查重构结果：
- 是否改善了代码质量？
- 是否引入了新问题？
- 是否符合设计原则？

## 8. 完成报告

```markdown
# 重构报告

## 重构概述
- **范围**: [重构的代码范围]
- **类型**: [方法级/类级/架构级]

## 重构前
\`\`\`csharp
// 原代码
\`\`\`

## 重构后
\`\`\`csharp
// 新代码
\`\`\`

## 改进点
- ✅ [改进 1]
- ✅ [改进 2]

## 验证
- 所有测试通过: ✅
- 无编译警告: ✅

## 变更文件
- `src/xxx.cs` - [变更说明]
```

---

**重构原则**：
1. 有测试才重构
2. 小步快跑
3. 每步都能回退
4. 不混入功能变更
5. 重构是投资，不是债务

**常用快捷选项**：
- `/refactor extract-method` - 提取方法重构
- `/refactor rename` - 重命名重构
- `/refactor cleanup` - 清理死代码

## ⚠️ 质量门禁 (必须满足)

重构必须严格遵循测试驱动流程，每个阶段都有明确检查点：

### 阶段 1: 重构前准备
- [ ] **测试基线** - 运行 `dotnet test`，记录当前测试结果
- [ ] **覆盖率检查** - 被重构代码有测试覆盖（否则先补测试）
- [ ] **代码快照** - 记录重构前代码状态
- [ ] **目标明确** - 清晰描述重构目的和预期改进

### 阶段 2: 重构执行
- [ ] **小步验证** - 每个重构步骤后运行测试
- [ ] **保持绿灯** - 测试必须始终通过
- [ ] **原子提交** - 每步可独立回滚
- [ ] **功能不变** - 不添加新功能，不修复无关 Bug

### 阶段 3: 重构后验证
- [ ] **测试全过** - `dotnet test` 100% 通过
- [ ] **无回归** - 与基线对比，测试数量不减少
- [ ] **构建成功** - `dotnet build --warnaserror` 无警告
- [ ] **代码审查** - 确认改进符合预期

### 测试记录表（必须填写）

```markdown
## 测试执行记录

| 阶段 | 命令 | 测试数 | 通过 | 失败 | 时间 |
|------|------|--------|------|------|------|
| 重构前 | dotnet test | xx | xx | 0 | xx:xx |
| 步骤1后 | dotnet test | xx | xx | 0 | xx:xx |
| 步骤2后 | dotnet test | xx | xx | 0 | xx:xx |
| 重构后 | dotnet test | xx | xx | 0 | xx:xx |
```

### ❌ 不合格示例
```markdown
重构完成:
- 提取了几个方法       ← 没有测试证据
- 应该没问题          ← 没有验证记录
```

### ✅ 合格示例
```markdown
# 重构报告: DeviceRepository

## 重构目标
将 200 行的 QueryDevices 方法拆分为多个职责单一的小方法

## 测试执行记录
| 阶段 | 命令 | 测试数 | 通过 | 失败 |
|------|------|--------|------|------|
| 重构前 | dotnet test | 48 | 48 | 0 |
| 提取 BuildWhereClause 后 | dotnet test | 48 | 48 | 0 |
| 提取 BuildOrderByClause 后 | dotnet test | 48 | 48 | 0 |
| 提取 ExecutePagedQuery 后 | dotnet test | 48 | 48 | 0 |
| 重构后 | dotnet test | 48 | 48 | 0 |

## 重构前
```csharp
// src/Infrastructure/Sqlite/DeviceRepository.cs:45
public async Task<PagedResult<Device>> QueryDevices(DeviceQuery query)
{
    // 200 行复杂逻辑...
}
```

## 重构后
```csharp
public async Task<PagedResult<Device>> QueryDevices(DeviceQuery query)
{
    var whereClause = BuildWhereClause(query);
    var orderBy = BuildOrderByClause(query);
    return await ExecutePagedQuery(whereClause, orderBy, query.Page, query.PageSize);
}

private string BuildWhereClause(DeviceQuery query) { ... }
private string BuildOrderByClause(DeviceQuery query) { ... }
private async Task<PagedResult<Device>> ExecutePagedQuery(...) { ... }
```

## 改进点
- ✅ 方法行数: 200 → 4 + 25 + 20 + 35 (主方法 4 行)
- ✅ 可读性: 显著提升
- ✅ 可测试性: 子方法可独立测试
- ✅ 测试回归: 无 (48/48)

## 验证
- [x] 所有测试通过: 48/48
- [x] 无编译警告
- [x] 代码审查: 无问题
```
