> Historical design/development note, retained for context. Claims of production readiness, performance, ROI and deployment are unverified. For current supported behavior and validation, see the repository README and docs/OSS_READINESS_REPORT.md.

# IntelliMaint Pro 变更日志

## v51 (2025-01-01) - UI 深度优化

### 🔴 P0 修复 - 可用性问题

#### 1. 侧边栏菜单被挡住问题
**问题**: 底部"系统状态"使用 absolute 定位，遮挡了审计日志、用户管理、设置等菜单项

**修复**: 使用 flexbox 布局替代
```tsx
<Sider style={{ display: 'flex', flexDirection: 'column' }}>
  <Logo />           {/* flexShrink: 0 */}
  <Menu />           {/* flex: 1, overflowY: auto */}
  <SystemStatus />   {/* flexShrink: 0 */}
</Sider>
```

#### 2. 表单标签看不清
**修复**: 添加 CSS 覆盖
```css
.ant-form-item-label > label {
  color: #d1d5db !important;
}
```

#### 3. 下拉框选项看不清
**修复**: 完整覆盖下拉面板样式
```css
.ant-select-dropdown {
  background: #1f2937 !important;
  border: 1px solid #374151 !important;
}
.ant-select-item {
  color: #d1d5db !important;
}
```

#### 4. 日期选择器看不清
**修复**: 完整覆盖日历面板样式
```css
.ant-picker-panel-container {
  background: #1f2937 !important;
}
.ant-picker-cell-in-view {
  color: #d1d5db !important;
}
```

---

### 🟡 P1 修复 - 视觉一致性

#### 5. 指标卡片添加左边框
**修改文件**: `MetricCard.tsx`, `Dashboard/index.tsx`
```tsx
// 之前
border: `1px solid ${config.border}`
borderRadius: 12

// 之后
borderLeft: `4px solid ${config.borderLeft}`
borderRadius: '0 12px 12px 0'
```

#### 6. 告警项添加左边框
**修改文件**: `AlertPanel.tsx`, `Dashboard/index.tsx`
```tsx
// 之前
border: `1px solid ${config.border}`
borderRadius: 8

// 之后
borderLeft: `3px solid ${config.color}`
borderRadius: '0 8px 8px 0'
```

#### 7. 按钮深色样式
```css
.ant-btn-default {
  background: #1f2937 !important;
  border-color: #374151 !important;
  color: #d1d5db !important;
}
```

#### 8. 分页器深色样式
```css
.ant-pagination-item {
  background: #1f2937 !important;
  border-color: #374151 !important;
}
```

---

### 🟢 P2 优化 - 动画效果

#### 9. 状态灯脉冲动画增强
```css
@keyframes pulse {
  0%, 100% {
    opacity: 1;
    transform: scale(1);
    box-shadow: 0 0 0 0 rgba(16, 185, 129, 0.7);
  }
  50% {
    opacity: 0.8;
    transform: scale(1.05);
    box-shadow: 0 0 0 6px rgba(16, 185, 129, 0);
  }
}
```

#### 10. 旋转动画
```css
@keyframes spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}

.animate-spin {
  animation: spin 1s linear infinite;
}
```

---

### 📁 文件变更清单

| 文件 | 改动 |
|------|------|
| `src/styles/global.css` | +400行 Ant Design 深色主题覆盖 |
| `src/components/Layout/MainLayout.tsx` | 侧边栏 flex 布局 |
| `src/components/common/MetricCard.tsx` | 左边框样式 |
| `src/components/common/AlertPanel.tsx` | 左边框样式 |
| `src/pages/Dashboard/index.tsx` | 卡片和告警左边框 |

---

### 📋 新增 CSS 覆盖清单 (60+条)

| 组件类别 | 覆盖项 |
|----------|--------|
| 布局 | Layout, Sider, Header |
| 菜单 | Menu, Menu-item |
| 表格 | Table, Thead, Tbody |
| 卡片 | Card, Card-head |
| 模态框 | Modal, Modal-header, Modal-footer |
| 表单 | Form-item-label, Input, TextArea |
| 选择器 | Select, Select-dropdown |
| 日期 | Picker, Picker-panel |
| 按钮 | Btn-default, Btn-primary, Btn-danger |
| 分页 | Pagination |
| 其他 | Tooltip, Popconfirm, Dropdown, Tabs, Badge, Empty |

---

### ✅ 验收标准

- [x] 侧边栏 14 个菜单项全部可见可点击
- [x] 设备管理表单标签清晰 (#d1d5db)
- [x] 告警管理下拉框选项清晰
- [x] 日期选择器深色主题
- [x] 指标卡片左侧彩色边框
- [x] 告警项左侧彩色边框
- [x] 按钮深色背景样式
- [x] 分页器深色背景样式
- [x] 状态灯脉冲动画
- [x] 旋转加载动画

---

### 🚀 部署命令

```bash
# 解压
unzip intellimaint-pro-v51.zip

# 安装依赖
cd intellimaint-pro-v41-fixed
npm install --prefix intellimaint-ui

# 启动后端
dotnet run --project src/Host.Api

# 启动前端
npm run dev --prefix intellimaint-ui

# 访问
http://localhost:3000
```

---

**版本**: 0.0.51  
**日期**: 2025-01-01  
**主题**: UI 深度优化 - 完整深色主题适配
