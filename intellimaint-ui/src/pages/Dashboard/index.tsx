import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Select, Empty, message, Skeleton } from 'antd'
import { Activity, AlertTriangle, Wrench, TrendingUp, Thermometer, Zap, Heart, RefreshCw, Cpu, ChevronRight } from 'lucide-react'
import {
  LineChart,
  Line,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip as RechartsTooltip,
  ResponsiveContainer,
  Legend
} from 'recharts'
import { useNavigate } from 'react-router-dom'
import { logError } from '../../utils/logger'
import { getDevices } from '../../api/device'
import { getAlarmStats, queryAlarms } from '../../api/alarm'
import { getAllDevicesHealth, type HealthScore } from '../../api/healthAssessment'
import { telemetrySignalR, TelemetryDataPoint } from '../../api/signalr'
import { getAllDiagnoses } from '../../api/motor'
import type { Device } from '../../types/device'
import type { Alarm } from '../../types/alarm'
import type { MotorDiagnosisResult } from '../../types/motor'

// 图表颜色
const lineColors = ['#3b82f6', '#10b981', '#00BCD4', '#f59e0b', '#ef4444', '#8b5cf6']

// 指标卡片颜色配置 - 左边框样式
const cardColors = {
  primary: {
    gradient: 'linear-gradient(90deg, rgba(26, 35, 126, 0.15) 0%, rgba(26, 35, 126, 0.02) 100%)',
    borderLeft: '#1A237E',
    iconBg: 'rgba(26, 35, 126, 0.2)',
    iconColor: '#00BCD4'
  },
  warning: {
    gradient: 'linear-gradient(90deg, rgba(245, 158, 11, 0.15) 0%, rgba(245, 158, 11, 0.02) 100%)',
    borderLeft: '#f59e0b',
    iconBg: 'rgba(245, 158, 11, 0.2)',
    iconColor: '#f59e0b'
  },
  success: {
    gradient: 'linear-gradient(90deg, rgba(16, 185, 129, 0.15) 0%, rgba(16, 185, 129, 0.02) 100%)',
    borderLeft: '#10b981',
    iconBg: 'rgba(16, 185, 129, 0.2)',
    iconColor: '#10b981'
  }
}

function formatTime(ts: number): string {
  return new Date(ts).toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit', second: '2-digit' })
}

interface RealtimeDataPoint {
  time: string
  ts: number
  [key: string]: string | number
}


// 告警严重程度配置 (1=Info, 2=Warning, 3=Alarm, 4=Critical)
const severityConfig: Record<number, { color: string; bg: string; border: string; label: string }> = {
  1: { color: '#3b82f6', bg: 'rgba(59, 130, 246, 0.1)', border: 'rgba(59, 130, 246, 0.3)', label: '提示' },
  2: { color: '#f59e0b', bg: 'rgba(245, 158, 11, 0.1)', border: 'rgba(245, 158, 11, 0.3)', label: '警告' },
  3: { color: '#f97316', bg: 'rgba(249, 115, 22, 0.1)', border: 'rgba(249, 115, 22, 0.3)', label: '告警' },
  4: { color: '#ef4444', bg: 'rgba(239, 68, 68, 0.1)', border: 'rgba(239, 68, 68, 0.3)', label: '紧急' }
}

// 健康等级配置
const healthLevelConfig: Record<string, { color: string; statusClass: string }> = {
  'Healthy': { color: '#10b981', statusClass: 'online' },
  'Attention': { color: '#f59e0b', statusClass: 'warning' },
  'Warning': { color: '#f97316', statusClass: 'warning' },
  'Critical': { color: '#ef4444', statusClass: 'offline' }
}

// 格式化相对时间
function formatRelativeTime(ts: number): string {
  const now = Date.now()
  const diff = now - ts
  const minutes = Math.floor(diff / 60000)
  const hours = Math.floor(diff / 3600000)
  const days = Math.floor(diff / 86400000)

  if (minutes < 1) return '刚刚'
  if (minutes < 60) return `${minutes}分钟前`
  if (hours < 24) return `${hours}小时前`
  return `${days}天前`
}

// 骨架屏组件 - 指标卡片
function MetricCardSkeleton() {
  return (
    <div style={{
      background: 'var(--color-bg-subtle)',
      borderLeft: '4px solid var(--color-border)',
      borderRadius: '0 12px 12px 0',
      padding: 24
    }}>
      <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', marginBottom: 16 }}>
        <Skeleton.Avatar active size={48} shape="square" style={{ borderRadius: 10 }} />
        <Skeleton.Button active size="small" style={{ width: 60 }} />
      </div>
      <Skeleton active paragraph={false} title={{ width: 80 }} style={{ marginBottom: 8 }} />
      <Skeleton active paragraph={false} title={{ width: 100 }} />
    </div>
  )
}

// 骨架屏组件 - 告警项
function AlarmItemSkeleton() {
  return (
    <div style={{
      background: 'var(--color-bg-subtle)',
      borderLeft: '3px solid var(--color-border)',
      borderRadius: '0 8px 8px 0',
      padding: 16,
      marginBottom: 12
    }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 8 }}>
        <Skeleton.Input active size="small" style={{ width: 120 }} />
        <Skeleton.Input active size="small" style={{ width: 60 }} />
      </div>
      <Skeleton active paragraph={{ rows: 1, width: '90%' }} title={false} />
      <div style={{ display: 'flex', gap: 8, marginTop: 8 }}>
        <Skeleton.Button active size="small" style={{ width: 50 }} />
        <Skeleton.Button active size="small" style={{ width: 60 }} />
      </div>
    </div>
  )
}

// 骨架屏组件 - 设备健康卡片
function DeviceHealthSkeleton() {
  return (
    <div style={{
      background: 'var(--color-bg-subtle)',
      border: '1px solid var(--color-border)',
      borderRadius: 8,
      padding: 16
    }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 12 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <Skeleton.Avatar active size={12} />
          <Skeleton.Input active size="small" style={{ width: 100 }} />
        </div>
        <Skeleton.Input active size="small" style={{ width: 60 }} />
      </div>
      <div style={{ marginBottom: 12, padding: 12, background: 'var(--color-bg-dark)', borderRadius: 6 }}>
        <Skeleton active paragraph={{ rows: 1 }} title={{ width: 80 }} />
      </div>
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: 8 }}>
        {[1, 2, 3].map(i => (
          <Skeleton key={i} active paragraph={false} title={{ width: 50 }} />
        ))}
      </div>
    </div>
  )
}

export default function Dashboard() {
  const navigate = useNavigate()
  // 分段加载状态
  const [metricsLoading, setMetricsLoading] = useState(true)
  const [alarmsLoading, setAlarmsLoading] = useState(true)
  const [healthLoading, setHealthLoading] = useState(true)
  const [refreshing, setRefreshing] = useState(false)
  const [devices, setDevices] = useState<Device[]>([])
  const [openAlarmCount, setOpenAlarmCount] = useState(0)
  const [selectedDevice, setSelectedDevice] = useState<string | null>(null)
  const [realtimeData, setRealtimeData] = useState<RealtimeDataPoint[]>([])
  const [availableTags, setAvailableTags] = useState<string[]>([])
  const [selectedTags, setSelectedTags] = useState<string[]>([])
  const availableTagsRef = useRef<string[]>([])
  const [connected, setConnected] = useState(false)
  // 真实数据状态
  const [healthScores, setHealthScores] = useState<HealthScore[]>([])
  const [recentAlarms, setRecentAlarms] = useState<Alarm[]>([])
  // 实时遥测数据缓存（用于设备健康卡片显示）
  const [latestTelemetry, setLatestTelemetry] = useState<Record<string, Record<string, number>>>({})
  // v64: 电机诊断数据
  const [motorDiagnoses, setMotorDiagnoses] = useState<MotorDiagnosisResult[]>([])

  // 加载数据 - 分段加载，优先显示关键数据
  const loadData = useCallback(async (isRefresh = false) => {
    if (isRefresh) {
      setRefreshing(true)
    } else {
      setMetricsLoading(true)
      setAlarmsLoading(true)
      setHealthLoading(true)
    }

    try {
      // 第一阶段：加载设备列表和告警统计（关键指标）
      const [deviceList, alarmStatsRes] = await Promise.all([
        getDevices(),
        getAlarmStats().catch(() => ({ success: false, data: { openCount: 0 } }))
      ])

      setDevices(deviceList)
      if (deviceList.length > 0 && !selectedDevice) {
        setSelectedDevice(deviceList[0].deviceId)
      }

      if (alarmStatsRes.success && alarmStatsRes.data) {
        setOpenAlarmCount(alarmStatsRes.data.openCount)
      }
      setMetricsLoading(false)

      // 第二阶段：加载告警列表
      const alarmsRes = await queryAlarms({ status: 0, limit: 10 }).catch(() => ({ success: false, data: { items: [] } }))
      if (alarmsRes.success && alarmsRes.data?.items) {
        setRecentAlarms(alarmsRes.data.items)
      }
      setAlarmsLoading(false)

      // 第三阶段：加载健康评分和电机诊断（计算量较大）
      const [healthRes, motorRes] = await Promise.all([
        getAllDevicesHealth().catch(() => ({ success: false, data: [] })),
        getAllDiagnoses().catch(() => ({ success: false, data: [] }))
      ])
      if (healthRes.success && healthRes.data) {
        setHealthScores(healthRes.data)
      }
      if (motorRes.success && motorRes.data) {
        setMotorDiagnoses(motorRes.data)
      }
      setHealthLoading(false)
    } catch (error) {
      logError('加载数据失败', error, 'Dashboard')
      message.error('加载设备数据失败')
    } finally {
      setMetricsLoading(false)
      setAlarmsLoading(false)
      setHealthLoading(false)
      setRefreshing(false)
    }
  }, [selectedDevice])

  // 手动刷新
  const handleRefresh = useCallback(() => {
    loadData(true)
  }, [loadData])

  // SignalR 连接（使用全局单例，避免重复连接）
  useEffect(() => {
    // 数据处理回调
    const handleTelemetryData = (points: TelemetryDataPoint[]) => {
      // 更新所有设备的最新遥测数据缓存（用于健康卡片）
      setLatestTelemetry(prev => {
        const updated = { ...prev }
        points.forEach(p => {
          const numValue = typeof p.value === 'number' ? p.value :
                          typeof p.value === 'boolean' ? (p.value ? 1 : 0) :
                          parseFloat(String(p.value)) || 0
          if (!updated[p.deviceId]) {
            updated[p.deviceId] = {}
          }
          updated[p.deviceId][p.tagId] = numValue
        })
        return updated
      })

      // 更新趋势图数据（仅选中设备）
      if (!selectedDevice) return
      const devicePoints = points.filter(p => p.deviceId === selectedDevice)
      if (devicePoints.length === 0) return

      setRealtimeData(prev => {
        const newPoint: RealtimeDataPoint = {
          time: formatTime(Date.now()),
          ts: Date.now()
        }

        devicePoints.forEach(p => {
          const numValue = typeof p.value === 'number' ? p.value :
                          typeof p.value === 'boolean' ? (p.value ? 1 : 0) :
                          parseFloat(String(p.value)) || 0
          newPoint[p.tagId] = numValue
          if (!availableTagsRef.current.includes(p.tagId)) {
            availableTagsRef.current = [...availableTagsRef.current, p.tagId]
            setAvailableTags([...availableTagsRef.current])
          }
        })

        return [...prev, newPoint].slice(-60)
      })
    }

    // 连接状态回调
    const handleConnectionChange = (isConnected: boolean) => {
      setConnected(isConnected)
    }

    // 注册回调
    const unsubscribeData = telemetrySignalR.onData(handleTelemetryData)
    const unsubscribeConnection = telemetrySignalR.onConnectionChange(handleConnectionChange)

    // 连接并订阅
    const connectAndSubscribe = async () => {
      try {
        await telemetrySignalR.connect()
        setConnected(telemetrySignalR.isConnected())
        // 使用智能切换，根据选中设备订阅
        await telemetrySignalR.switchSubscription(selectedDevice || undefined)
      } catch (err) {
        logError('SignalR 连接失败', err, 'Dashboard')
        setConnected(false)
      }
    }

    connectAndSubscribe()

    // 清理：仅取消回调注册，不断开连接（其他组件可能还在使用）
    return () => {
      unsubscribeData()
      unsubscribeConnection()
    }
  }, [selectedDevice])

  // 初始加载
  useEffect(() => {
    loadData()
    const interval = setInterval(loadData, 60000)
    return () => clearInterval(interval)
  }, [loadData])

  useEffect(() => {
    setRealtimeData([])
    setAvailableTags([])
    setSelectedTags([]) // 重置选中的标签
    availableTagsRef.current = []
  }, [selectedDevice])

  // 计算要显示的标签
  const displayTags = useMemo(() => {
    if (selectedTags.length === 0) {
      return availableTags.slice(0, 6) // 未选择时显示前6个
    }
    return selectedTags.slice(0, 6) // 最多显示6个
  }, [selectedTags, availableTags])

  // 统计数据（使用真实健康评分）
  const statistics = useMemo(() => {
    const deviceCount = devices.length
    // 在线设备：有健康评分的设备视为在线（说明有遥测数据）
    const onlineCount = healthScores.length > 0 ? healthScores.length : devices.filter(d => d.enabled).length
    // 使用真实健康评分计算平均值
    const avgHealth = healthScores.length > 0
      ? Math.round(healthScores.reduce((sum, h) => sum + h.index, 0) / healthScores.length)
      : deviceCount > 0 ? 80 : 0
    return { deviceCount, onlineCount, avgHealth }
  }, [devices, healthScores])

  // v64: 电机统计数据
  const motorStats = useMemo(() => {
    const total = motorDiagnoses.length
    const healthy = motorDiagnoses.filter(d => d.healthScore >= 90).length
    const attention = motorDiagnoses.filter(d => d.healthScore >= 70 && d.healthScore < 90).length
    const warning = motorDiagnoses.filter(d => d.healthScore < 70).length
    const totalFaults = motorDiagnoses.reduce((sum, d) => sum + d.faults.length, 0)
    const avgHealth = total > 0
      ? Math.round(motorDiagnoses.reduce((sum, d) => sum + d.healthScore, 0) / total)
      : 0
    return { total, healthy, attention, warning, totalFaults, avgHealth }
  }, [motorDiagnoses])

  // 指标卡片数据
  const metrics = [
    { icon: Activity, title: '在线设备', value: statistics.onlineCount, unit: '台', trend: 0, color: 'primary' as const },
    { icon: AlertTriangle, title: '活动警报', value: openAlarmCount, unit: '条', trend: 0, color: 'warning' as const },
    { icon: Wrench, title: '待处理工单', value: 0, unit: '个', trend: 0, color: 'success' as const },
    { icon: TrendingUp, title: '系统健康度', value: statistics.avgHealth, unit: '%', trend: 0, color: 'success' as const }
  ]

  return (
    <div>
      {/* 页面标题 */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: 24 }}>
        <div>
          <h1 style={{ fontSize: 24, fontWeight: 700, color: 'var(--color-text-primary)', margin: '0 0 8px 0' }}>
            实时监控中心
          </h1>
          <p style={{ fontSize: 14, color: 'var(--color-text-muted)', margin: 0 }}>
            全面掌握设备运行状态和系统健康指标
          </p>
        </div>
        <button
          onClick={handleRefresh}
          disabled={refreshing}
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 6,
            padding: '8px 16px',
            background: 'var(--color-bg-subtle)',
            border: '1px solid var(--color-border)',
            borderRadius: 8,
            color: 'var(--color-text-secondary)',
            cursor: refreshing ? 'not-allowed' : 'pointer',
            fontSize: 14,
            transition: 'all 0.2s'
          }}
          onMouseEnter={(e) => {
            if (!refreshing) {
              e.currentTarget.style.borderColor = 'var(--color-primary)'
              e.currentTarget.style.color = 'var(--color-primary)'
            }
          }}
          onMouseLeave={(e) => {
            e.currentTarget.style.borderColor = 'var(--color-border)'
            e.currentTarget.style.color = 'var(--color-text-secondary)'
          }}
        >
          <RefreshCw size={16} style={{ animation: refreshing ? 'spin 1s linear infinite' : 'none' }} />
          {refreshing ? '刷新中...' : '刷新数据'}
        </button>
      </div>
      <style>{`@keyframes spin { from { transform: rotate(0deg); } to { transform: rotate(360deg); } }`}</style>

      {/* 指标卡片 */}
      <div style={{
        display: 'grid',
        gridTemplateColumns: 'repeat(4, 1fr)',
        gap: 24,
        marginBottom: 24
      }}>
        {metricsLoading ? (
          // 骨架屏加载状态
          [1, 2, 3, 4].map(i => <MetricCardSkeleton key={i} />)
        ) : (
          metrics.map((metric, idx) => {
            const Icon = metric.icon
            const colors = cardColors[metric.color]

            return (
              <div
                key={idx}
                style={{
                  background: colors.gradient,
                  borderLeft: `4px solid ${colors.borderLeft}`,
                  borderRadius: '0 12px 12px 0',
                  padding: 24,
                  transition: 'all 0.3s ease',
                  cursor: 'default'
                }}
                onMouseEnter={(e) => {
                  e.currentTarget.style.transform = 'translateY(-2px)'
                  e.currentTarget.style.boxShadow = '0 8px 25px rgba(0, 0, 0, 0.3)'
                }}
                onMouseLeave={(e) => {
                  e.currentTarget.style.transform = 'translateY(0)'
                  e.currentTarget.style.boxShadow = 'none'
                }}
              >
                <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', marginBottom: 16 }}>
                  <div style={{ padding: 12, background: colors.iconBg, borderRadius: 10 }}>
                    <Icon size={24} color={colors.iconColor} />
                  </div>
                  <span style={{
                    fontSize: 12,
                    padding: '4px 10px',
                    borderRadius: 20,
                    background: metric.trend >= 0 ? 'rgba(16, 185, 129, 0.2)' : 'rgba(239, 68, 68, 0.2)',
                    color: metric.trend >= 0 ? 'var(--color-success)' : 'var(--color-danger)',
                    fontWeight: 500
                  }}>
                    {metric.trend >= 0 ? '↑' : '↓'} {Math.abs(metric.trend)}%
                  </span>
                </div>
                <h3 style={{ fontSize: 14, color: 'var(--color-text-muted)', marginBottom: 8, fontWeight: 400 }}>{metric.title}</h3>
                <div style={{ display: 'flex', alignItems: 'baseline', gap: 8 }}>
                  <span style={{ fontSize: 32, fontWeight: 700, color: 'var(--color-text-primary)', lineHeight: 1 }}>{metric.value}</span>
                  <span style={{ fontSize: 14, color: 'var(--color-text-muted)' }}>{metric.unit}</span>
                </div>
              </div>
            )
          })
        )}
      </div>

      {/* 主内容区 - 图表和告警 */}
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 380px', gap: 24, marginBottom: 24 }}>
        {/* 趋势图 */}
        <div style={{ background: 'var(--color-bg-dark)', border: '1px solid var(--color-border)', borderRadius: 12, padding: 24 }}>
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 20, flexWrap: 'wrap', gap: 12 }}>
            <h2 style={{ fontSize: 16, fontWeight: 600, color: 'var(--color-text-primary)', margin: 0 }}>实时数据趋势</h2>
            <div style={{ display: 'flex', gap: 12, alignItems: 'center' }}>
              <Select
                placeholder="选择设备"
                value={selectedDevice}
                onChange={setSelectedDevice}
                style={{ width: 160 }}
                options={devices.map(d => ({ value: d.deviceId, label: d.name || d.deviceId }))}
              />
              <Select
                mode="multiple"
                placeholder="选择标签（留空显示全部）"
                value={selectedTags}
                onChange={setSelectedTags}
                style={{ minWidth: 200, maxWidth: 400 }}
                maxTagCount={2}
                options={availableTags.map(t => ({ value: t, label: t }))}
                allowClear
              />
            </div>
          </div>

          {realtimeData.length === 0 ? (
            <div style={{ height: 280, display: 'flex', flexDirection: 'column', justifyContent: 'center', alignItems: 'center' }}>
              <Empty description={<span style={{ color: 'var(--color-text-dim)' }}>{connected ? "等待实时数据..." : "SignalR 未连接"}</span>} />
              <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginTop: 12 }}>
                <div style={{ width: 8, height: 8, borderRadius: '50%', background: connected ? 'var(--color-success)' : 'var(--color-danger)' }} />
                <span style={{ color: 'var(--color-text-dim)', fontSize: 13 }}>{connected ? '已连接' : '未连接'}</span>
              </div>
            </div>
          ) : (
            <ResponsiveContainer width="100%" height={280}>
              <LineChart data={realtimeData}>
                <CartesianGrid strokeDasharray="3 3" stroke="var(--color-border)" />
                <XAxis dataKey="time" tick={{ fontSize: 12, fill: 'var(--color-text-muted)' }} stroke="var(--color-border)" />
                <YAxis tick={{ fontSize: 12, fill: 'var(--color-text-muted)' }} stroke="var(--color-border)" />
                <RechartsTooltip contentStyle={{ background: 'var(--color-bg-card)', border: '1px solid var(--color-border)', borderRadius: 8, color: 'var(--color-text-primary)' }} />
                <Legend wrapperStyle={{ color: 'var(--color-text-muted)' }} />
                {displayTags.map((tag, idx) => (
                  <Line key={tag} type="monotone" dataKey={tag} stroke={lineColors[idx % lineColors.length]} dot={false} strokeWidth={2} isAnimationActive={false} />
                ))}
              </LineChart>
            </ResponsiveContainer>
          )}
        </div>

        {/* 告警面板 */}
        <div style={{ background: 'var(--color-bg-dark)', border: '1px solid var(--color-border)', borderRadius: 12, padding: 24 }}>
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 16 }}>
            <h2 style={{ fontSize: 16, fontWeight: 600, color: 'var(--color-text-primary)', margin: 0 }}>实时报警</h2>
            <span style={{ fontSize: 14, color: 'var(--color-text-muted)' }}>{openAlarmCount} 条活动警报</span>
          </div>
          <div style={{ maxHeight: 320, overflowY: 'auto' }}>
            {alarmsLoading ? (
              // 骨架屏加载状态
              [1, 2, 3].map(i => <AlarmItemSkeleton key={i} />)
            ) : recentAlarms.length === 0 ? (
              <Empty description={<span style={{ color: 'var(--color-text-dim)' }}>暂无活动告警</span>} />
            ) : (
              recentAlarms.map((alarm) => {
                const config = severityConfig[alarm.severity] || severityConfig[1]
                const device = devices.find(d => d.deviceId === alarm.deviceId)
                return (
                  <div key={alarm.alarmId} style={{
                    background: config.bg,
                    borderLeft: `3px solid ${config.color}`,
                    borderRadius: '0 8px 8px 0',
                    padding: 16,
                    marginBottom: 12
                  }}>
                    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 4 }}>
                      <span style={{ fontSize: 14, fontWeight: 500, color: 'var(--color-text-primary)' }}>
                        {device?.name || alarm.deviceId}
                      </span>
                      <span style={{ fontSize: 12, color: 'var(--color-text-dim)' }}>{formatRelativeTime(alarm.ts)}</span>
                    </div>
                    <p style={{ fontSize: 13, color: 'var(--color-text-secondary)', margin: '0 0 8px 0', lineHeight: 1.5 }}>{alarm.message}</p>
                    <div style={{ display: 'flex', gap: 8 }}>
                      <span style={{ fontSize: 11, padding: '2px 8px', borderRadius: 20, background: config.bg, color: config.color, border: `1px solid ${config.border}` }}>{config.label}</span>
                      <span style={{ fontSize: 11, padding: '2px 8px', borderRadius: 20, background: 'var(--color-bg-card)', color: 'var(--color-text-muted)' }}>{alarm.code}</span>
                      {alarm.tagId && (
                        <span style={{ fontSize: 11, padding: '2px 8px', borderRadius: 20, background: 'var(--color-bg-card)', color: 'var(--color-text-muted)' }}>{alarm.tagId}</span>
                      )}
                    </div>
                  </div>
                )
              })
            )}
          </div>
        </div>
      </div>

      {/* 设备健康状态 */}
      <div style={{ background: 'var(--color-bg-dark)', border: '1px solid var(--color-border)', borderRadius: 12, padding: 24 }}>
        <h2 style={{ fontSize: 16, fontWeight: 600, color: 'var(--color-text-primary)', margin: '0 0 16px 0' }}>设备健康状态</h2>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, 1fr)', gap: 16 }}>
          {healthLoading ? (
            // 骨架屏加载状态
            [1, 2, 3, 4].map(i => <DeviceHealthSkeleton key={i} />)
          ) : devices.length === 0 ? (
            <div style={{ gridColumn: '1 / -1' }}>
              <Empty description={<span style={{ color: 'var(--color-text-dim)' }}>暂无设备数据</span>} />
            </div>
          ) : (
            devices.slice(0, 4).map((device) => {
              const health = healthScores.find(h => h.deviceId === device.deviceId)
              const levelConfig = healthLevelConfig[health?.level || 'Healthy'] || healthLevelConfig['Healthy']
              const telemetry = latestTelemetry[device.deviceId] || {}
              // 尝试获取常见标签名的遥测数据
              const getTagValue = (keywords: string[]) => {
                for (const key of Object.keys(telemetry)) {
                  const lowerKey = key.toLowerCase()
                  if (keywords.some(k => lowerKey.includes(k))) {
                    return telemetry[key]
                  }
                }
                return null
              }
              const vibration = getTagValue(['vibration', 'vib', '振动'])
              const temperature = getTagValue(['temp', 'temperature', '温度'])
              const current = getTagValue(['current', 'curr', '电流'])

              return (
                <div key={device.deviceId} style={{ background: 'var(--color-bg-subtle)', border: '1px solid var(--color-border)', borderRadius: 8, padding: 16, transition: 'all 0.2s ease' }}
                  onMouseEnter={(e) => { e.currentTarget.style.borderColor = 'var(--color-border-light)' }}
                  onMouseLeave={(e) => { e.currentTarget.style.borderColor = 'var(--color-border)' }}
                >
                  <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 12 }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                      <div className={`status-dot ${levelConfig.statusClass}`} />
                      <span style={{ fontSize: 15, fontWeight: 500, color: 'var(--color-text-primary)' }}>{device.name || device.deviceId}</span>
                    </div>
                    <span style={{ fontSize: 12, color: 'var(--color-text-dim)' }}>{device.location || '--'}</span>
                  </div>

                  {/* 健康指数显示 */}
                  <div style={{ marginBottom: 12, padding: 12, background: 'var(--color-bg-dark)', borderRadius: 6 }}>
                    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 8 }}>
                      <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                        <Heart size={14} color={levelConfig.color} />
                        <span style={{ fontSize: 12, color: 'var(--color-text-muted)' }}>健康指数</span>
                      </div>
                      <span style={{ fontSize: 18, fontWeight: 600, color: levelConfig.color }}>{health?.index ?? '--'}%</span>
                    </div>
                    {health && (
                      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                        <span style={{ fontSize: 10, padding: '2px 6px', borderRadius: 4, background: 'var(--color-bg-card)', color: 'var(--color-text-dim)' }}>偏差:{health.deviationScore}</span>
                        <span style={{ fontSize: 10, padding: '2px 6px', borderRadius: 4, background: 'var(--color-bg-card)', color: 'var(--color-text-dim)' }}>趋势:{health.trendScore}</span>
                        <span style={{ fontSize: 10, padding: '2px 6px', borderRadius: 4, background: 'var(--color-bg-card)', color: 'var(--color-text-dim)' }}>稳定:{health.stabilityScore}</span>
                        <span style={{ fontSize: 10, padding: '2px 6px', borderRadius: 4, background: 'var(--color-bg-card)', color: 'var(--color-text-dim)' }}>告警:{health.alarmScore}</span>
                      </div>
                    )}
                  </div>

                  {/* 实时遥测数据 */}
                  <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: 8 }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                      <Activity size={14} color="#3b82f6" />
                      <div>
                        <p style={{ fontSize: 10, color: 'var(--color-text-dim)', margin: 0 }}>振动</p>
                        <p style={{ fontSize: 12, color: 'var(--color-text-primary)', margin: 0 }}>{vibration?.toFixed(2) ?? '--'}</p>
                      </div>
                    </div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                      <Thermometer size={14} color="#f59e0b" />
                      <div>
                        <p style={{ fontSize: 10, color: 'var(--color-text-dim)', margin: 0 }}>温度</p>
                        <p style={{ fontSize: 12, color: 'var(--color-text-primary)', margin: 0 }}>{temperature?.toFixed(1) ?? '--'}°C</p>
                      </div>
                    </div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                      <Zap size={14} color="#eab308" />
                      <div>
                        <p style={{ fontSize: 10, color: 'var(--color-text-dim)', margin: 0 }}>电流</p>
                        <p style={{ fontSize: 12, color: 'var(--color-text-primary)', margin: 0 }}>{current?.toFixed(1) ?? '--'}A</p>
                      </div>
                    </div>
                  </div>

                  {/* 问题标签提示 */}
                  {health?.problemTags && health.problemTags.length > 0 && (
                    <div style={{ marginTop: 12, paddingTop: 12, borderTop: '1px solid var(--color-border)' }}>
                      <span style={{ fontSize: 11, color: 'var(--color-text-dim)' }}>问题标签：</span>
                      <span style={{ fontSize: 11, color: 'var(--color-warning)' }}>{health.problemTags.slice(0, 3).join(', ')}</span>
                    </div>
                  )}
                </div>
              )
            })
          )}
        </div>
      </div>

      {/* v64: 电机故障预测摘要 */}
      {motorDiagnoses.length > 0 && (
        <div style={{
          marginTop: 24,
          background: 'linear-gradient(135deg, rgba(26, 35, 126, 0.15) 0%, rgba(0, 188, 212, 0.1) 100%)',
          border: '1px solid var(--color-border)',
          borderRadius: 12,
          padding: 24,
          position: 'relative',
          overflow: 'hidden'
        }}>
          {/* 背景装饰 */}
          <div style={{
            position: 'absolute',
            right: -20,
            top: -20,
            width: 120,
            height: 120,
            background: 'radial-gradient(circle, rgba(0, 188, 212, 0.2) 0%, transparent 70%)',
            borderRadius: '50%'
          }} />

          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 20, position: 'relative' }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
              <div style={{
                padding: 10,
                background: 'linear-gradient(135deg, #1A237E 0%, #283593 100%)',
                borderRadius: 10
              }}>
                <Cpu size={24} color="#00BCD4" />
              </div>
              <div>
                <h2 style={{ fontSize: 16, fontWeight: 600, color: 'var(--color-text-primary)', margin: 0 }}>电机故障预测</h2>
                <p style={{ fontSize: 12, color: 'var(--color-text-muted)', margin: '4px 0 0' }}>基于机器学习的智能诊断</p>
              </div>
            </div>
            <button
              onClick={() => navigate('/motor-prediction')}
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: 6,
                padding: '8px 16px',
                background: 'var(--color-primary)',
                border: 'none',
                borderRadius: 8,
                color: '#fff',
                cursor: 'pointer',
                fontSize: 14,
                fontWeight: 500,
                transition: 'all 0.2s'
              }}
              onMouseEnter={(e) => {
                e.currentTarget.style.background = 'var(--color-primary-light)'
                e.currentTarget.style.transform = 'translateX(2px)'
              }}
              onMouseLeave={(e) => {
                e.currentTarget.style.background = 'var(--color-primary)'
                e.currentTarget.style.transform = 'translateX(0)'
              }}
            >
              查看详情
              <ChevronRight size={16} />
            </button>
          </div>

          {/* 统计数据 */}
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(5, 1fr)', gap: 16, position: 'relative' }}>
            <div style={{ background: 'var(--color-bg-dark)', borderRadius: 8, padding: 16, textAlign: 'center' }}>
              <p style={{ fontSize: 12, color: 'var(--color-text-muted)', margin: '0 0 8px' }}>监测电机</p>
              <p style={{ fontSize: 24, fontWeight: 700, color: 'var(--color-text-primary)', margin: 0 }}>{motorStats.total}</p>
            </div>
            <div style={{ background: 'var(--color-bg-dark)', borderRadius: 8, padding: 16, textAlign: 'center' }}>
              <p style={{ fontSize: 12, color: 'var(--color-text-muted)', margin: '0 0 8px' }}>健康运行</p>
              <p style={{ fontSize: 24, fontWeight: 700, color: '#52c41a', margin: 0 }}>{motorStats.healthy}</p>
            </div>
            <div style={{ background: 'var(--color-bg-dark)', borderRadius: 8, padding: 16, textAlign: 'center' }}>
              <p style={{ fontSize: 12, color: 'var(--color-text-muted)', margin: '0 0 8px' }}>需要关注</p>
              <p style={{ fontSize: 24, fontWeight: 700, color: '#faad14', margin: 0 }}>{motorStats.attention}</p>
            </div>
            <div style={{ background: 'var(--color-bg-dark)', borderRadius: 8, padding: 16, textAlign: 'center' }}>
              <p style={{ fontSize: 12, color: 'var(--color-text-muted)', margin: '0 0 8px' }}>故障警告</p>
              <p style={{ fontSize: 24, fontWeight: 700, color: '#f5222d', margin: 0 }}>{motorStats.warning}</p>
            </div>
            <div style={{ background: 'var(--color-bg-dark)', borderRadius: 8, padding: 16, textAlign: 'center' }}>
              <p style={{ fontSize: 12, color: 'var(--color-text-muted)', margin: '0 0 8px' }}>平均健康度</p>
              <p style={{
                fontSize: 24,
                fontWeight: 700,
                color: motorStats.avgHealth >= 90 ? '#52c41a' : motorStats.avgHealth >= 70 ? '#faad14' : '#f5222d',
                margin: 0
              }}>
                {motorStats.avgHealth}%
              </p>
            </div>
          </div>

          {/* 故障预警列表 */}
          {motorDiagnoses.filter(d => d.faults.length > 0).length > 0 && (
            <div style={{ marginTop: 16 }}>
              <p style={{ fontSize: 13, color: 'var(--color-text-muted)', margin: '0 0 12px', display: 'flex', alignItems: 'center', gap: 6 }}>
                <AlertTriangle size={14} color="#f5222d" />
                检测到 {motorStats.totalFaults} 个潜在故障
              </p>
              <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
                {motorDiagnoses
                  .filter(d => d.faults.length > 0)
                  .slice(0, 4)
                  .map((diagnosis) => (
                    <div
                      key={diagnosis.diagnosisId}
                      style={{
                        flex: '1 1 200px',
                        background: 'var(--color-bg-dark)',
                        border: '1px solid var(--color-border)',
                        borderRadius: 8,
                        padding: 12,
                        cursor: 'pointer',
                        transition: 'all 0.2s'
                      }}
                      onClick={() => navigate('/motor-prediction')}
                      onMouseEnter={(e) => {
                        e.currentTarget.style.borderColor = 'var(--color-primary)'
                      }}
                      onMouseLeave={(e) => {
                        e.currentTarget.style.borderColor = 'var(--color-border)'
                      }}
                    >
                      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 8 }}>
                        <span style={{ fontSize: 13, fontWeight: 500, color: 'var(--color-text-primary)' }}>
                          {diagnosis.instanceId.slice(0, 12)}
                        </span>
                        <span style={{
                          fontSize: 11,
                          padding: '2px 8px',
                          borderRadius: 10,
                          background: diagnosis.healthScore >= 70 ? 'rgba(250, 173, 20, 0.2)' : 'rgba(245, 34, 45, 0.2)',
                          color: diagnosis.healthScore >= 70 ? '#faad14' : '#f5222d'
                        }}>
                          {diagnosis.healthScore}%
                        </span>
                      </div>
                      <div style={{ fontSize: 12, color: 'var(--color-text-muted)' }}>
                        {diagnosis.faults.slice(0, 2).map((f, i) => (
                          <span key={i}>{f.faultTypeName || f.description}{i < Math.min(diagnosis.faults.length, 2) - 1 ? '、' : ''}</span>
                        ))}
                        {diagnosis.faults.length > 2 && <span> 等{diagnosis.faults.length}项</span>}
                      </div>
                    </div>
                  ))}
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  )
}
