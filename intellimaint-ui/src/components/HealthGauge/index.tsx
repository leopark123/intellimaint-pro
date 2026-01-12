import { CSSProperties, useMemo } from 'react'

interface HealthGaugeProps {
  value: number // 0-100
  size?: number // 默认 160
  strokeWidth?: number // 默认 12
  showLabel?: boolean
  label?: string
}

const getColor = (value: number): string => {
  if (value >= 80) return '#52c41a' // 绿色 - Healthy
  if (value >= 60) return '#faad14' // 黄色 - Attention
  if (value >= 40) return '#fa8c16' // 橙色 - Warning
  return '#f5222d' // 红色 - Critical
}

const getLevel = (value: number): string => {
  if (value >= 80) return '健康'
  if (value >= 60) return '注意'
  if (value >= 40) return '警告'
  return '危险'
}

export default function HealthGauge({
  value,
  size = 160,
  strokeWidth = 12,
  showLabel = true,
  label
}: HealthGaugeProps) {
  const clampedValue = Math.max(0, Math.min(100, value))
  const color = useMemo(() => getColor(clampedValue), [clampedValue])
  const levelText = useMemo(() => getLevel(clampedValue), [clampedValue])

  // 计算 SVG 参数
  const radius = (size - strokeWidth) / 2
  const circumference = 2 * Math.PI * radius
  const progress = (clampedValue / 100) * circumference
  const offset = circumference - progress

  // 容器样式
  const containerStyle: CSSProperties = {
    position: 'relative',
    width: size,
    height: size,
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center'
  }

  // 中心文字样式
  const centerStyle: CSSProperties = {
    position: 'absolute',
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center'
  }

  const valueStyle: CSSProperties = {
    fontSize: size * 0.25,
    fontWeight: 'bold',
    color: color,
    lineHeight: 1
  }

  const labelStyle: CSSProperties = {
    fontSize: size * 0.1,
    color: '#8c8c8c',
    marginTop: 4
  }

  const levelStyle: CSSProperties = {
    fontSize: size * 0.09,
    color: color,
    marginTop: 2,
    fontWeight: 500
  }

  return (
    <div style={containerStyle}>
      <svg
        width={size}
        height={size}
        viewBox={`0 0 ${size} ${size}`}
        style={{ transform: 'rotate(-90deg)' }}
      >
        {/* 背景圆 */}
        <circle
          cx={size / 2}
          cy={size / 2}
          r={radius}
          fill="none"
          stroke="#f0f0f0"
          strokeWidth={strokeWidth}
        />
        {/* 进度圆 */}
        <circle
          cx={size / 2}
          cy={size / 2}
          r={radius}
          fill="none"
          stroke={color}
          strokeWidth={strokeWidth}
          strokeLinecap="round"
          strokeDasharray={circumference}
          strokeDashoffset={offset}
          style={{
            transition: 'stroke-dashoffset 0.5s ease, stroke 0.5s ease'
          }}
        />
      </svg>
      <div style={centerStyle}>
        <span style={valueStyle}>{clampedValue}</span>
        {showLabel && (
          <>
            <span style={labelStyle}>{label || '健康指数'}</span>
            <span style={levelStyle}>{levelText}</span>
          </>
        )}
      </div>
    </div>
  )
}

// 导出工具函数
export { getColor, getLevel }
