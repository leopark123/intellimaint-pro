import { Routes, Route, Navigate } from 'react-router-dom'
import MainLayout from '../components/Layout/MainLayout'
import ProtectedRoute from '../components/ProtectedRoute'
import Login from '../pages/Login'
import Dashboard from '../pages/Dashboard'
import DataExplorer from '../pages/DataExplorer'
import DeviceManagement from '../pages/DeviceManagement'
import TagManagement from '../pages/TagManagement'
import AlarmManagement from '../pages/AlarmManagement'
import AlarmRules from '../pages/AlarmRules'
import CollectionRules from '../pages/CollectionRules'
import CycleAnalysis from '../pages/CycleAnalysis'
import SystemHealth from '../pages/SystemHealth'
import AuditLog from '../pages/AuditLog'
import Settings from '../pages/Settings'
import UserManagement from '../pages/UserManagement'
// v50: 新增页面
import AnomalyDetection from '../pages/AnomalyDetection'
import PredictiveMaintenance from '../pages/PredictiveMaintenance'
import ModelOptimization from '../pages/ModelOptimization'
import KnowledgeGraph from '../pages/KnowledgeGraph'

export default function AppRouter() {
  return (
    <Routes>
      {/* 登录页 - 不需要认证 */}
      <Route path="/login" element={<Login />} />
      
      {/* 受保护的路由 */}
      <Route path="/" element={
        <ProtectedRoute>
          <MainLayout />
        </ProtectedRoute>
      }>
        <Route index element={<Navigate to="/dashboard" replace />} />
        <Route path="dashboard" element={<Dashboard />} />
        <Route path="anomaly-detection" element={<AnomalyDetection />} />
        <Route path="predictive-maintenance" element={<PredictiveMaintenance />} />
        <Route path="model-optimization" element={<ModelOptimization />} />
        <Route path="knowledge-graph" element={<KnowledgeGraph />} />
        <Route path="data-explorer" element={<DataExplorer />} />
        <Route path="device-management" element={<DeviceManagement />} />
        <Route path="tag-management" element={<TagManagement />} />
        <Route path="alarm-management" element={<AlarmManagement />} />
        <Route path="alarm-rules" element={<AlarmRules />} />
        <Route path="collection-rules" element={<CollectionRules />} />
        <Route path="cycle-analysis" element={<CycleAnalysis />} />
        <Route path="system-health" element={<SystemHealth />} />
        <Route path="audit-log" element={<AuditLog />} />
        <Route path="settings" element={<Settings />} />
        <Route path="user-management" element={<UserManagement />} />
      </Route>
    </Routes>
  )
}
