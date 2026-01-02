import type { LucideIcon } from 'lucide-react'
import { TrendingUp, Clock, Package } from 'lucide-react'

interface Strategy {
  id: string
  title: string
  icon: LucideIcon
  iconBg: string
  iconColor: string
  description: string
  metric: string
  metricColor?: string
}

interface StrategyCardProps {
  strategy: Strategy
}

export default function StrategyCard({ strategy }: StrategyCardProps) {
  const Icon = strategy.icon

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
      {/* 标题和图标 */}
      <div style={{
        display: 'flex',
        alignItems: 'center',
        gap: 12,
        marginBottom: 16
      }}>
        <div style={{
          width: 40,
          height: 40,
          borderRadius: 10,
          background: strategy.iconBg,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center'
        }}>
          <Icon size={20} color={strategy.iconColor} />
        </div>
        <h4 style={{
          fontSize: 15,
          fontWeight: 600,
          color: 'var(--color-text-primary)',
          margin: 0
        }}>
          {strategy.title}
        </h4>
      </div>

      {/* 描述 */}
      <p style={{
        fontSize: 13,
        color: 'var(--color-text-muted)',
        margin: '0 0 12px 0',
        lineHeight: 1.6
      }}>
        {strategy.description}
      </p>

      {/* 指标 */}
      <div style={{
        fontSize: 13,
        fontWeight: 500,
        color: strategy.metricColor || '#10b981'
      }}>
        {strategy.metric}
      </div>
    </div>
  )
}

// 预定义策略数据
export const mockStrategies: Strategy[] = [
  {
    id: '1',
    title: '成本优化',
    icon: TrendingUp,
    iconBg: 'rgba(16, 185, 129, 0.15)',
    iconColor: '#10b981',
    description: '通过预测性维护,本月可减少非计划停机3次',
    metric: '预计节省: ¥128,000',
    metricColor: '#10b981'
  },
  {
    id: '2',
    title: '时间优化',
    icon: Clock,
    iconBg: 'rgba(59, 130, 246, 0.15)',
    iconColor: '#3b82f6',
    description: '建议在生产低谷期(周末)进行3项维护',
    metric: '减少影响: 85%',
    metricColor: '#3b82f6'
  },
  {
    id: '3',
    title: '库存优化',
    icon: Package,
    iconBg: 'rgba(245, 158, 11, 0.15)',
    iconColor: '#f59e0b',
    description: '预测未来30天需要采购5种备件',
    metric: '库存周转: +20%',
    metricColor: '#f59e0b'
  }
]
