# Batch 11: React 项目脚手架 - ChatGPT 开发指令

## 项目背景

你正在为 **IntelliMaint Pro** 工业数据采集平台创建前端 UI。

### 后端 API 已完成
- `GET /api/telemetry/query` - 查询历史数据
- `GET /api/telemetry/latest` - 获取最新值
- `GET /api/telemetry/tags` - 获取标签列表
- `GET /api/telemetry/aggregate` - 聚合查询

### 本批次目标
创建 React 项目脚手架，包含：
1. 项目初始化（Vite + React + TypeScript）
2. 路由配置
3. 布局组件
4. API 客户端封装
5. 基础页面结构

---

## 技术栈（必须遵守）

| 技术 | 版本 | 说明 |
|------|------|------|
| React | 18.x | 核心框架 |
| TypeScript | 5.x | 类型安全 |
| Vite | 5.x | 构建工具 |
| Ant Design | 5.x | UI 组件库 |
| React Router | 6.x | 路由 |
| Axios | 1.x | HTTP 客户端 |
| ECharts | 5.x | 图表（下一批次使用） |

---

## 项目结构

```
intellimaint-ui/
├── index.html
├── package.json
├── tsconfig.json
├── tsconfig.node.json
├── vite.config.ts
├── src/
│   ├── main.tsx                 # 入口
│   ├── App.tsx                  # 根组件
│   ├── vite-env.d.ts
│   │
│   ├── api/                     # API 客户端
│   │   ├── client.ts            # Axios 实例
│   │   └── telemetry.ts         # 遥测 API
│   │
│   ├── components/              # 公共组件
│   │   └── Layout/
│   │       └── MainLayout.tsx   # 主布局
│   │
│   ├── pages/                   # 页面
│   │   ├── Dashboard/
│   │   │   └── index.tsx        # 仪表板页面
│   │   ├── DataExplorer/
│   │   │   └── index.tsx        # 数据查询页面
│   │   ├── DeviceManagement/
│   │   │   └── index.tsx        # 设备管理页面（占位）
│   │   └── Settings/
│   │       └── index.tsx        # 系统设置页面（占位）
│   │
│   ├── router/
│   │   └── index.tsx            # 路由配置
│   │
│   ├── types/                   # TypeScript 类型
│   │   └── telemetry.ts
│   │
│   └── styles/
│       └── global.css           # 全局样式
```

---

## 文件内容

### 1. `package.json`

```json
{
  "name": "intellimaint-ui",
  "private": true,
  "version": "1.0.0",
  "type": "module",
  "scripts": {
    "dev": "vite",
    "build": "tsc && vite build",
    "preview": "vite preview"
  },
  "dependencies": {
    "react": "^18.2.0",
    "react-dom": "^18.2.0",
    "react-router-dom": "^6.20.0",
    "antd": "^5.12.0",
    "@ant-design/icons": "^5.2.6",
    "axios": "^1.6.2",
    "dayjs": "^1.11.10"
  },
  "devDependencies": {
    "@types/react": "^18.2.43",
    "@types/react-dom": "^18.2.17",
    "@vitejs/plugin-react": "^4.2.1",
    "typescript": "^5.3.2",
    "vite": "^5.0.8"
  }
}
```

### 2. `vite.config.ts`

```typescript
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 3000,
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true
      }
    }
  }
})
```

### 3. `tsconfig.json`

```json
{
  "compilerOptions": {
    "target": "ES2020",
    "useDefineForClassFields": true,
    "lib": ["ES2020", "DOM", "DOM.Iterable"],
    "module": "ESNext",
    "skipLibCheck": true,
    "moduleResolution": "bundler",
    "allowImportingTsExtensions": true,
    "resolveJsonModule": true,
    "isolatedModules": true,
    "noEmit": true,
    "jsx": "react-jsx",
    "strict": true,
    "noUnusedLocals": true,
    "noUnusedParameters": true,
    "noFallthroughCasesInSwitch": true,
    "baseUrl": ".",
    "paths": {
      "@/*": ["src/*"]
    }
  },
  "include": ["src"],
  "references": [{ "path": "./tsconfig.node.json" }]
}
```

### 4. `tsconfig.node.json`

```json
{
  "compilerOptions": {
    "composite": true,
    "skipLibCheck": true,
    "module": "ESNext",
    "moduleResolution": "bundler",
    "allowSyntheticDefaultImports": true
  },
  "include": ["vite.config.ts"]
}
```

### 5. `index.html`

```html
<!DOCTYPE html>
<html lang="zh-CN">
  <head>
    <meta charset="UTF-8" />
    <link rel="icon" type="image/svg+xml" href="/vite.svg" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>IntelliMaint Pro</title>
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/main.tsx"></script>
  </body>
</html>
```

### 6. `src/main.tsx`

```typescript
import React from 'react'
import ReactDOM from 'react-dom/client'
import { ConfigProvider } from 'antd'
import zhCN from 'antd/locale/zh_CN'
import App from './App'
import './styles/global.css'

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <ConfigProvider locale={zhCN}>
      <App />
    </ConfigProvider>
  </React.StrictMode>,
)
```

### 7. `src/App.tsx`

```typescript
import { BrowserRouter } from 'react-router-dom'
import AppRouter from './router'

function App() {
  return (
    <BrowserRouter>
      <AppRouter />
    </BrowserRouter>
  )
}

export default App
```

### 8. `src/vite-env.d.ts`

```typescript
/// <reference types="vite/client" />
```

### 9. `src/styles/global.css`

```css
* {
  margin: 0;
  padding: 0;
  box-sizing: border-box;
}

html, body, #root {
  height: 100%;
}

body {
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
}
```

### 10. `src/types/telemetry.ts`

```typescript
// API 响应包装
export interface ApiResponse<T> {
  success: boolean
  data: T | null
  error: string | null
  timestamp: number
}

// 遥测数据点
export interface TelemetryDataPoint {
  deviceId: string
  tagId: string
  ts: number
  value: number | string | boolean | null
  valueType: string
  quality: number
  unit: string | null
}

// 标签信息
export interface TagInfo {
  deviceId: string
  tagId: string
  valueType: string
  unit: string | null
  lastTs: number | null
  pointCount: number
}

// 聚合结果
export interface AggregateResult {
  ts: number
  value: number
  count: number
}

// 查询参数
export interface TelemetryQueryParams {
  deviceId?: string
  tagId?: string
  startTs?: number
  endTs?: number
  limit?: number
}

// 聚合查询参数
export interface AggregateQueryParams {
  deviceId: string
  tagId: string
  startTs: number
  endTs: number
  intervalMs?: number
  function?: 'avg' | 'min' | 'max' | 'sum' | 'count'
}
```

### 11. `src/api/client.ts`

```typescript
import axios from 'axios'

const apiClient = axios.create({
  baseURL: '/api',
  timeout: 30000,
  headers: {
    'Content-Type': 'application/json'
  }
})

// 请求拦截器
apiClient.interceptors.request.use(
  (config) => {
    // 可以在这里添加 token 等
    return config
  },
  (error) => {
    return Promise.reject(error)
  }
)

// 响应拦截器
apiClient.interceptors.response.use(
  (response) => {
    return response.data
  },
  (error) => {
    console.error('API Error:', error)
    return Promise.reject(error)
  }
)

export default apiClient
```

### 12. `src/api/telemetry.ts`

```typescript
import apiClient from './client'
import type {
  ApiResponse,
  TelemetryDataPoint,
  TagInfo,
  AggregateResult,
  TelemetryQueryParams,
  AggregateQueryParams
} from '../types/telemetry'

// 查询历史数据
export async function queryTelemetry(params: TelemetryQueryParams): Promise<ApiResponse<TelemetryDataPoint[]>> {
  return apiClient.get('/telemetry/query', { params })
}

// 获取最新值
export async function getLatestTelemetry(deviceId?: string, tagId?: string): Promise<ApiResponse<TelemetryDataPoint[]>> {
  return apiClient.get('/telemetry/latest', { params: { deviceId, tagId } })
}

// 获取标签列表
export async function getTags(): Promise<ApiResponse<TagInfo[]>> {
  return apiClient.get('/telemetry/tags')
}

// 聚合查询
export async function aggregateTelemetry(params: AggregateQueryParams): Promise<ApiResponse<AggregateResult[]>> {
  return apiClient.get('/telemetry/aggregate', { params })
}
```

### 13. `src/router/index.tsx`

```typescript
import { Routes, Route, Navigate } from 'react-router-dom'
import MainLayout from '../components/Layout/MainLayout'
import Dashboard from '../pages/Dashboard'
import DataExplorer from '../pages/DataExplorer'
import DeviceManagement from '../pages/DeviceManagement'
import Settings from '../pages/Settings'

export default function AppRouter() {
  return (
    <Routes>
      <Route path="/" element={<MainLayout />}>
        <Route index element={<Navigate to="/dashboard" replace />} />
        <Route path="dashboard" element={<Dashboard />} />
        <Route path="data-explorer" element={<DataExplorer />} />
        <Route path="device-management" element={<DeviceManagement />} />
        <Route path="settings" element={<Settings />} />
      </Route>
    </Routes>
  )
}
```

### 14. `src/components/Layout/MainLayout.tsx`

```typescript
import { useState } from 'react'
import { Outlet, useNavigate, useLocation } from 'react-router-dom'
import { Layout, Menu, theme } from 'antd'
import {
  DashboardOutlined,
  LineChartOutlined,
  ClusterOutlined,
  SettingOutlined,
  MenuFoldOutlined,
  MenuUnfoldOutlined
} from '@ant-design/icons'

const { Header, Sider, Content } = Layout

const menuItems = [
  {
    key: '/dashboard',
    icon: <DashboardOutlined />,
    label: '实时监控'
  },
  {
    key: '/data-explorer',
    icon: <LineChartOutlined />,
    label: '数据查询'
  },
  {
    key: '/device-management',
    icon: <ClusterOutlined />,
    label: '设备管理'
  },
  {
    key: '/settings',
    icon: <SettingOutlined />,
    label: '系统设置'
  }
]

export default function MainLayout() {
  const [collapsed, setCollapsed] = useState(false)
  const navigate = useNavigate()
  const location = useLocation()
  const { token: { colorBgContainer } } = theme.useToken()

  const handleMenuClick = ({ key }: { key: string }) => {
    navigate(key)
  }

  return (
    <Layout style={{ minHeight: '100vh' }}>
      <Sider trigger={null} collapsible collapsed={collapsed}>
        <div style={{
          height: 64,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          color: '#fff',
          fontSize: collapsed ? 14 : 18,
          fontWeight: 'bold',
          borderBottom: '1px solid rgba(255,255,255,0.1)'
        }}>
          {collapsed ? 'IM' : 'IntelliMaint'}
        </div>
        <Menu
          theme="dark"
          mode="inline"
          selectedKeys={[location.pathname]}
          items={menuItems}
          onClick={handleMenuClick}
        />
      </Sider>
      <Layout>
        <Header style={{
          padding: '0 16px',
          background: colorBgContainer,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          boxShadow: '0 1px 4px rgba(0,0,0,0.1)'
        }}>
          <div style={{ display: 'flex', alignItems: 'center' }}>
            {collapsed ? (
              <MenuUnfoldOutlined
                style={{ fontSize: 18, cursor: 'pointer' }}
                onClick={() => setCollapsed(false)}
              />
            ) : (
              <MenuFoldOutlined
                style={{ fontSize: 18, cursor: 'pointer' }}
                onClick={() => setCollapsed(true)}
              />
            )}
            <span style={{ marginLeft: 16, fontSize: 16, fontWeight: 500 }}>
              工业数据采集与监控平台
            </span>
          </div>
          <div>
            <span style={{ color: '#666' }}>v1.0.0</span>
          </div>
        </Header>
        <Content style={{
          margin: 16,
          padding: 24,
          background: colorBgContainer,
          borderRadius: 8,
          minHeight: 280,
          overflow: 'auto'
        }}>
          <Outlet />
        </Content>
      </Layout>
    </Layout>
  )
}
```

### 15. `src/pages/Dashboard/index.tsx`

```typescript
import { useEffect, useState } from 'react'
import { Card, Row, Col, Statistic, Table, Tag, Spin, message } from 'antd'
import { ReloadOutlined, CheckCircleOutlined, ClockCircleOutlined } from '@ant-design/icons'
import { getLatestTelemetry, getTags } from '../../api/telemetry'
import type { TelemetryDataPoint, TagInfo } from '../../types/telemetry'

export default function Dashboard() {
  const [loading, setLoading] = useState(true)
  const [latestData, setLatestData] = useState<TelemetryDataPoint[]>([])
  const [tags, setTags] = useState<TagInfo[]>([])
  const [lastUpdate, setLastUpdate] = useState<Date | null>(null)

  const fetchData = async () => {
    try {
      setLoading(true)
      const [latestRes, tagsRes] = await Promise.all([
        getLatestTelemetry(),
        getTags()
      ])
      
      if (latestRes.success && latestRes.data) {
        setLatestData(latestRes.data)
      }
      if (tagsRes.success && tagsRes.data) {
        setTags(tagsRes.data)
      }
      setLastUpdate(new Date())
    } catch (error) {
      message.error('获取数据失败')
      console.error(error)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    fetchData()
    // 每 5 秒刷新一次
    const interval = setInterval(fetchData, 5000)
    return () => clearInterval(interval)
  }, [])

  const columns = [
    {
      title: '设备',
      dataIndex: 'deviceId',
      key: 'deviceId'
    },
    {
      title: '标签',
      dataIndex: 'tagId',
      key: 'tagId'
    },
    {
      title: '当前值',
      dataIndex: 'value',
      key: 'value',
      render: (value: number | string | boolean | null) => (
        <span style={{ fontWeight: 'bold', fontSize: 16 }}>{String(value)}</span>
      )
    },
    {
      title: '类型',
      dataIndex: 'valueType',
      key: 'valueType',
      render: (type: string) => <Tag color="blue">{type}</Tag>
    },
    {
      title: '质量',
      dataIndex: 'quality',
      key: 'quality',
      render: (quality: number) => (
        quality === 192 ? (
          <Tag color="success" icon={<CheckCircleOutlined />}>Good</Tag>
        ) : (
          <Tag color="warning">Bad ({quality})</Tag>
        )
      )
    },
    {
      title: '时间',
      dataIndex: 'ts',
      key: 'ts',
      render: (ts: number) => new Date(ts).toLocaleString('zh-CN')
    }
  ]

  const totalPoints = tags.reduce((sum, t) => sum + t.pointCount, 0)

  return (
    <div>
      <Row gutter={16} style={{ marginBottom: 16 }}>
        <Col span={6}>
          <Card>
            <Statistic
              title="在线设备"
              value={new Set(tags.map(t => t.deviceId)).size}
              prefix={<CheckCircleOutlined style={{ color: '#52c41a' }} />}
            />
          </Card>
        </Col>
        <Col span={6}>
          <Card>
            <Statistic
              title="监控点位"
              value={tags.length}
            />
          </Card>
        </Col>
        <Col span={6}>
          <Card>
            <Statistic
              title="总数据量"
              value={totalPoints}
            />
          </Card>
        </Col>
        <Col span={6}>
          <Card>
            <Statistic
              title="最后更新"
              value={lastUpdate ? lastUpdate.toLocaleTimeString('zh-CN') : '-'}
              prefix={<ClockCircleOutlined />}
            />
          </Card>
        </Col>
      </Row>

      <Card
        title="实时数据"
        extra={
          <ReloadOutlined
            spin={loading}
            style={{ cursor: 'pointer' }}
            onClick={fetchData}
          />
        }
      >
        <Spin spinning={loading}>
          <Table
            dataSource={latestData}
            columns={columns}
            rowKey={(record) => `${record.deviceId}-${record.tagId}`}
            pagination={false}
            size="middle"
          />
        </Spin>
      </Card>
    </div>
  )
}
```

### 16. `src/pages/DataExplorer/index.tsx`

```typescript
import { useState } from 'react'
import { Card, Form, Select, DatePicker, Button, Table, Space, message, InputNumber } from 'antd'
import { SearchOutlined } from '@ant-design/icons'
import dayjs from 'dayjs'
import { queryTelemetry, getTags } from '../../api/telemetry'
import type { TelemetryDataPoint, TagInfo, TelemetryQueryParams } from '../../types/telemetry'
import { useEffect } from 'react'

const { RangePicker } = DatePicker

export default function DataExplorer() {
  const [form] = Form.useForm()
  const [loading, setLoading] = useState(false)
  const [tags, setTags] = useState<TagInfo[]>([])
  const [data, setData] = useState<TelemetryDataPoint[]>([])

  useEffect(() => {
    loadTags()
  }, [])

  const loadTags = async () => {
    try {
      const res = await getTags()
      if (res.success && res.data) {
        setTags(res.data)
      }
    } catch (error) {
      console.error(error)
    }
  }

  const handleSearch = async () => {
    try {
      const values = await form.validateFields()
      setLoading(true)

      const params: TelemetryQueryParams = {
        limit: values.limit || 1000
      }

      if (values.deviceId) {
        params.deviceId = values.deviceId
      }
      if (values.tagId) {
        params.tagId = values.tagId
      }
      if (values.timeRange && values.timeRange.length === 2) {
        params.startTs = values.timeRange[0].valueOf()
        params.endTs = values.timeRange[1].valueOf()
      }

      const res = await queryTelemetry(params)
      if (res.success && res.data) {
        setData(res.data)
        message.success(`查询到 ${res.data.length} 条记录`)
      } else {
        message.warning('未查询到数据')
        setData([])
      }
    } catch (error) {
      message.error('查询失败')
      console.error(error)
    } finally {
      setLoading(false)
    }
  }

  const columns = [
    {
      title: '时间',
      dataIndex: 'ts',
      key: 'ts',
      width: 180,
      render: (ts: number) => new Date(ts).toLocaleString('zh-CN')
    },
    {
      title: '设备',
      dataIndex: 'deviceId',
      key: 'deviceId',
      width: 120
    },
    {
      title: '标签',
      dataIndex: 'tagId',
      key: 'tagId',
      width: 150
    },
    {
      title: '值',
      dataIndex: 'value',
      key: 'value',
      width: 120,
      render: (value: number | string | boolean | null) => (
        <span style={{ fontWeight: 'bold' }}>{String(value)}</span>
      )
    },
    {
      title: '类型',
      dataIndex: 'valueType',
      key: 'valueType',
      width: 100
    },
    {
      title: '质量',
      dataIndex: 'quality',
      key: 'quality',
      width: 80
    }
  ]

  const deviceOptions = [...new Set(tags.map(t => t.deviceId))].map(d => ({
    label: d,
    value: d
  }))

  const tagOptions = tags.map(t => ({
    label: `${t.tagId} (${t.deviceId})`,
    value: t.tagId
  }))

  return (
    <div>
      <Card title="查询条件" style={{ marginBottom: 16 }}>
        <Form form={form} layout="inline">
          <Form.Item name="deviceId" label="设备">
            <Select
              style={{ width: 150 }}
              placeholder="全部"
              allowClear
              options={deviceOptions}
            />
          </Form.Item>
          <Form.Item name="tagId" label="标签">
            <Select
              style={{ width: 200 }}
              placeholder="全部"
              allowClear
              showSearch
              options={tagOptions}
            />
          </Form.Item>
          <Form.Item name="timeRange" label="时间范围">
            <RangePicker
              showTime
              format="YYYY-MM-DD HH:mm:ss"
              presets={[
                { label: '最近1小时', value: [dayjs().subtract(1, 'hour'), dayjs()] },
                { label: '最近24小时', value: [dayjs().subtract(24, 'hour'), dayjs()] },
                { label: '最近7天', value: [dayjs().subtract(7, 'day'), dayjs()] }
              ]}
            />
          </Form.Item>
          <Form.Item name="limit" label="限制条数" initialValue={1000}>
            <InputNumber min={1} max={10000} style={{ width: 100 }} />
          </Form.Item>
          <Form.Item>
            <Button
              type="primary"
              icon={<SearchOutlined />}
              onClick={handleSearch}
              loading={loading}
            >
              查询
            </Button>
          </Form.Item>
        </Form>
      </Card>

      <Card title={`查询结果 (${data.length} 条)`}>
        <Table
          dataSource={data}
          columns={columns}
          rowKey={(record) => `${record.deviceId}-${record.tagId}-${record.ts}`}
          pagination={{
            pageSize: 50,
            showSizeChanger: true,
            showQuickJumper: true,
            showTotal: (total) => `共 ${total} 条`
          }}
          size="small"
          scroll={{ y: 400 }}
          loading={loading}
        />
      </Card>
    </div>
  )
}
```

### 17. `src/pages/DeviceManagement/index.tsx`

```typescript
import { Card, Result } from 'antd'
import { ClusterOutlined } from '@ant-design/icons'

export default function DeviceManagement() {
  return (
    <Card>
      <Result
        icon={<ClusterOutlined />}
        title="设备管理"
        subTitle="此功能正在开发中..."
      />
    </Card>
  )
}
```

### 18. `src/pages/Settings/index.tsx`

```typescript
import { Card, Result } from 'antd'
import { SettingOutlined } from '@ant-design/icons'

export default function Settings() {
  return (
    <Card>
      <Result
        icon={<SettingOutlined />}
        title="系统设置"
        subTitle="此功能正在开发中..."
      />
    </Card>
  )
}
```

---

## 运行说明

### 1. 创建项目
```bash
cd E:\DAYDAYUP\intellimaint-pro-v19\intellimaint-pro
mkdir intellimaint-ui
cd intellimaint-ui
```

### 2. 创建所有文件
按上面的结构创建所有文件

### 3. 安装依赖
```bash
npm install
```

### 4. 启动开发服务器
```bash
npm run dev
```

### 5. 确保后端 API 运行
在另一个终端：
```bash
cd E:\DAYDAYUP\intellimaint-pro-v19\intellimaint-pro
dotnet run --project src/Host.Api
```

### 6. 访问
浏览器打开：http://localhost:3000

---

## 输出要求

请提供上述所有文件的完整代码，按照目录结构组织。

每个文件必须：
- 包含完整的 import 语句
- 可直接复制使用
- 不要省略任何代码

---

## 重要提醒

1. **Vite 代理配置**：API 请求 `/api/*` 会代理到 `http://localhost:5000`
2. **Ant Design 中文**：已配置 `zhCN` 语言包
3. **自动刷新**：Dashboard 页面每 5 秒自动刷新数据
4. **响应式布局**：使用 Ant Design 的 Row/Col 实现
