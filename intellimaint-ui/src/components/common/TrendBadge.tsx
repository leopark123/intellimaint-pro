import { TrendingUp, TrendingDown, Minus } from 'lucide-react'

interface TrendBadgeProps {
  value: number  // 正数上升，负数下降，0持平
  suffix?: string
  showIcon?: boolean
}

export default function TrendBadge({ 
  value, 
  suffix = '%',
  showIcon = true 
}: TrendBadgeProps) {
  const isUp = value > 0
  const isDown = value < 0
  const isNeutral = value === 0

  const bgColor = isUp 
    ? 'rgba(16, 185, 129, 0.15)' 
    : isDown 
    ? 'rgba(239, 68, 68, 0.15)' 
    : 'rgba(107, 114, 128, 0.15)'
  
  const textColor = isUp 
    ? '#10b981' 
    : isDown 
    ? '#ef4444' 
    : 'var(--color-text-muted)'

  const Icon = isUp ? TrendingUp : isDown ? TrendingDown : Minus

  return (
    <span style={{
      display: 'inline-flex',
      alignItems: 'center',
      gap: 4,
      padding: '4px 10px',
      background: bgColor,
      color: textColor,
      fontSize: 12,
      fontWeight: 500,
      borderRadius: 20,
      whiteSpace: 'nowrap'
    }}>
      {showIcon && <Icon size={14} />}
      <span>
        {isUp && '+'}{value}{suffix}
      </span>
    </span>
  )
}

// 简化版本 - 只显示箭头和数字
export function SimpleTrend({ value, suffix = '%' }: { value: number; suffix?: string }) {
  const isUp = value > 0
  const isDown = value < 0

  const textColor = isUp ? '#10b981' : isDown ? '#ef4444' : 'var(--color-text-muted)'
  const arrow = isUp ? '↑' : isDown ? '↓' : '→'

  return (
    <span style={{ color: textColor, fontSize: 12, fontWeight: 500 }}>
      {arrow} {Math.abs(value)}{suffix}
    </span>
  )
}
