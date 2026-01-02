import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Select, Empty, message } from 'antd'
import { Activity, AlertTriangle, Wrench, TrendingUp, Thermometer, Zap, Gauge } from 'lucide-react'
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
import { HubConnectionBuilder, HubConnection, LogLevel } from '@microsoft/signalr'
import { getDevices } from '../../api/device'
import { getAlarmStats } from '../../api/alarm'
import { getToken, refreshTokenIfNeeded, isTokenExpiringSoon } from '../../store/authStore'
import type { Device } from '../../types/device'

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

// 健康等级
function getDeviceHealthIndex(device: Device): number {
  const hash = device.deviceId.split('').reduce((a, b) => {
    a = ((a << 5) - a) + b.charCodeAt(0)
    return a & a
  }, 0)
  const baseRandom = Math.abs(hash % 100)
  if (device.status === 'Running') return 80 + (baseRandom % 20)
  if (device.status === 'Stopped') return 60 + (baseRandom % 20)
  if (device.status === 'Fault') return 20 + (baseRandom % 30)
  return 50 + (baseRandom % 40)
}

function formatTime(ts: number): string {
  return new Date(ts).toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit', second: '2-digit' })
}

interface RealtimeDataPoint {
  time: string
  ts: number
  [key: string]: string | number
}

interface TelemetryData {
  deviceId: string
  tagId: string
  value: any
  ts: number
}

// 模拟设备详细数据
const mockEquipmentData = [
  { id: '1', name: '电机M-301', location: '车间A', status: 'normal', vibration: 2.3, temperature: 68, current: 42, health: 94, rul: 156 },
  { id: '2', name: '泵P-102', location: '车间B', status: 'warning', vibration: 4.8, temperature: 78, current: 38, health: 76, rul: 45 },
  { id: '3', name: '压缩机C-201', location: '车间A', status: 'normal', vibration: 1.9, temperature: 65, current: 55, health: 92, rul: 187 },
  { id: '4', name: '风机F-401', location: '车间C', status: 'critical', vibration: 7.2, temperature: 85, current: 48, health: 58, rul: 12 }
]

// 模拟告警数据
const mockAlerts = [
  { id: '1', equipment: '泵P-102', message: '温度持续升高,可能存在轴承磨损', time: '15分钟前', level: 'warning' as const, parameter: '温度: 78°C' },
  { id: '2', equipment: '电机M-301', message: '电流波动异常,建议检查电源', time: '1小时前', level: 'info' as const, parameter: '电流' },
  { id: '3', equipment: '风机F-401', message: '振动超标,需立即检修', time: '2小时前', level: 'critical' as const, parameter: '振动: 7.2mm/s' },
  { id: '4', equipment: '压缩机C-201', message: '运行正常,健康度良好', time: '3小时前', level: 'normal' as const, parameter: '健康度: 92%' }
]

const alertConfig = {
  critical: { color: '#ef4444', bg: 'rgba(239, 68, 68, 0.1)', border: 'rgba(239, 68, 68, 0.3)', label: '紧急' },
  warning: { color: '#f59e0b', bg: 'rgba(245, 158, 11, 0.1)', border: 'rgba(245, 158, 11, 0.3)', label: '警告' },
  info: { color: '#3b82f6', bg: 'rgba(59, 130, 246, 0.1)', border: 'rgba(59, 130, 246, 0.3)', label: '提示' },
  normal: { color: '#10b981', bg: 'rgba(16, 185, 129, 0.1)', border: 'rgba(16, 185, 129, 0.3)', label: '正常' }
}

const statusColors = {
  normal: '#10b981',
  warning: '#f59e0b',
  critical: '#ef4444'
}

export default function Dashboard() {
  const [loading, setLoading] = useState(true)
  const [devices, setDevices] = useState<Device[]>([])
  const [openAlarmCount, setOpenAlarmCount] = useState(0)
  const [selectedDevice, setSelectedDevice] = useState<string | null>(null)
  const [realtimeData, setRealtimeData] = useState<RealtimeDataPoint[]>([])
  const [availableTags, setAvailableTags] = useState<string[]>([])
  const connectionRef = useRef<HubConnection | null>(null)
  const availableTagsRef = useRef<string[]>([])
  const [connected, setConnected] = useState(false)

  // 加载数据
  const loadData = useCallback(async () => {
    setLoading(true)
    try {
      const deviceList = await getDevices()
      setDevices(deviceList)
      if (deviceList.length > 0 && !selectedDevice) {
        setSelectedDevice(deviceList[0].deviceId)
      }
      try {
        const alarmStatsRes = await getAlarmStats()
        if (alarmStatsRes.success && alarmStatsRes.data) {
          setOpenAlarmCount(alarmStatsRes.data.openCount)
        }
      } catch (e) {
        console.warn('加载告警统计失败:', e)
      }
    } catch (error) {
      console.error('加载数据失败:', error)
      message.error('加载设备数据失败')
    } finally {
      setLoading(false)
    }
  }, [selectedDevice])

  // SignalR 连接
  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/telemetry', {
        accessTokenFactory: async () => {
          if (isTokenExpiringSoon()) {
            await refreshTokenIfNeeded()
          }
          return getToken() || ''
        }
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    // 监听后端发送的数据
    const handleTelemetryData = (data: TelemetryData[] | TelemetryData) => {
      if (!selectedDevice) return
      
      const points = Array.isArray(data) ? data : [data]
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
                          parseFloat(p.value) || 0
          newPoint[p.tagId] = numValue
          if (!availableTagsRef.current.includes(p.tagId)) {
            availableTagsRef.current = [...availableTagsRef.current, p.tagId]
            setAvailableTags([...availableTagsRef.current])
          }
        })

        return [...prev, newPoint].slice(-60)
      })
    }

    connection.on('ReceiveData', handleTelemetryData)
    connection.onclose(() => setConnected(false))
    connection.onreconnected(() => {
      setConnected(true)
      connection.invoke('SubscribeAll').catch(console.error)
    })

    connection.start()
      .then(() => {
        setConnected(true)
        connection.invoke('SubscribeAll').catch(console.warn)
      })
      .catch(err => {
        console.error('SignalR 连接失败:', err)
        setConnected(false)
      })

    connectionRef.current = connection

    return () => {
      connection.stop()
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
    availableTagsRef.current = []
  }, [selectedDevice])

  // 统计数据
  const statistics = useMemo(() => {
    const deviceCount = devices.length
    const onlineCount = devices.filter(d => d.status === 'Running').length
    const avgHealth = deviceCount > 0 
      ? Math.round(devices.reduce((sum, d) => sum + getDeviceHealthIndex(d), 0) / deviceCount)
      : 94
    return { deviceCount, onlineCount, avgHealth }
  }, [devices])

  // 指标卡片数据
  const metrics = [
    { icon: Activity, title: '在线设备', value: statistics.onlineCount || 48, unit: '台', trend: 2, color: 'primary' as const },
    { icon: AlertTriangle, title: '活动警报', value: openAlarmCount || 12, unit: '条', trend: -15, color: 'warning' as const },
    { icon: Wrench, title: '待处理工单', value: 5, unit: '个', trend: -20, color: 'success' as const },
    { icon: TrendingUp, title: '系统健康度', value: statistics.avgHealth, unit: '%', trend: 3, color: 'success' as const }
  ]

  return (
    <div>
      {/* 页面标题 */}
      <div style={{ marginBottom: 24 }}>
        <h1 style={{ fontSize: 24, fontWeight: 700, color: 'var(--color-text-primary)', margin: '0 0 8px 0' }}>
          实时监控中心
        </h1>
        <p style={{ fontSize: 14, color: 'var(--color-text-muted)', margin: 0 }}>
          全面掌握设备运行状态和系统健康指标
        </p>
      </div>

      {/* 指标卡片 */}
      <div style={{ 
        display: 'grid', 
        gridTemplateColumns: 'repeat(4, 1fr)', 
        gap: 24, 
        marginBottom: 24 
      }}>
        {metrics.map((metric, idx) => {
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
        })}
      </div>

      {/* 主内容区 - 图表和告警 */}
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 380px', gap: 24, marginBottom: 24 }}>
        {/* 趋势图 */}
        <div style={{ background: 'var(--color-bg-dark)', border: '1px solid var(--color-border)', borderRadius: 12, padding: 24 }}>
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 20 }}>
            <h2 style={{ fontSize: 16, fontWeight: 600, color: 'var(--color-text-primary)', margin: 0 }}>实时数据趋势</h2>
            <Select
              placeholder="选择设备"
              value={selectedDevice}
              onChange={setSelectedDevice}
              style={{ width: 160 }}
              options={devices.map(d => ({ value: d.deviceId, label: d.name || d.deviceId }))}
            />
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
                {availableTags.slice(0, 6).map((tag, idx) => (
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
            <span style={{ fontSize: 14, color: 'var(--color-text-muted)' }}>{mockAlerts.length} 条活动警报</span>
          </div>
          <div style={{ maxHeight: 320, overflowY: 'auto' }}>
            {mockAlerts.map((alert) => {
              const config = alertConfig[alert.level]
              return (
                <div key={alert.id} style={{ 
                  background: config.bg, 
                  borderLeft: `3px solid ${config.color}`,
                  borderRadius: '0 8px 8px 0', 
                  padding: 16, 
                  marginBottom: 12 
                }}>
                  <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 4 }}>
                    <span style={{ fontSize: 14, fontWeight: 500, color: 'var(--color-text-primary)' }}>{alert.equipment}</span>
                    <span style={{ fontSize: 12, color: 'var(--color-text-dim)' }}>{alert.time}</span>
                  </div>
                  <p style={{ fontSize: 13, color: 'var(--color-text-secondary)', margin: '0 0 8px 0', lineHeight: 1.5 }}>{alert.message}</p>
                  <div style={{ display: 'flex', gap: 8 }}>
                    <span style={{ fontSize: 11, padding: '2px 8px', borderRadius: 20, background: config.bg, color: config.color, border: `1px solid ${config.border}` }}>{config.label}</span>
                    <span style={{ fontSize: 11, padding: '2px 8px', borderRadius: 20, background: 'var(--color-bg-card)', color: 'var(--color-text-muted)' }}>{alert.parameter}</span>
                  </div>
                </div>
              )
            })}
          </div>
        </div>
      </div>

      {/* 设备健康状态 */}
      <div style={{ background: 'var(--color-bg-dark)', border: '1px solid var(--color-border)', borderRadius: 12, padding: 24 }}>
        <h2 style={{ fontSize: 16, fontWeight: 600, color: 'var(--color-text-primary)', margin: '0 0 16px 0' }}>设备健康状态</h2>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, 1fr)', gap: 16 }}>
          {mockEquipmentData.map((item) => (
            <div key={item.id} style={{ background: 'var(--color-bg-subtle)', border: '1px solid var(--color-border)', borderRadius: 8, padding: 16, transition: 'all 0.2s ease' }}
              onMouseEnter={(e) => { e.currentTarget.style.borderColor = 'var(--color-border-light)' }}
              onMouseLeave={(e) => { e.currentTarget.style.borderColor = 'var(--color-border)' }}
            >
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 12 }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                  <div className={`status-dot ${item.status === 'normal' ? 'online' : item.status === 'warning' ? 'warning' : 'offline'}`} />
                  <span style={{ fontSize: 15, fontWeight: 500, color: 'var(--color-text-primary)' }}>{item.name}</span>
                </div>
                <span style={{ fontSize: 12, color: 'var(--color-text-dim)' }}>{item.location}</span>
              </div>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                  <Activity size={16} color="#3b82f6" />
                  <div>
                    <p style={{ fontSize: 11, color: 'var(--color-text-dim)', margin: 0 }}>振动</p>
                    <p style={{ fontSize: 13, color: 'var(--color-text-primary)', margin: 0 }}>{item.vibration} mm/s</p>
                  </div>
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                  <Thermometer size={16} color="#f59e0b" />
                  <div>
                    <p style={{ fontSize: 11, color: 'var(--color-text-dim)', margin: 0 }}>温度</p>
                    <p style={{ fontSize: 13, color: 'var(--color-text-primary)', margin: 0 }}>{item.temperature}°C</p>
                  </div>
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                  <Zap size={16} color="#eab308" />
                  <div>
                    <p style={{ fontSize: 11, color: 'var(--color-text-dim)', margin: 0 }}>电流</p>
                    <p style={{ fontSize: 13, color: 'var(--color-text-primary)', margin: 0 }}>{item.current} A</p>
                  </div>
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                  <Gauge size={16} color="#10b981" />
                  <div>
                    <p style={{ fontSize: 11, color: 'var(--color-text-dim)', margin: 0 }}>健康度</p>
                    <p style={{ fontSize: 13, color: 'var(--color-text-primary)', margin: 0 }}>{item.health}%</p>
                  </div>
                </div>
              </div>
              <div style={{ marginTop: 12, paddingTop: 12, borderTop: '1px solid var(--color-border)', display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                <span style={{ fontSize: 12, color: 'var(--color-text-dim)' }}>预计剩余寿命</span>
                <span style={{ fontSize: 13, fontWeight: 500, color: item.rul <= 30 ? 'var(--color-danger)' : item.rul <= 90 ? 'var(--color-warning)' : 'var(--color-text-primary)' }}>{item.rul} 天</span>
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}
