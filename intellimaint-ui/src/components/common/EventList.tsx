import { Progress } from 'antd'

interface DetectionEvent {
  id: string
  time: string
  device: string
  type: string
  confidence: number
  level: 'high' | 'medium' | 'low'
}

interface EventListProps {
  events: DetectionEvent[]
  title?: string
}

const levelConfig = {
  high: { color: '#ef4444', label: '高', bg: 'rgba(239, 68, 68, 0.15)' },
  medium: { color: '#f59e0b', label: '中', bg: 'rgba(245, 158, 11, 0.15)' },
  low: { color: '#10b981', label: '低', bg: 'rgba(16, 185, 129, 0.15)' }
}

export default function EventList({ 
  events, 
  title = '最近检测事件' 
}: EventListProps) {
  return (
    <div style={{
      background: 'var(--color-bg-dark)',
      border: '1px solid var(--color-border)',
      borderRadius: 12,
      padding: 24
    }}>
      <h3 style={{
        fontSize: 16,
        fontWeight: 600,
        color: 'var(--color-text-primary)',
        margin: '0 0 16px 0'
      }}>
        {title}
      </h3>

      <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
        {events.map((event) => {
          const config = levelConfig[event.level]
          
          return (
            <div
              key={event.id}
              style={{
                background: 'var(--color-bg-card)',
                border: '1px solid var(--color-border)',
                borderRadius: 8,
                padding: 16,
                display: 'flex',
                alignItems: 'center',
                gap: 16
              }}
            >
              {/* 时间 */}
              <div style={{
                width: 80,
                fontSize: 13,
                color: 'var(--color-text-dim)',
                flexShrink: 0
              }}>
                {event.time}
              </div>

              {/* 设备和类型 */}
              <div style={{ flex: 1 }}>
                <div style={{
                  fontSize: 14,
                  fontWeight: 500,
                  color: 'var(--color-text-primary)',
                  marginBottom: 2
                }}>
                  {event.device}
                </div>
                <div style={{
                  fontSize: 12,
                  color: 'var(--color-text-muted)'
                }}>
                  {event.type}
                </div>
              </div>

              {/* 置信度 */}
              <div style={{ width: 140, flexShrink: 0 }}>
                <div style={{
                  fontSize: 12,
                  color: 'var(--color-text-muted)',
                  marginBottom: 4,
                  textAlign: 'right'
                }}>
                  置信度: {event.confidence.toFixed(1)}%
                </div>
                <Progress
                  percent={event.confidence}
                  showInfo={false}
                  strokeColor={config.color}
                  trailColor="var(--color-border)"
                  size="small"
                />
              </div>

              {/* 级别标签 */}
              <span style={{
                padding: '4px 12px',
                background: config.bg,
                color: config.color,
                fontSize: 12,
                fontWeight: 500,
                borderRadius: 20,
                flexShrink: 0
              }}>
                {config.label}
              </span>
            </div>
          )
        })}
      </div>
    </div>
  )
}

// 模拟事件数据
export const mockEvents: DetectionEvent[] = [
  { id: '1', time: '14:23:45', device: '电机M-301', type: '振动异常', confidence: 92.0, level: 'high' },
  { id: '2', time: '14:18:12', device: '泵P-102', type: '温度异常', confidence: 87.0, level: 'medium' },
  { id: '3', time: '14:05:33', device: '压缩机C-201', type: '电流波动', confidence: 78.0, level: 'low' },
  { id: '4', time: '13:52:18', device: '风机F-401', type: '轴承磨损', confidence: 94.0, level: 'high' }
]
