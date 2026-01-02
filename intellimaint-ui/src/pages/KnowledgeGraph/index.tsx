import { Network, BookOpen, Lightbulb, Search } from 'lucide-react'
import { Input, Button, Progress } from 'antd'
import {
  MetricCard,
  CaseCard,
  mockCases
} from '../../components/common'

// 热门故障类型数据
const faultTypeStats = [
  { name: '轴承磨损', count: 87 },
  { name: '电机过热', count: 64 },
  { name: '振动异常', count: 52 },
  { name: '密封泄漏', count: 41 }
]

const maxCount = Math.max(...faultTypeStats.map(f => f.count))

export default function KnowledgeGraph() {
  // 指标数据
  const metrics = [
    { icon: Network, title: '知识节点', value: '1,247', unit: '个', trend: 12, color: 'primary' as const },
    { icon: BookOpen, title: '故障案例', value: '386', unit: '条', trend: 8, color: 'success' as const },
    { icon: Lightbulb, title: '解决方案', value: '542', unit: '个', trend: 15, color: 'warning' as const },
    { icon: Search, title: '查询次数', value: '2,341', unit: '次', trend: 23, color: 'primary' as const }
  ]

  return (
    <div>
      {/* 页面标题 */}
      <div style={{ marginBottom: 24 }}>
        <h1 style={{ fontSize: 24, fontWeight: 700, color: 'var(--color-text-primary)', margin: '0 0 8px 0' }}>
          知识图谱 - 智能诊断
        </h1>
        <p style={{ fontSize: 14, color: 'var(--color-text-muted)', margin: 0 }}>
          基于工业知识图谱的故障诊断与根因分析
        </p>
      </div>

      {/* 指标卡片 */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 24, marginBottom: 24 }}>
        {metrics.map((metric, idx) => (
          <MetricCard key={idx} {...metric} />
        ))}
      </div>

      {/* 搜索框 */}
      <div style={{ 
        background: 'var(--color-bg-dark)', 
        border: '1px solid var(--color-border)', 
        borderRadius: 12, 
        padding: 24, 
        marginBottom: 24 
      }}>
        <div style={{ display: 'flex', gap: 12 }}>
          <Input
            prefix={<Search size={18} color="#6b7280" />}
            placeholder="搜索故障现象、设备型号或解决方案..."
            style={{
              flex: 1,
              background: 'var(--color-bg-card)',
              border: '1px solid var(--color-border)',
              borderRadius: 8,
              color: 'var(--color-text-primary)',
              height: 48
            }}
          />
          <Button
            type="primary"
            style={{
              background: '#1A237E',
              borderColor: '#1A237E',
              height: 48,
              paddingLeft: 24,
              paddingRight: 24
            }}
          >
            搜索
          </Button>
        </div>
      </div>

      {/* 图谱可视化 + 热门故障类型 */}
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 400px', gap: 24, marginBottom: 24 }}>
        {/* 图谱可视化占位 */}
        <div style={{ 
          background: 'var(--color-bg-dark)', 
          border: '1px solid var(--color-border)', 
          borderRadius: 12, 
          padding: 24,
          minHeight: 300,
          display: 'flex',
          flexDirection: 'column'
        }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 16 }}>
            <Network size={20} color="#00BCD4" />
            <h3 style={{ fontSize: 16, fontWeight: 600, color: 'var(--color-text-primary)', margin: 0 }}>
              知识图谱可视化
            </h3>
          </div>
          <div style={{ 
            flex: 1, 
            display: 'flex', 
            alignItems: 'center', 
            justifyContent: 'center',
            color: 'var(--color-text-dim)',
            fontSize: 14
          }}>
            图谱可视化组件开发中...
          </div>
        </div>

        {/* 热门故障类型 */}
        <div style={{ 
          background: 'var(--color-bg-dark)', 
          border: '1px solid var(--color-border)', 
          borderRadius: 12, 
          padding: 24 
        }}>
          <h3 style={{ fontSize: 16, fontWeight: 600, color: 'var(--color-text-primary)', margin: '0 0 16px 0' }}>
            热门故障类型
          </h3>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
            {faultTypeStats.map((fault, idx) => (
              <div key={idx}>
                <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 6 }}>
                  <span style={{ fontSize: 13, color: 'var(--color-text-secondary)' }}>{fault.name}</span>
                  <span style={{ fontSize: 13, color: 'var(--color-text-muted)' }}>{fault.count}次</span>
                </div>
                <Progress
                  percent={(fault.count / maxCount) * 100}
                  showInfo={false}
                  strokeColor="#3b82f6"
                  trailColor="var(--color-border)"
                  size="small"
                />
              </div>
            ))}
          </div>
        </div>
      </div>

      {/* 故障案例库 */}
      <div>
        <h2 style={{ fontSize: 16, fontWeight: 600, color: 'var(--color-text-primary)', margin: '0 0 16px 0' }}>
          故障案例库
        </h2>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, 1fr)', gap: 16 }}>
          {mockCases.map((case_) => (
            <CaseCard key={case_.id} case_={case_} onClick={() => console.log('View case:', case_.id)} />
          ))}
        </div>
      </div>
    </div>
  )
}
