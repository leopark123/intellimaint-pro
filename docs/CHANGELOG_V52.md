# IntelliMaint Pro 变更日志

## v52 (2025-01-01) - 侧边栏分组优化

### 🗂️ 核心改动：菜单分组

将 16 个页面按功能逻辑分为 5 大组：

```
▼ 智能分析 (5)
  ├ 实时监控
  ├ 异常检测
  ├ 预测性维护
  ├ 持续优化
  └ 知识图谱

▼ 数据中心 (2)
  ├ 数据查询
  └ 周期分析 ← 新增显示

▼ 资产配置 (3)
  ├ 设备管理
  ├ 标签管理
  └ 采集规则 ← 新增显示

▼ 告警中心 (2)
  ├ 告警管理
  └ 告警规则

▼ 系统管理 (4)
  ├ 系统健康
  ├ 审计日志
  ├ 用户管理 [Admin]
  └ 系统设置
```

---

### ✨ 新增功能

| 功能 | 说明 |
|------|------|
| **菜单分组** | 5 大功能区，逻辑清晰 |
| **展开/折叠** | SubMenu 支持展开折叠 |
| **状态持久化** | localStorage 保存展开状态 |
| **自动展开** | 访问页面时自动展开对应分组 |
| **权限过滤** | 用户管理仅 Admin 可见 |

---

### 🔧 新增显示的页面

| 页面 | 代码行数 | 功能 |
|------|----------|------|
| **采集规则** | 767 行 | 配置数据采集策略、条件触发 |
| **周期分析** | 501 行 | 工作周期分析、基线学习、异常检测 |

这两个页面之前已实现但未在菜单中显示，现已加入"数据中心"和"资产配置"分组。

---

### 📁 文件改动

| 文件 | 改动说明 |
|------|----------|
| `MainLayout.tsx` | 重构菜单为 menuGroups 分组结构 |
| `global.css` | +80行 SubMenu 深色样式 |

---

### 🎨 菜单样式

```css
/* 分组标题 */
.ant-menu-dark .ant-menu-submenu-title {
  height: 44px;
  font-weight: 500;
  border-radius: 8px;
}

/* 子菜单项 */
.ant-menu-dark .ant-menu-sub .ant-menu-item {
  height: 38px;
  font-size: 13px;
  padding-left: 24px;
  margin-left: 16px;
}

/* 选中状态 */
.ant-menu-dark .ant-menu-sub .ant-menu-item-selected {
  background: #1A237E;
}
```

---

### 📊 分组图标

| 分组 | 图标 | 颜色含义 |
|------|------|----------|
| 智能分析 | Brain | 核心 AI 功能 |
| 数据中心 | Database | 数据查询分析 |
| 资产配置 | Settings2 | 设备点位配置 |
| 告警中心 | Bell | 告警相关 |
| 系统管理 | Shield | 管理功能 |

---

### ✅ 验收标准

- [x] 5 个分组正确显示
- [x] 采集规则、周期分析 显示在菜单
- [x] 分组可展开/折叠
- [x] 当前页面正确高亮
- [x] 展开状态持久化 (localStorage)
- [x] 折叠态弹出菜单正常
- [x] 权限过滤正常 (用户管理仅Admin可见)
- [x] 自动展开当前页面所在分组

---

### 🚀 部署命令

```bash
# 解压
unzip intellimaint-pro-v52.zip

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

**版本**: 0.0.52  
**日期**: 2025-01-01  
**主题**: 侧边栏分组优化 - 16个页面分5组
