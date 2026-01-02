# IntelliMaint Pro v46 变更日志

## v46 (2025-12-31) - 采集规则功能

### 新增功能

**采集规则 (Collection Rule)** - 条件触发的数据采集逻辑

类似 PLC 的触发逻辑：
- 当开始条件满足时，自动开始采集指定标签的数据
- 当停止条件满足时，自动停止采集
- 采集的数据保存为"片段 (Segment)"供后续分析

### 条件支持

| 类型 | 说明 | 示例 |
|------|------|------|
| **tag** | 标签值比较 | `CD_F[0] > 5` |
| **duration** | 持续时间 | `持续 3 秒` |

操作符：`>`, `>=`, `<`, `<=`, `=`, `≠`

逻辑组合：`AND`, `OR`

### 采集配置

- **采集标签**: 指定要采集的标签列表
- **前置缓冲**: 开始采集前也保留的数据（秒）
- **后置缓冲**: 停止条件满足后继续采集的时间（秒）

### API 端点

```
采集规则
GET    /api/collection-rules                    # 规则列表
GET    /api/collection-rules/{ruleId}           # 规则详情
POST   /api/collection-rules                    # 创建规则
PUT    /api/collection-rules/{ruleId}           # 更新规则
DELETE /api/collection-rules/{ruleId}           # 删除规则
PUT    /api/collection-rules/{ruleId}/enable    # 启用
PUT    /api/collection-rules/{ruleId}/disable   # 禁用
POST   /api/collection-rules/test               # 测试条件

采集片段
GET    /api/collection-segments                 # 片段列表
GET    /api/collection-segments/{id}            # 片段详情
DELETE /api/collection-segments/{id}            # 删除片段
```

### 数据库变更

**Schema Version: 7**

新增表：
- `collection_rule` - 采集规则配置
- `collection_segment` - 采集片段记录

### 新增文件

**后端**:
- `src/Core/Contracts/CollectionRule.cs` - 实体定义
- `src/Core/Abstractions/Repositories.cs` - 接口 (追加)
- `src/Infrastructure/Sqlite/CollectionRuleRepository.cs` - 规则仓储
- `src/Infrastructure/Sqlite/CollectionSegmentRepository.cs` - 片段仓储
- `src/Host.Api/Endpoints/CollectionRuleEndpoints.cs` - API 端点
- `src/Host.Api/Services/CollectionRuleEngine.cs` - 规则引擎

**前端**:
- `intellimaint-ui/src/types/collectionRule.ts` - 类型定义
- `intellimaint-ui/src/api/collectionRule.ts` - API 调用
- `intellimaint-ui/src/pages/CollectionRules/index.tsx` - 管理页面

### 使用示例

**翻车机工作采集规则**:

```json
{
  "ruleId": "car-dumper-work",
  "name": "翻车机工作采集",
  "deviceId": "CAR_DUMPER_01",
  "startCondition": {
    "logic": "AND",
    "conditions": [
      { "type": "tag", "tagId": "CD_F[0]", "operator": "gt", "value": 5 },
      { "type": "tag", "tagId": "DMP_01_ACTUAL_CURRENT", "operator": "gt", "value": 100 }
    ]
  },
  "stopCondition": {
    "logic": "AND",
    "conditions": [
      { "type": "tag", "tagId": "CD_F[0]", "operator": "lt", "value": 2 },
      { "type": "duration", "seconds": 3 }
    ]
  },
  "collectionConfig": {
    "tagIds": ["CD_F[0]", "DMP_01_ACTUAL_CURRENT", "DMP_02_ACTUAL_CURRENT"],
    "preBufferSeconds": 5,
    "postBufferSeconds": 3
  }
}
```

### 规则引擎工作原理

```
状态机:
  Idle → [开始条件满足] → Collecting → [停止条件满足] → PostBuffer → [缓冲结束] → Idle

数据流:
  遥测数据 → 规则引擎评估 → 触发采集 → 保存片段
```

---

**版本**: v46  
**日期**: 2025-12-31  
**Schema**: v7
