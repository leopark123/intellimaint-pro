import type { LucideIcon } from 'lucide-react'

interface MetricCardProps {
  icon: LucideIcon
  title: string
  value: string | number
  unit?: string
  trend?: number
  color?: 'primary' | 'success' | 'warning' | 'danger'
}

const colorConfig = {
  primary: {
    gradient: 'linear-gradient(90deg, rgba(26, 35, 126, 0.15) 0%, rgba(26, 35, 126, 0.02) 100%)',
    borderLeft: '#1A237E',
    iconBg: 'rgba(26, 35, 126, 0.2)',
    iconColor: '#00BCD4'
  },
  success: {
    gradient: 'linear-gradient(90deg, rgba(16, 185, 129, 0.15) 0%, rgba(16, 185, 129, 0.02) 100%)',
    borderLeft: '#10b981',
    iconBg: 'rgba(16, 185, 129, 0.2)',
    iconColor: '#10b981'
  },
  warning: {
    gradient: 'linear-gradient(90deg, rgba(245, 158, 11, 0.15) 0%, rgba(245, 158, 11, 0.02) 100%)',
    borderLeft: '#f59e0b',
    iconBg: 'rgba(245, 158, 11, 0.2)',
    iconColor: '#f59e0b'
  },
  danger: {
    gradient: 'linear-gradient(90deg, rgba(239, 68, 68, 0.15) 0%, rgba(239, 68, 68, 0.02) 100%)',
    borderLeft: '#ef4444',
    iconBg: 'rgba(239, 68, 68, 0.2)',
    iconColor: '#ef4444'
  }
}

export default function MetricCard({ 
  icon: Icon, 
  title, 
  value, 
  unit, 
  trend, 
  color = 'primary' 
}: MetricCardProps) {
  const config = colorConfig[color]

  return (
    <div
      style={{
        background: config.gradient,
        borderLeft: `4px solid ${config.borderLeft}`,
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
      {/* 顶部区域：图标和趋势 */}
      <div style={{ 
        display: 'flex', 
        alignItems: 'flex-start', 
        justifyContent: 'space-between', 
        marginBottom: 16 
      }}>
        <div style={{
          padding: 12,
          background: config.iconBg,
          borderRadius: 10
        }}>
          <Icon size={24} color={config.iconColor} />
        </div>
        
        {trend !== undefined && (
          <span style={{
            fontSize: 12,
            padding: '4px 10px',
            borderRadius: 20,
            background: trend >= 0 ? 'rgba(16, 185, 129, 0.2)' : 'rgba(239, 68, 68, 0.2)',
            color: trend >= 0 ? 'var(--color-success)' : 'var(--color-danger)',
            fontWeight: 500
          }}>
            {trend >= 0 ? '↑' : '↓'} {Math.abs(trend)}%
          </span>
        )}
      </div>

      {/* 标题 */}
      <h3 style={{ 
        fontSize: 14, 
        color: 'var(--color-text-muted)', 
        marginBottom: 8,
        fontWeight: 400
      }}>
        {title}
      </h3>

      {/* 数值 */}
      <div style={{ display: 'flex', alignItems: 'baseline', gap: 8 }}>
        <span style={{ 
          fontSize: 32, 
          fontWeight: 700, 
          color: 'var(--color-text-primary)',
          lineHeight: 1
        }}>
          {value}
        </span>
        {unit && (
          <span style={{ 
            fontSize: 14, 
            color: 'var(--color-text-muted)' 
          }}>
            {unit}
          </span>
        )}
      </div>
    </div>
  )
}
