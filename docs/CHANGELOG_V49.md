# IntelliMaint Pro 变更日志

## v49 (2025-12-31) - UI 深色主题重构

### 🎨 UI 设计改造

基于参考项目 VITARA 智能维护平台的设计风格，全面改造 UI 为深色工业风格。

#### 设计特点

| 特征 | 说明 |
|------|------|
| **主题** | 深色背景 (#0a0a0f / #111827) |
| **主色** | 深蓝色 (#1A237E) + 青色 (#00BCD4) |
| **图标** | lucide-react 替代 @ant-design/icons |
| **布局** | 固定侧边栏 + 顶部导航栏 |
| **卡片** | 渐变背景 + 圆角 + 悬浮效果 |
| **字体** | Inter 字体 |

### 📦 新增依赖

```json
{
  "framer-motion": "^11.0.0",
  "lucide-react": "^0.300.0"
}
```

### 🆕 新增组件

#### 1. MetricCard 指标卡片
```tsx
<MetricCard
  icon={Server}
  title="在线设备"
  value={48}
  unit="台"
  trend={2}
  color="primary"  // primary | success | warning | danger
/>
```

特点：
- 渐变背景
- 悬浮动画
- 趋势指示器
- 4 种颜色主题

#### 2. AlertPanel 告警面板
```tsx
<AlertPanel
  alerts={[
    { id: 1, equipment: '电机 M-001', message: '振动超标', time: '2分钟前', level: 'warning' }
  ]}
  title="实时报警"
/>
```

特点：
- 分级显示 (critical/warning/info/normal)
- 滑动进入动画
- 参数标签

#### 3. EquipmentStatus 设备状态卡片
```tsx
<EquipmentStatus
  equipment={[
    { id: '1', name: '电机 M-001', status: 'normal', health: 92, vibration: 2.5, temperature: 45 }
  ]}
/>
```

特点：
- 状态指示灯 (脉冲动画)
- 多参数显示
- 剩余寿命预估

### 📝 修改的文件

| 文件 | 修改内容 |
|------|----------|
| `package.json` | 添加 lucide-react, framer-motion |
| `global.css` | 全新深色主题样式 |
| `MainLayout.tsx` | 重构为深色主题布局 |
| `Dashboard/index.tsx` | 使用新组件，深色风格 |
| `Login/index.tsx` | 深色主题登录页 |

### 🆕 新增文件

| 文件 | 说明 |
|------|------|
| `components/common/MetricCard.tsx` | 指标卡片组件 |
| `components/common/AlertPanel.tsx` | 告警面板组件 |
| `components/common/EquipmentStatus.tsx` | 设备状态组件 |

### 🎨 颜色方案

```css
/* 主色调 */
--color-primary: #1A237E;
--color-primary-light: #283593;
--color-secondary: #00BCD4;

/* 背景色 */
--color-bg-darker: #0a0a0f;
--color-bg-dark: #111827;
--color-bg-card: #1f2937;

/* 状态色 */
--color-success: #10b981;
--color-warning: #f59e0b;
--color-danger: #ef4444;
--color-info: #3b82f6;
```

### 📸 UI 预览

**登录页**
- 深色背景 + 渐变光晕
- 居中卡片设计
- Logo 渐变图标

**Dashboard**
- 4 个指标卡片 (在线设备、活动警报、数据采集点、系统健康度)
- 实时趋势图 (深色主题)
- 设备健康状态网格
- 告警面板

**侧边栏**
- 固定定位
- Logo + 系统名称
- 菜单项悬浮效果
- 底部系统状态指示

**顶部导航**
- 折叠按钮
- 系统标题 + 版本标签
- 通知图标 (带红点)
- 用户下拉菜单

### 🔧 使用说明

1. 安装新依赖：
```bash
npm install --prefix intellimaint-ui
```

2. 启动前端：
```bash
npm run dev --prefix intellimaint-ui
```

3. 访问：http://localhost:3000

### 📋 待完成（后续版本）

- [ ] 其他页面深色主题适配
- [ ] 响应式布局优化
- [ ] Framer Motion 动画增强
- [ ] 数据大屏模式

---

**影响范围**: 前端 UI 全面改造
**风险等级**: 低（仅 UI 变化，功能不变）
**需要重启**: 否（热更新）
