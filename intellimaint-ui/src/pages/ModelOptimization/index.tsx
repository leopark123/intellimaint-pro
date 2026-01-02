import { Target, TrendingUp, Zap, RefreshCw } from 'lucide-react'
import {
  LineChart,
  Line,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip as RechartsTooltip,
  ResponsiveContainer,
  Legend
} from 'recharts'
import {
  MetricCard,
  ChartCard,
  ExperimentCard,
  PipelineSteps,
  mockExperiments,
  mockPipelineSteps
} from '../../components/common'

// 模拟模型性能趋势数据
const mockPerformanceData = Array.from({ length: 20 }, (_, i) => ({
  epoch: i + 1,
  accuracy: 80 + i * 0.8 + Math.random() * 2,
  precision: 78 + i * 0.85 + Math.random() * 2,
  recall: 75 + i * 0.9 + Math.random() * 2
}))

// 模拟训练损失曲线
const mockLossData = Array.from({ length: 20 }, (_, i) => ({
  epoch: i + 1,
  loss: 0.5 * Math.exp(-i / 8) + 0.1 + Math.random() * 0.02
}))

// 超参数优化数据
const hyperparamData = [
  { name: 'Learning Rate', current: '0.001', suggested: '0.0015', improvement: '+2.3%' },
  { name: 'Batch Size', current: '32', suggested: '64', improvement: '+1.8%' },
  { name: 'Hidden Units', current: '128', suggested: '256', improvement: '+3.1%' },
  { name: 'Dropout Rate', current: '0.2', suggested: '0.3', improvement: '+0.9%' }
]

export default function ModelOptimization() {
  // 指标数据
  const metrics = [
    { icon: Target, title: '模型准确率', value: '96.8', unit: '%', trend: 2.1, color: 'primary' as const },
    { icon: TrendingUp, title: '优化迭代', value: '24', unit: '次', trend: 8, color: 'success' as const },
    { icon: Zap, title: '推理速度', value: '12', unit: 'ms', trend: -15, color: 'success' as const },
    { icon: RefreshCw, title: '自动训练', value: '7', unit: '天/次', trend: 0, color: 'primary' as const }
  ]

  return (
    <div>
      {/* 页面标题 */}
      <div style={{ marginBottom: 24 }}>
        <h1 style={{ fontSize: 24, fontWeight: 700, color: 'var(--color-text-primary)', margin: '0 0 8px 0' }}>
          持续优化层 - 模型优化
        </h1>
        <p style={{ fontSize: 14, color: 'var(--color-text-muted)', margin: 0 }}>
          MLOps流水线与自动化模型训练优化
        </p>
      </div>

      {/* 指标卡片 */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 24, marginBottom: 24 }}>
        {metrics.map((metric, idx) => (
          <MetricCard key={idx} {...metric} />
        ))}
      </div>

      {/* 性能趋势图 */}
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 24, marginBottom: 24 }}>
        <ChartCard title="模型性能趋势">
          <ResponsiveContainer width="100%" height="100%">
            <LineChart data={mockPerformanceData}>
              <CartesianGrid strokeDasharray="3 3" stroke="var(--color-border)" />
              <XAxis dataKey="epoch" tick={{ fontSize: 12, fill: 'var(--color-text-muted)' }} stroke="var(--color-border)" />
              <YAxis tick={{ fontSize: 12, fill: 'var(--color-text-muted)' }} stroke="var(--color-border)" domain={[70, 100]} />
              <RechartsTooltip contentStyle={{ background: 'var(--color-bg-card)', border: '1px solid var(--color-border)', borderRadius: 8, color: 'var(--color-text-primary)' }} />
              <Legend />
              <Line type="monotone" dataKey="accuracy" stroke="#3b82f6" dot={false} strokeWidth={2} />
              <Line type="monotone" dataKey="precision" stroke="#10b981" dot={false} strokeWidth={2} />
              <Line type="monotone" dataKey="recall" stroke="#00BCD4" dot={false} strokeWidth={2} />
            </LineChart>
          </ResponsiveContainer>
        </ChartCard>

        <ChartCard title="训练损失曲线">
          <ResponsiveContainer width="100%" height="100%">
            <LineChart data={mockLossData}>
              <CartesianGrid strokeDasharray="3 3" stroke="var(--color-border)" />
              <XAxis dataKey="epoch" tick={{ fontSize: 12, fill: 'var(--color-text-muted)' }} stroke="var(--color-border)" />
              <YAxis tick={{ fontSize: 12, fill: 'var(--color-text-muted)' }} stroke="var(--color-border)" domain={[0, 0.6]} />
              <RechartsTooltip contentStyle={{ background: 'var(--color-bg-card)', border: '1px solid var(--color-border)', borderRadius: 8, color: 'var(--color-text-primary)' }} />
              <Legend />
              <Line type="monotone" dataKey="loss" stroke="#3b82f6" dot={false} strokeWidth={2} />
            </LineChart>
          </ResponsiveContainer>
        </ChartCard>
      </div>

      {/* A/B 测试实验 */}
      <div style={{ marginBottom: 24 }}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
          {mockExperiments.map((exp) => (
            <ExperimentCard key={exp.id} experiment={exp} />
          ))}
        </div>
      </div>

      {/* 训练流水线 + 超参数优化 */}
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 24 }}>
        <PipelineSteps steps={mockPipelineSteps} />
        
        {/* 超参数优化建议 */}
        <div style={{ background: 'var(--color-bg-dark)', border: '1px solid var(--color-border)', borderRadius: 12, padding: 24 }}>
          <h3 style={{ fontSize: 16, fontWeight: 600, color: 'var(--color-text-primary)', margin: '0 0 16px 0' }}>
            超参数优化
          </h3>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
            {hyperparamData.map((param, idx) => (
              <div key={idx} style={{ background: 'var(--color-bg-card)', border: '1px solid var(--color-border)', borderRadius: 8, padding: 16, display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                <div>
                  <div style={{ fontSize: 14, fontWeight: 500, color: 'var(--color-text-primary)', marginBottom: 4 }}>{param.name}</div>
                  <div style={{ fontSize: 13, color: 'var(--color-text-muted)' }}>
                    当前: {param.current} <span style={{ color: 'var(--color-text-dim)', margin: '0 8px' }}>→</span> 建议: {param.suggested}
                  </div>
                </div>
                <span style={{ padding: '4px 10px', background: 'rgba(16, 185, 129, 0.15)', color: 'var(--color-success)', fontSize: 12, fontWeight: 500, borderRadius: 20 }}>
                  {param.improvement}
                </span>
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  )
}
