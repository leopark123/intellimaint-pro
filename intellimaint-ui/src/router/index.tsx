// v64: 路由配置 - 使用 React.lazy 实现代码分割

import { Suspense, lazy } from 'react'
import { Routes, Route, Navigate } from 'react-router-dom'
import { Spin } from 'antd'
import MainLayout from '../components/Layout/MainLayout'
import ProtectedRoute from '../components/ProtectedRoute'

// 加载指示器
const PageLoader = () => (
  <div
    style={{
      display: 'flex',
      justifyContent: 'center',
      alignItems: 'center',
      height: '100vh',
      background: 'var(--color-bg-darker)',
    }}
  >
    <Spin size="large" tip="加载中..." />
  </div>
)

// 懒加载页面组件
const Login = lazy(() => import('../pages/Login'))
const Dashboard = lazy(() => import('../pages/Dashboard'))
const DataExplorer = lazy(() => import('../pages/DataExplorer'))
const DeviceManagement = lazy(() => import('../pages/DeviceManagement'))
const TagManagement = lazy(() => import('../pages/TagManagement'))
const AlarmManagement = lazy(() => import('../pages/AlarmManagement'))
const AlarmRules = lazy(() => import('../pages/AlarmRules'))
const AlarmGroups = lazy(() => import('../pages/AlarmGroups'))
const CollectionRules = lazy(() => import('../pages/CollectionRules'))
const CycleAnalysis = lazy(() => import('../pages/CycleAnalysis'))
const SystemHealth = lazy(() => import('../pages/SystemHealth'))
const AuditLog = lazy(() => import('../pages/AuditLog'))
const Settings = lazy(() => import('../pages/Settings'))
const UserManagement = lazy(() => import('../pages/UserManagement'))

// v50: 智能分析页面
const AnomalyDetection = lazy(() => import('../pages/AnomalyDetection'))
const PredictiveMaintenance = lazy(() => import('../pages/PredictiveMaintenance'))
const ModelOptimization = lazy(() => import('../pages/ModelOptimization'))
const KnowledgeGraph = lazy(() => import('../pages/KnowledgeGraph'))

// v60: 健康评估
const HealthAssessment = lazy(() => import('../pages/HealthAssessment'))

// v63: 预测预警
const PredictionAlerts = lazy(() => import('../pages/PredictionAlerts'))

// v64: 电机故障预测
const MotorPrediction = lazy(() => import('../pages/MotorPrediction'))
const MotorConfig = lazy(() => import('../pages/MotorConfig'))

// v65: Edge 配置管理
const EdgeConfig = lazy(() => import('../pages/EdgeConfig'))

export default function AppRouter() {
  return (
    <Suspense fallback={<PageLoader />}>
      <Routes>
        {/* 登录页 - 不需要认证 */}
        <Route path="/login" element={<Login />} />

        {/* 受保护的路由 */}
        <Route
          path="/"
          element={
            <ProtectedRoute>
              <MainLayout />
            </ProtectedRoute>
          }
        >
          <Route index element={<Navigate to="/dashboard" replace />} />

          {/* 智能分析 */}
          <Route path="dashboard" element={<Dashboard />} />
          <Route path="health-assessment" element={<HealthAssessment />} />
          <Route path="motor-prediction" element={<MotorPrediction />} />
          <Route path="motor-config" element={<MotorConfig />} />
          <Route path="prediction-alerts" element={<PredictionAlerts />} />
          <Route path="anomaly-detection" element={<AnomalyDetection />} />
          <Route path="predictive-maintenance" element={<PredictiveMaintenance />} />
          <Route path="model-optimization" element={<ModelOptimization />} />
          <Route path="knowledge-graph" element={<KnowledgeGraph />} />

          {/* 数据中心 */}
          <Route path="data-explorer" element={<DataExplorer />} />
          <Route path="cycle-analysis" element={<CycleAnalysis />} />

          {/* 资产配置 */}
          <Route path="device-management" element={<DeviceManagement />} />
          <Route path="tag-management" element={<TagManagement />} />
          <Route path="collection-rules" element={<CollectionRules />} />

          {/* 告警中心 */}
          <Route path="alarm-management" element={<AlarmManagement />} />
          <Route path="alarm-rules" element={<AlarmRules />} />
          <Route path="alarm-groups" element={<AlarmGroups />} />

          {/* 系统管理 */}
          <Route path="system-health" element={<SystemHealth />} />
          <Route path="edge-config" element={<EdgeConfig />} />
          <Route path="audit-log" element={<AuditLog />} />
          <Route path="settings" element={<Settings />} />
          <Route path="user-management" element={<UserManagement />} />
        </Route>
      </Routes>
    </Suspense>
  )
}
