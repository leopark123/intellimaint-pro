import { Wrench, Clock, Package, DollarSign, Plus, Download } from 'lucide-react'
import { Button } from 'antd'
import {
  MetricCard,
  RULCard,
  WorkOrderCard,
  StrategyCard,
  mockRULData,
  mockWorkOrders,
  mockStrategies
} from '../../components/common'

export default function PredictiveMaintenance() {
  // 指标数据
  const metrics = [
    { icon: Wrench, title: '预测性维护', value: '15', unit: '项', trend: -12, color: 'primary' as const },
    { icon: Clock, title: '平均RUL', value: '87', unit: '天', trend: 8, color: 'success' as const },
    { icon: Package, title: '备件需求', value: '8', unit: '种', trend: -5, color: 'warning' as const },
    { icon: DollarSign, title: '预计节省', value: '42', unit: '万元', trend: 15, color: 'success' as const }
  ]

  return (
    <div>
      {/* 页面标题 */}
      <div style={{ marginBottom: 24 }}>
        <h1 style={{ fontSize: 24, fontWeight: 700, color: 'var(--color-text-primary)', margin: '0 0 8px 0' }}>
          信息处理层 - 预测性维护
        </h1>
        <p style={{ fontSize: 14, color: 'var(--color-text-muted)', margin: 0 }}>
          基于RUL预测的智能维护决策与工单生成
        </p>
      </div>

      {/* 指标卡片 */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 24, marginBottom: 24 }}>
        {metrics.map((metric, idx) => (
          <MetricCard key={idx} {...metric} />
        ))}
      </div>

      {/* 设备剩余寿命预测 */}
      <div style={{ marginBottom: 24 }}>
        <h2 style={{ fontSize: 16, fontWeight: 600, color: 'var(--color-text-primary)', margin: '0 0 16px 0' }}>
          设备剩余寿命预测
        </h2>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 16 }}>
          {mockRULData.map((rul, idx) => (
            <RULCard key={idx} {...rul} />
          ))}
        </div>
      </div>

      {/* 智能工单管理 */}
      <div style={{ marginBottom: 24 }}>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 16 }}>
          <h2 style={{ fontSize: 16, fontWeight: 600, color: 'var(--color-text-primary)', margin: 0 }}>
            智能工单管理
          </h2>
          <div style={{ display: 'flex', gap: 12 }}>
            <Button 
              type="primary" 
              icon={<Plus size={16} />}
              style={{ 
                background: '#1A237E',
                borderColor: '#1A237E',
                display: 'flex',
                alignItems: 'center',
                gap: 8
              }}
            >
              生成新工单
            </Button>
            <Button 
              icon={<Download size={16} />}
              style={{ 
                background: 'var(--color-bg-card)',
                borderColor: 'var(--color-border)',
                color: 'var(--color-text-primary)',
                display: 'flex',
                alignItems: 'center',
                gap: 8
              }}
            >
              导出报告
            </Button>
          </div>
        </div>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, 1fr)', gap: 16 }}>
          {mockWorkOrders.map((order) => (
            <WorkOrderCard key={order.id} order={order} />
          ))}
        </div>
      </div>

      {/* 维护策略优化建议 */}
      <div>
        <h2 style={{ fontSize: 16, fontWeight: 600, color: 'var(--color-text-primary)', margin: '0 0 16px 0' }}>
          维护策略优化建议
        </h2>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 16 }}>
          {mockStrategies.map((strategy) => (
            <StrategyCard key={strategy.id} strategy={strategy} />
          ))}
        </div>
      </div>
    </div>
  )
}
