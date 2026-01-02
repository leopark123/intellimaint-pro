import { Activity, Zap, Thermometer, Gauge } from 'lucide-react'

interface Equipment {
  id: string
  name: string
  location?: string
  status: 'normal' | 'warning' | 'critical'
  vibration?: number
  temperature?: number
  current?: number
  health: number
  rul?: number // 剩余使用寿命（天）
}

interface EquipmentStatusProps {
  equipment: Equipment[]
  title?: string
}

const statusConfig = {
  normal: { color: '#10b981', label: '正常' },
  warning: { color: '#f59e0b', label: '警告' },
  critical: { color: '#ef4444', label: '异常' }
}

export default function EquipmentStatus({ equipment, title = '设备健康状态' }: EquipmentStatusProps) {
  return (
    <div style={{
      background: 'var(--color-bg-dark)',
      border: '1px solid var(--color-border)',
      borderRadius: 12,
      padding: 24
    }}>
      <h2 style={{
        fontSize: 16,
        fontWeight: 600,
        color: 'var(--color-text-primary)',
        margin: '0 0 16px 0'
      }}>
        {title}
      </h2>

      <div style={{
        display: 'grid',
        gridTemplateColumns: 'repeat(auto-fill, minmax(300px, 1fr))',
        gap: 16
      }}>
        {equipment.map((item) => {
          const status = statusConfig[item.status]

          return (
            <div
              key={item.id}
              style={{
                background: 'var(--color-bg-subtle)',
                border: '1px solid var(--color-border)',
                borderRadius: 8,
                padding: 16,
                transition: 'all 0.2s ease'
              }}
              onMouseEnter={(e) => {
                e.currentTarget.style.transform = 'scale(1.02)'
                e.currentTarget.style.borderColor = 'var(--color-border-light)'
              }}
              onMouseLeave={(e) => {
                e.currentTarget.style.transform = 'scale(1)'
                e.currentTarget.style.borderColor = 'var(--color-border)'
              }}
            >
              {/* 设备名称和状态 */}
              <div style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                marginBottom: 12
              }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                  <div style={{
                    width: 10,
                    height: 10,
                    borderRadius: '50%',
                    background: status.color,
                    boxShadow: `0 0 8px ${status.color}`,
                    animation: 'pulse 2s infinite'
                  }} />
                  <h3 style={{
                    fontSize: 15,
                    fontWeight: 500,
                    color: 'var(--color-text-primary)',
                    margin: 0
                  }}>
                    {item.name}
                  </h3>
                </div>
                {item.location && (
                  <span style={{
                    fontSize: 12,
                    color: 'var(--color-text-dim)'
                  }}>
                    {item.location}
                  </span>
                )}
              </div>

              {/* 参数指标 */}
              <div style={{
                display: 'grid',
                gridTemplateColumns: '1fr 1fr',
                gap: 12
              }}>
                {item.vibration !== undefined && (
                  <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                    <Activity size={16} color="#3b82f6" />
                    <div>
                      <p style={{ fontSize: 11, color: 'var(--color-text-dim)', margin: 0 }}>振动</p>
                      <p style={{ fontSize: 13, color: 'var(--color-text-primary)', margin: 0 }}>{item.vibration} mm/s</p>
                    </div>
                  </div>
                )}

                {item.temperature !== undefined && (
                  <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                    <Thermometer size={16} color="#f59e0b" />
                    <div>
                      <p style={{ fontSize: 11, color: 'var(--color-text-dim)', margin: 0 }}>温度</p>
                      <p style={{ fontSize: 13, color: 'var(--color-text-primary)', margin: 0 }}>{item.temperature}°C</p>
                    </div>
                  </div>
                )}

                {item.current !== undefined && (
                  <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                    <Zap size={16} color="#eab308" />
                    <div>
                      <p style={{ fontSize: 11, color: 'var(--color-text-dim)', margin: 0 }}>电流</p>
                      <p style={{ fontSize: 13, color: 'var(--color-text-primary)', margin: 0 }}>{item.current} A</p>
                    </div>
                  </div>
                )}

                <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                  <Gauge size={16} color="#10b981" />
                  <div>
                    <p style={{ fontSize: 11, color: 'var(--color-text-dim)', margin: 0 }}>健康度</p>
                    <p style={{ fontSize: 13, color: 'var(--color-text-primary)', margin: 0 }}>{item.health}%</p>
                  </div>
                </div>
              </div>

              {/* 剩余寿命 */}
              {item.rul !== undefined && (
                <div style={{
                  marginTop: 12,
                  paddingTop: 12,
                  borderTop: '1px solid var(--color-border)',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between'
                }}>
                  <span style={{ fontSize: 12, color: 'var(--color-text-dim)' }}>预计剩余寿命</span>
                  <span style={{ fontSize: 13, fontWeight: 500, color: 'var(--color-text-primary)' }}>{item.rul} 天</span>
                </div>
              )}
            </div>
          )
        })}
      </div>
    </div>
  )
}
