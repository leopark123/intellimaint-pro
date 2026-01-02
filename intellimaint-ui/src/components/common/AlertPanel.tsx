import { AlertTriangle, AlertCircle, Info, CheckCircle } from 'lucide-react'

interface Alert {
  id: string | number
  equipment: string
  message: string
  time: string
  level: 'critical' | 'warning' | 'info' | 'normal'
  parameter?: string
}

interface AlertPanelProps {
  alerts: Alert[]
  title?: string
}

const alertConfig = {
  critical: {
    icon: AlertTriangle,
    color: '#ef4444',
    bg: 'rgba(239, 68, 68, 0.1)',
    border: 'rgba(239, 68, 68, 0.3)',
    label: '危急'
  },
  warning: {
    icon: AlertCircle,
    color: '#f59e0b',
    bg: 'rgba(245, 158, 11, 0.1)',
    border: 'rgba(245, 158, 11, 0.3)',
    label: '警告'
  },
  info: {
    icon: Info,
    color: '#3b82f6',
    bg: 'rgba(59, 130, 246, 0.1)',
    border: 'rgba(59, 130, 246, 0.3)',
    label: '提示'
  },
  normal: {
    icon: CheckCircle,
    color: '#10b981',
    bg: 'rgba(16, 185, 129, 0.1)',
    border: 'rgba(16, 185, 129, 0.3)',
    label: '正常'
  }
}

export default function AlertPanel({ alerts, title = '实时报警' }: AlertPanelProps) {
  return (
    <div style={{
      background: 'var(--color-bg-dark)',
      border: '1px solid var(--color-border)',
      borderRadius: 12,
      padding: 24,
      height: '100%'
    }}>
      {/* 标题 */}
      <div style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        marginBottom: 16
      }}>
        <h2 style={{
          fontSize: 16,
          fontWeight: 600,
          color: 'var(--color-text-primary)',
          margin: 0
        }}>
          {title}
        </h2>
        <span style={{
          fontSize: 14,
          color: 'var(--color-text-muted)'
        }}>
          {alerts.length} 条活动警报
        </span>
      </div>

      {/* 告警列表 */}
      <div style={{
        maxHeight: 400,
        overflowY: 'auto'
      }}>
        {alerts.length === 0 ? (
          <div style={{
            textAlign: 'center',
            padding: '40px 0',
            color: 'var(--color-text-dim)'
          }}>
            暂无告警
          </div>
        ) : (
          alerts.map((alert) => {
            const config = alertConfig[alert.level]
            const Icon = config.icon

            return (
              <div
                key={alert.id}
                style={{
                  background: config.bg,
                  borderLeft: `3px solid ${config.color}`,
                  borderRadius: '0 8px 8px 0',
                  padding: 16,
                  marginBottom: 12,
                  transition: 'all 0.2s ease'
                }}
                onMouseEnter={(e) => {
                  e.currentTarget.style.transform = 'translateX(4px)'
                }}
                onMouseLeave={(e) => {
                  e.currentTarget.style.transform = 'translateX(0)'
                }}
              >
                <div style={{ display: 'flex', gap: 12 }}>
                  <Icon 
                    size={20} 
                    color={config.color} 
                    style={{ flexShrink: 0, marginTop: 2 }}
                  />
                  <div style={{ flex: 1 }}>
                    {/* 设备名和时间 */}
                    <div style={{
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'space-between',
                      marginBottom: 4
                    }}>
                      <h3 style={{
                        fontSize: 14,
                        fontWeight: 500,
                        color: 'var(--color-text-primary)',
                        margin: 0
                      }}>
                        {alert.equipment}
                      </h3>
                      <span style={{
                        fontSize: 12,
                        color: 'var(--color-text-dim)'
                      }}>
                        {alert.time}
                      </span>
                    </div>

                    {/* 告警消息 */}
                    <p style={{
                      fontSize: 13,
                      color: 'var(--color-text-secondary)',
                      margin: '0 0 8px 0',
                      lineHeight: 1.5
                    }}>
                      {alert.message}
                    </p>

                    {/* 标签 */}
                    <div style={{ display: 'flex', gap: 8 }}>
                      <span style={{
                        fontSize: 11,
                        padding: '2px 8px',
                        borderRadius: 20,
                        background: config.bg,
                        color: config.color,
                        border: `1px solid ${config.border}`
                      }}>
                        {config.label}
                      </span>
                      {alert.parameter && (
                        <span style={{
                          fontSize: 11,
                          padding: '2px 8px',
                          borderRadius: 20,
                          background: 'var(--color-bg-card)',
                          color: 'var(--color-text-muted)'
                        }}>
                          {alert.parameter}
                        </span>
                      )}
                    </div>
                  </div>
                </div>
              </div>
            )
          })
        )}
      </div>
    </div>
  )
}
