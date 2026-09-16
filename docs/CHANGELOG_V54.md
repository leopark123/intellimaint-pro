> Historical design/development note, retained for context. Claims of production readiness, performance, ROI and deployment are unverified. For current supported behavior and validation, see the repository README and docs/OSS_READINESS_REPORT.md.

# IntelliMaint Pro 变更日志

## v54 (2025-01-01) - 主题切换功能

### 🎨 核心功能：深色/浅色主题一键切换

新增完整的主题切换系统，支持深色和浅色两套主题。

---

### ✨ 新增功能

| 功能 | 说明 |
|------|------|
| **🌙/☀️ 一键切换** | Header 右上角 + 登录页右上角 |
| **💾 持久化** | localStorage 保存用户偏好 |
| **🖥️ 系统跟随** | 首次访问自动跟随系统主题 |
| **⚡ 无闪烁** | HTML head 注入脚本，避免白屏闪烁 |
| **🎭 平滑过渡** | 300ms 动画过渡 |

---

### 🔧 全面颜色变量化

将所有页面和组件中的硬编码颜色替换为 CSS 变量：

| 统计项 | 数量 |
|--------|------|
| CSS 变量使用 | 307 处 |
| 浅色主题覆盖规则 | 87 条 |

---

### 📁 新增文件

| 文件 | 说明 |
|------|------|
| `src/hooks/useTheme.ts` | 主题切换 Hook (~80行) |

---

### 🎨 CSS 变量清单

#### 背景色
| 变量 | 深色 | 浅色 |
|------|------|------|
| `--color-bg-darker` | `#0a0a0f` | `#f8fafc` |
| `--color-bg-dark` | `#111827` | `#ffffff` |
| `--color-bg-card` | `#1f2937` | `#ffffff` |
| `--color-bg-elevated` | `#1f2937` | `#f9fafb` |
| `--color-bg-subtle` | `rgba(31,41,55,0.5)` | `rgba(243,244,246,0.8)` |

#### 边框色
| 变量 | 深色 | 浅色 |
|------|------|------|
| `--color-border` | `#374151` | `#e5e7eb` |
| `--color-border-light` | `#4b5563` | `#d1d5db` |

#### 文字色
| 变量 | 深色 | 浅色 |
|------|------|------|
| `--color-text-primary` | `#ffffff` | `#1f2937` |
| `--color-text-secondary` | `#d1d5db` | `#374151` |
| `--color-text-muted` | `#9ca3af` | `#6b7280` |
| `--color-text-dim` | `#6b7280` | `#9ca3af` |

#### 状态色（两个主题略有不同以保证对比度）
| 变量 | 深色 | 浅色 |
|------|------|------|
| `--color-success` | `#10b981` | `#059669` |
| `--color-warning` | `#f59e0b` | `#d97706` |
| `--color-danger` | `#ef4444` | `#dc2626` |
| `--color-info` | `#3b82f6` | `#2563eb` |

---

### 📄 修改文件列表

**核心文件：**
- `global.css` - +450行（变量 + 浅色主题 Ant Design 覆盖 + 滚动条）
- `useTheme.ts` - 新增 ~80行
- `MainLayout.tsx` - 切换按钮 + CSS 变量
- `Login/index.tsx` - 主题支持 + 切换按钮
- `index.html` - 防闪烁脚本

**页面文件（16个）：**
Dashboard, DeviceManagement, TagManagement, DataExplorer, AlarmManagement, AlarmRules, CollectionRules, CycleAnalysis, SystemHealth, AuditLog, UserManagement, Settings, KnowledgeGraph, PredictiveMaintenance, AnomalyDetection, ModelOptimization

**组件文件（15个）：**
MetricCard, AlertPanel, ChartCard, CaseCard, StrategyCard, ExperimentCard, PipelineSteps, AlgorithmList, EquipmentStatus, RULCard, WorkOrderCard, PageHeader, StatusBadge, TrendBadge, HeatmapGrid

---

### ✅ 验收清单

- [x] 深色/浅色一键切换
- [x] 刷新后保持主题选择
- [x] 首次访问跟随系统主题
- [x] 页面加载无闪烁
- [x] Ant Design 组件全适配（87条覆盖规则）
- [x] 侧边栏适配
- [x] 登录页适配
- [x] 表格、卡片、表单适配
- [x] 图表 Tooltip 适配
- [x] 滚动条适配
- [x] 切换有平滑过渡动画
- [x] Layout 背景适配
- [x] 设备卡片背景适配

---

**版本**: 0.0.54  
**日期**: 2025-01-01  
**主题**: 主题切换功能 - 深色/浅色一键切换（完整颜色变量化）
