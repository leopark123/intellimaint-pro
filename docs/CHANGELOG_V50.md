> Historical design/development note, retained for context. Claims of production readiness, performance, ROI and deployment are unverified. For current supported behavior and validation, see the repository README and docs/OSS_READINESS_REPORT.md.

# IntelliMaint Pro 变更日志

## v50 (2025-12-31) - 完整 UI 重构 + 新功能页面

### 🎯 核心目标

完全对标 VITARA 智能维护平台 V2.0 的 UI 设计和功能布局。

---

### 📦 新增通用组件 (12个)

| 组件 | 文件 | 用途 |
|------|------|------|
| `MetricCard` | MetricCard.tsx | 指标卡片（4种颜色主题） |
| `AlertPanel` | AlertPanel.tsx | 告警面板 |
| `EquipmentStatus` | EquipmentStatus.tsx | 设备状态卡片 |
| `ChartCard` | ChartCard.tsx | 图表容器 |
| `StatusBadge` | StatusBadge.tsx | 状态标签 |
| `TrendBadge` | TrendBadge.tsx | 趋势标签 (↑↓) |
| `HeatmapGrid` | HeatmapGrid.tsx | 异常热力图 |
| `AlgorithmList` | AlgorithmList.tsx | 算法状态列表 |
| `EventList` | EventList.tsx | 检测事件列表 |
| `RULCard` | RULCard.tsx | 剩余寿命预测卡片 |
| `WorkOrderCard` | WorkOrderCard.tsx | 工单卡片 |
| `CaseCard` | CaseCard.tsx | 故障案例卡片 |
| `PipelineSteps` | PipelineSteps.tsx | 流水线步骤 |
| `StrategyCard` | StrategyCard.tsx | 策略优化卡片 |
| `ExperimentCard` | ExperimentCard.tsx | A/B测试实验卡片 |

**组件索引**: `src/components/common/index.ts`

---

### 🆕 新增页面 (4个)

#### 1. 异常检测 `/anomaly-detection`
- 4个指标卡片：AI准确率、异常数、误报率、监控点
- 检测算法状态列表
- 异常热力图 (5x12 网格)
- 异常分数时序图
- 多维特征趋势图
- 最近检测事件列表

#### 2. 预测性维护 `/predictive-maintenance`
- 4个指标卡片：维护项、平均RUL、备件需求、预计节省
- 设备RUL预测卡片 (6个设备)
- 智能工单管理 (生成工单、导出报告)
- 工单卡片列表
- 维护策略优化建议 (成本/时间/库存)

#### 3. 持续优化 `/model-optimization`
- 4个指标卡片：准确率、迭代次数、推理速度、训练周期
- 模型性能趋势图
- 训练损失曲线
- A/B测试实验对比
- 训练流水线步骤
- 超参数优化建议

#### 4. 知识图谱 `/knowledge-graph`
- 4个指标卡片：节点数、案例数、方案数、查询数
- 故障搜索框
- 知识图谱可视化 (占位)
- 热门故障类型统计
- 故障案例库

---

### 🔄 页面更新

#### Dashboard 完全重构
对标 VITARA 实时监控中心：
- 4个指标卡片（在线设备、活动警报、待处理工单、系统健康度）
- 振动趋势分析图
- 实时报警面板
- 设备健康状态 (4个设备详细参数)

#### 侧边栏菜单更新
新菜单顺序：
1. 实时监控 (Dashboard)
2. 异常检测 (NEW)
3. 预测性维护 (NEW)
4. 持续优化 (NEW)
5. 知识图谱 (NEW)
6. 数据查询
7. 设备管理
8. 标签管理
9. 告警管理
10. 告警规则
11. 系统健康
12. 审计日志
13. 用户管理
14. 系统设置

---

### 🎨 设计规范

```css
/* 颜色方案 */
--primary: #1A237E;        /* 深蓝 */
--secondary: #00BCD4;      /* 青色 */
--success: #10b981;        /* 绿色 */
--warning: #f59e0b;        /* 橙色 */
--danger: #ef4444;         /* 红色 */

/* 背景色 */
--bg-darker: #0a0a0f;
--bg-dark: #111827;
--bg-card: #1f2937;

/* 边框 */
--border: #374151;
```

---

### 📁 文件变更清单

**新增文件 (19个)**:
```
src/components/common/
├── index.ts
├── ChartCard.tsx
├── StatusBadge.tsx
├── TrendBadge.tsx
├── HeatmapGrid.tsx
├── AlgorithmList.tsx
├── EventList.tsx
├── RULCard.tsx
├── WorkOrderCard.tsx
├── CaseCard.tsx
├── PipelineSteps.tsx
├── StrategyCard.tsx
└── ExperimentCard.tsx

src/pages/
├── AnomalyDetection/index.tsx
├── PredictiveMaintenance/index.tsx
├── ModelOptimization/index.tsx
└── KnowledgeGraph/index.tsx
```

**修改文件 (4个)**:
- `src/router/index.tsx` - 添加新路由
- `src/components/Layout/MainLayout.tsx` - 更新菜单
- `src/pages/Dashboard/index.tsx` - 完全重构
- `package.json` - 版本号 → 0.0.50

---

### 🚀 部署步骤

```bash
# 1. 解压
unzip intellimaint-pro-v50.zip

# 2. 安装依赖
cd intellimaint-pro-v41-fixed
npm install --prefix intellimaint-ui

# 3. 启动后端
dotnet run --project src/Host.Api

# 4. 启动前端
npm run dev --prefix intellimaint-ui

# 5. 访问
http://localhost:3000
```

---

### 📋 后续计划

| 版本 | 内容 |
|------|------|
| v51 | 现有页面深色主题适配 |
| v52 | 响应式布局优化 |
| v53 | 动画效果增强 |
| v54 | 后端模拟数据API |

---

**总组件数**: 15个  
**总页面数**: 17个 (原13 + 新4)  
**代码行数**: ~3000行新增
