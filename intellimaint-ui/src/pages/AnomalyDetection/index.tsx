import { Brain, AlertCircle, TrendingDown, Activity } from 'lucide-react'
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
  AlgorithmList,
  HeatmapGrid,
  EventList,
  mockAlgorithms,
  mockEvents,
  generateMockHeatmapData
} from '../../components/common'

// 模拟异常分数时序数据
const mockAnomalyScoreData = Array.from({ length: 24 }, (_, i) => ({
  time: `${String(i).padStart(2, '0')}:00`,
  score: Math.sin(i / 4) * 0.3 + 0.4 + Math.random() * 0.1
}))

// 模拟多维特征数据
const mockFeatureData = Array.from({ length: 24 }, (_, i) => ({
  time: `${String(i).padStart(2, '0')}:00`,
  current: Math.sin(i / 6) * 0.3 + 0.3 + Math.random() * 0.1,
  temperature: Math.sin(i / 5 + 1) * 0.25 + 0.5 + Math.random() * 0.1,
  vibration: Math.sin(i / 4 + 2) * 0.2 + 0.2 + Math.random() * 0.1
}))

export default function AnomalyDetection() {
  // 指标数据
  const metrics = [
    { icon: Brain, title: 'AI模型准确率', value: '96.8', unit: '%', trend: 1.2, color: 'primary' as const },
    { icon: AlertCircle, title: '检测到异常', value: '23', unit: '次', trend: -8, color: 'warning' as const },
    { icon: TrendingDown, title: '误报率', value: '2.1', unit: '%', trend: -15, color: 'success' as const },
    { icon: Activity, title: '实时监控点', value: '156', unit: '个', trend: 5, color: 'primary' as const }
  ]

  // 热力图数据
  const heatmapData = generateMockHeatmapData(5, 12)

  return (
    <div>
      {/* 页面标题 */}
      <div style={{ marginBottom: 24 }}>
        <h1 style={{ fontSize: 24, fontWeight: 700, color: 'var(--color-text-primary)', margin: '0 0 8px 0' }}>
          智能感知层 - 异常检测
        </h1>
        <p style={{ fontSize: 14, color: 'var(--color-text-muted)', margin: 0 }}>
          基于多算法融合的实时异常检测与模式识别
        </p>
      </div>

      {/* 指标卡片 */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 24, marginBottom: 24 }}>
        {metrics.map((metric, idx) => (
          <MetricCard key={idx} {...metric} />
        ))}
      </div>

      {/* 算法状态 + 热力图 */}
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 24, marginBottom: 24 }}>
        <AlgorithmList algorithms={mockAlgorithms} />
        <HeatmapGrid data={heatmapData} />
      </div>

      {/* 时序图表 */}
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 24, marginBottom: 24 }}>
        <ChartCard title="异常分数时序图">
          <ResponsiveContainer width="100%" height="100%">
            <LineChart data={mockAnomalyScoreData}>
              <CartesianGrid strokeDasharray="3 3" stroke="var(--color-border)" />
              <XAxis dataKey="time" tick={{ fontSize: 12, fill: 'var(--color-text-muted)' }} stroke="var(--color-border)" />
              <YAxis tick={{ fontSize: 12, fill: 'var(--color-text-muted)' }} stroke="var(--color-border)" domain={[0, 1]} />
              <RechartsTooltip contentStyle={{ background: 'var(--color-bg-card)', border: '1px solid var(--color-border)', borderRadius: 8, color: 'var(--color-text-primary)' }} />
              <Legend />
              <Line type="monotone" dataKey="score" stroke="#3b82f6" dot={false} strokeWidth={2} name="score" />
            </LineChart>
          </ResponsiveContainer>
        </ChartCard>

        <ChartCard title="多维特征异常趋势">
          <ResponsiveContainer width="100%" height="100%">
            <LineChart data={mockFeatureData}>
              <CartesianGrid strokeDasharray="3 3" stroke="var(--color-border)" />
              <XAxis dataKey="time" tick={{ fontSize: 12, fill: 'var(--color-text-muted)' }} stroke="var(--color-border)" />
              <YAxis tick={{ fontSize: 12, fill: 'var(--color-text-muted)' }} stroke="var(--color-border)" domain={[0, 1]} />
              <RechartsTooltip contentStyle={{ background: 'var(--color-bg-card)', border: '1px solid var(--color-border)', borderRadius: 8, color: 'var(--color-text-primary)' }} />
              <Legend />
              <Line type="monotone" dataKey="current" stroke="#10b981" dot={false} strokeWidth={2} name="current" />
              <Line type="monotone" dataKey="temperature" stroke="#3b82f6" dot={false} strokeWidth={2} name="temperature" />
              <Line type="monotone" dataKey="vibration" stroke="#00BCD4" dot={false} strokeWidth={2} name="vibration" />
            </LineChart>
          </ResponsiveContainer>
        </ChartCard>
      </div>

      {/* 最近检测事件 */}
      <EventList events={mockEvents} />
    </div>
  )
}
