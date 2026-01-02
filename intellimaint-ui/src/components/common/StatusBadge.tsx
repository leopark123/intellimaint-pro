type StatusType = 'success' | 'warning' | 'danger' | 'info' | 'default'

interface StatusBadgeProps {
  status: StatusType
  text: string
  size?: 'small' | 'default'
  dot?: boolean
}

const statusConfig: Record<StatusType, { bg: string; color: string; dotColor: string }> = {
  success: {
    bg: 'rgba(16, 185, 129, 0.15)',
    color: '#10b981',
    dotColor: '#10b981'
  },
  warning: {
    bg: 'rgba(245, 158, 11, 0.15)',
    color: '#f59e0b',
    dotColor: '#f59e0b'
  },
  danger: {
    bg: 'rgba(239, 68, 68, 0.15)',
    color: '#ef4444',
    dotColor: '#ef4444'
  },
  info: {
    bg: 'rgba(59, 130, 246, 0.15)',
    color: '#3b82f6',
    dotColor: '#3b82f6'
  },
  default: {
    bg: 'rgba(107, 114, 128, 0.15)',
    color: 'var(--color-text-muted)',
    dotColor: '#6b7280'
  }
}

export default function StatusBadge({ 
  status, 
  text, 
  size = 'default',
  dot = false 
}: StatusBadgeProps) {
  const config = statusConfig[status]
  const isSmall = size === 'small'

  return (
    <span style={{
      display: 'inline-flex',
      alignItems: 'center',
      gap: 6,
      padding: isSmall ? '2px 8px' : '4px 12px',
      background: config.bg,
      color: config.color,
      fontSize: isSmall ? 11 : 12,
      fontWeight: 500,
      borderRadius: 20,
      whiteSpace: 'nowrap'
    }}>
      {dot && (
        <span style={{
          width: 6,
          height: 6,
          borderRadius: '50%',
          background: config.dotColor
        }} />
      )}
      {text}
    </span>
  )
}

// 预设的状态标签
export function HealthyBadge() {
  return <StatusBadge status="success" text="状态良好" dot />
}

export function WarningBadge() {
  return <StatusBadge status="warning" text="建议安排维护" dot />
}

export function CriticalBadge() {
  return <StatusBadge status="danger" text="需要立即关注" dot />
}

export function RunningBadge() {
  return <StatusBadge status="success" text="运行中" dot />
}

export function StoppedBadge() {
  return <StatusBadge status="default" text="已停止" dot />
}

// 优先级标签
export function HighPriorityBadge() {
  return <StatusBadge status="danger" text="高优先级" />
}

export function MediumPriorityBadge() {
  return <StatusBadge status="warning" text="中优先级" />
}

export function LowPriorityBadge() {
  return <StatusBadge status="info" text="低优先级" />
}

// 严重程度标签
export function SevereBadge() {
  return <StatusBadge status="danger" text="严重" />
}

export function ModerateBadge() {
  return <StatusBadge status="warning" text="中等" />
}

export function MinorBadge() {
  return <StatusBadge status="info" text="轻微" />
}
