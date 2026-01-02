import { Progress } from 'antd'

interface Experiment {
  id: string
  name: string
  status: 'running' | 'completed'
  variantA: { name: string; score: number }
  variantB: { name: string; score: number }
  winner?: 'A' | 'B'
}

interface ExperimentCardProps {
  experiment: Experiment
}

export default function ExperimentCard({ experiment }: ExperimentCardProps) {
  const isCompleted = experiment.status === 'completed'
  const winnerIsB = experiment.winner === 'B'
  const winnerIsA = experiment.winner === 'A'

  return (
    <div style={{
      background: 'var(--color-bg-dark)',
      border: '1px solid var(--color-border)',
      borderRadius: 12,
      padding: 20
    }}>
      {/* 标题和状态 */}
      <div style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        marginBottom: 16
      }}>
        <h4 style={{
          fontSize: 15,
          fontWeight: 600,
          color: 'var(--color-text-primary)',
          margin: 0
        }}>
          {experiment.name}
        </h4>

        <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
          {/* 状态标签 */}
          <span style={{
            padding: '4px 12px',
            background: isCompleted 
              ? 'rgba(16, 185, 129, 0.15)' 
              : 'rgba(59, 130, 246, 0.15)',
            color: isCompleted ? 'var(--color-success)' : 'var(--color-info)',
            fontSize: 12,
            fontWeight: 500,
            borderRadius: 20
          }}>
            {isCompleted ? '已完成' : '运行中'}
          </span>

          {/* 获胜标签 */}
          {experiment.winner && (
            <span style={{
              padding: '4px 12px',
              background: 'rgba(16, 185, 129, 0.15)',
              color: 'var(--color-success)',
              fontSize: 12,
              fontWeight: 500,
              borderRadius: 20
            }}>
              变体{experiment.winner}获胜
            </span>
          )}
        </div>
      </div>

      {/* 变体对比 */}
      <div style={{
        display: 'grid',
        gridTemplateColumns: '1fr 1fr',
        gap: 24
      }}>
        {/* 变体 A */}
        <div>
          <div style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            marginBottom: 8
          }}>
            <span style={{ 
              fontSize: 13, 
              color: winnerIsA ? 'var(--color-success)' : 'var(--color-text-muted)',
              fontWeight: winnerIsA ? 600 : 400
            }}>
              {experiment.variantA.name}
            </span>
            <span style={{ 
              fontSize: 14, 
              fontWeight: 600, 
              color: winnerIsA ? 'var(--color-success)' : 'var(--color-text-primary)' 
            }}>
              {experiment.variantA.score.toFixed(1)}%
            </span>
          </div>
          <Progress
            percent={experiment.variantA.score}
            showInfo={false}
            strokeColor={winnerIsA ? 'var(--color-success)' : 'var(--color-info)'}
            trailColor="var(--color-border)"
            size="small"
          />
        </div>

        {/* 变体 B */}
        <div>
          <div style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            marginBottom: 8
          }}>
            <span style={{ 
              fontSize: 13, 
              color: winnerIsB ? 'var(--color-success)' : 'var(--color-text-muted)',
              fontWeight: winnerIsB ? 600 : 400
            }}>
              {experiment.variantB.name}
            </span>
            <span style={{ 
              fontSize: 14, 
              fontWeight: 600, 
              color: winnerIsB ? 'var(--color-success)' : 'var(--color-text-primary)' 
            }}>
              {experiment.variantB.score.toFixed(1)}%
            </span>
          </div>
          <Progress
            percent={experiment.variantB.score}
            showInfo={false}
            strokeColor={winnerIsB ? 'var(--color-success)' : 'var(--color-info)'}
            trailColor="var(--color-border)"
            size="small"
          />
        </div>
      </div>
    </div>
  )
}

// 模拟实验数据
export const mockExperiments: Experiment[] = [
  {
    id: '1',
    name: '实验A: LSTM vs Transformer',
    status: 'running',
    variantA: { name: '变体A', score: 94.2 },
    variantB: { name: '变体B', score: 96.8 },
    winner: 'B'
  },
  {
    id: '2',
    name: '实验B: 特征工程优化',
    status: 'completed',
    variantA: { name: '变体A', score: 92.5 },
    variantB: { name: '变体B', score: 94.2 },
    winner: 'B'
  },
  {
    id: '3',
    name: '实验C: 超参数调优',
    status: 'running',
    variantA: { name: '变体A', score: 95.1 },
    variantB: { name: '变体B', score: 95.8 }
  }
]
