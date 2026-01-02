interface Algorithm {
  id: string
  name: string
  status: 'running' | 'stopped' | 'error'
  accuracy: number
  description?: string
}

interface AlgorithmListProps {
  algorithms: Algorithm[]
  title?: string
}

const statusColors = {
  running: '#10b981',
  stopped: 'var(--color-text-dim)',
  error: '#ef4444'
}

const statusLabels = {
  running: '运行中',
  stopped: '已停止',
  error: '错误'
}

export default function AlgorithmList({ 
  algorithms, 
  title = '检测算法状态' 
}: AlgorithmListProps) {
  return (
    <div style={{
      background: 'var(--color-bg-dark)',
      border: '1px solid var(--color-border)',
      borderRadius: 12,
      padding: 24
    }}>
      <h3 style={{
        fontSize: 16,
        fontWeight: 600,
        color: 'var(--color-text-primary)',
        margin: '0 0 16px 0'
      }}>
        {title}
      </h3>

      <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
        {algorithms.map((algo) => (
          <div
            key={algo.id}
            style={{
              background: 'var(--color-bg-card)',
              border: '1px solid var(--color-border)',
              borderRadius: 8,
              padding: 16,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between'
            }}
          >
            <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
              {/* 状态指示灯 */}
              <div style={{
                width: 10,
                height: 10,
                borderRadius: '50%',
                background: statusColors[algo.status],
                boxShadow: algo.status === 'running' 
                  ? `0 0 8px ${statusColors[algo.status]}` 
                  : 'none'
              }} />
              
              <div>
                <div style={{
                  fontSize: 14,
                  fontWeight: 500,
                  color: 'var(--color-text-primary)',
                  marginBottom: 2
                }}>
                  {algo.name}
                </div>
                <div style={{
                  fontSize: 12,
                  color: 'var(--color-text-dim)'
                }}>
                  {statusLabels[algo.status]}
                </div>
              </div>
            </div>

            {/* 准确率 */}
            <div style={{ textAlign: 'right' }}>
              <div style={{
                fontSize: 18,
                fontWeight: 700,
                color: algo.accuracy >= 90 ? 'var(--color-success)' : algo.accuracy >= 80 ? 'var(--color-warning)' : 'var(--color-danger)'
              }}>
                {algo.accuracy.toFixed(1)}%
              </div>
              <div style={{
                fontSize: 11,
                color: 'var(--color-text-dim)'
              }}>
                准确率
              </div>
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}

// 模拟算法数据
export const mockAlgorithms: Algorithm[] = [
  { id: '1', name: 'Isolation Forest', status: 'running', accuracy: 94.5, description: '孤立森林异常检测' },
  { id: '2', name: 'LSTM时序检测', status: 'running', accuracy: 96.8, description: '长短期记忆网络' },
  { id: '3', name: 'AutoEncoder', status: 'running', accuracy: 93.2, description: '自编码器重构误差' },
  { id: '4', name: 'XGBoost分类器', status: 'stopped', accuracy: 91.5, description: '梯度提升分类' }
]
