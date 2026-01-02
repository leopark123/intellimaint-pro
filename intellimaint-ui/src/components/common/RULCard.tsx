import { Progress } from 'antd'

interface RULCardProps {
  device: string
  health: number
  rul: number  // 剩余寿命（天）
  maintenanceDate: string
  status: 'good' | 'attention' | 'critical'
}

const statusConfig = {
  good: { color: '#10b981', label: '状态良好', dot: true },
  attention: { color: '#f59e0b', label: '建议安排维护', dot: true },
  critical: { color: '#ef4444', label: '需要立即关注', dot: true }
}

export default function RULCard({ 
  device, 
  health, 
  rul, 
  maintenanceDate,
  status 
}: RULCardProps) {
  const config = statusConfig[status]
  
  // 根据健康度确定进度条颜色
  const progressColor = health >= 80 ? '#10b981' : health >= 60 ? '#f59e0b' : '#ef4444'

  return (
    <div style={{
      background: 'var(--color-bg-dark)',
      border: '1px solid var(--color-border)',
      borderRadius: 12,
      padding: 20,
      transition: 'all 0.2s ease'
    }}
    onMouseEnter={(e) => {
      e.currentTarget.style.borderColor = 'var(--color-border-light)'
      e.currentTarget.style.transform = 'translateY(-2px)'
    }}
    onMouseLeave={(e) => {
      e.currentTarget.style.borderColor = 'var(--color-border)'
      e.currentTarget.style.transform = 'translateY(0)'
    }}
    >
      {/* 标题和健康度 */}
      <div style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        marginBottom: 16
      }}>
        <h4 style={{
          fontSize: 15,
          fontWeight: 600,
          color: 'var(--color-text-primary)',
          margin: 0
        }}>
          {device}
        </h4>
        <span style={{
          fontSize: 24,
          fontWeight: 700,
          color: progressColor
        }}>
          {health}%
        </span>
      </div>

      {/* 健康度进度条 */}
      <div style={{ marginBottom: 12 }}>
        <div style={{
          display: 'flex',
          justifyContent: 'space-between',
          marginBottom: 4
        }}>
          <span style={{ fontSize: 12, color: 'var(--color-text-dim)' }}>健康度</span>
          <span style={{ fontSize: 12, color: 'var(--color-text-muted)' }}>{health}%</span>
        </div>
        <Progress
          percent={health}
          showInfo={false}
          strokeColor={progressColor}
          trailColor="var(--color-border)"
          size="small"
        />
      </div>

      {/* 剩余寿命 */}
      <div style={{
        display: 'flex',
        justifyContent: 'space-between',
        marginBottom: 8
      }}>
        <span style={{ fontSize: 13, color: 'var(--color-text-dim)' }}>预计剩余寿命</span>
        <span style={{ 
          fontSize: 14, 
          fontWeight: 600, 
          color: rul <= 30 ? 'var(--color-danger)' : rul <= 90 ? 'var(--color-warning)' : 'var(--color-text-primary)' 
        }}>
          {rul} 天
        </span>
      </div>

      {/* 建议维护时间 */}
      <div style={{
        display: 'flex',
        justifyContent: 'space-between',
        marginBottom: 12
      }}>
        <span style={{ fontSize: 13, color: 'var(--color-text-dim)' }}>建议维护时间</span>
        <span style={{ fontSize: 14, color: 'var(--color-text-primary)' }}>{maintenanceDate}</span>
      </div>

      {/* 状态标签 */}
      <div style={{
        display: 'flex',
        alignItems: 'center',
        gap: 8
      }}>
        <div style={{
          width: 8,
          height: 8,
          borderRadius: '50%',
          background: config.color
        }} />
        <span style={{ fontSize: 13, color: config.color }}>
          {config.label}
        </span>
      </div>
    </div>
  )
}

// 模拟 RUL 数据
export const mockRULData: RULCardProps[] = [
  { device: '电机M-301', health: 94, rul: 156, maintenanceDate: '2026/6/5', status: 'good' },
  { device: '泵P-102', health: 76, rul: 45, maintenanceDate: '2026/2/14', status: 'attention' },
  { device: '压缩机C-201', health: 92, rul: 187, maintenanceDate: '2026/7/6', status: 'good' },
  { device: '风机F-401', health: 58, rul: 12, maintenanceDate: '2026/1/12', status: 'critical' },
  { device: '减速机G-505', health: 85, rul: 98, maintenanceDate: '2026/4/8', status: 'good' },
  { device: '搅拌机M-203', health: 88, rul: 134, maintenanceDate: '2026/5/14', status: 'good' }
]
