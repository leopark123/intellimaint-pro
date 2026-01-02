import { Check, Loader2 } from 'lucide-react'
import { Progress } from 'antd'

interface PipelineStep {
  id: string
  name: string
  status: 'completed' | 'running' | 'pending'
  progress?: number
}

interface PipelineStepsProps {
  steps: PipelineStep[]
  title?: string
}

export default function PipelineSteps({ 
  steps, 
  title = '自动化训练流水线' 
}: PipelineStepsProps) {
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
        margin: '0 0 20px 0'
      }}>
        {title}
      </h3>

      <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
        {steps.map((step, idx) => {
          const isCompleted = step.status === 'completed'
          const isRunning = step.status === 'running'
          const isPending = step.status === 'pending'

          return (
            <div
              key={step.id}
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: 16
              }}
            >
              {/* 步骤编号/图标 */}
              <div style={{
                width: 32,
                height: 32,
                borderRadius: '50%',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                flexShrink: 0,
                background: isCompleted 
                  ? 'var(--color-success)' 
                  : isRunning 
                  ? 'var(--color-info)' 
                  : 'var(--color-border)',
                color: isCompleted || isRunning ? 'var(--color-text-primary)' : 'var(--color-text-dim)',
                fontSize: 13,
                fontWeight: 500
              }}>
                {isCompleted ? (
                  <Check size={16} />
                ) : isRunning ? (
                  <Loader2 size={16} className="animate-spin" style={{ animation: 'spin 1s linear infinite' }} />
                ) : (
                  idx + 1
                )}
              </div>

              {/* 步骤信息 */}
              <div style={{ flex: 1 }}>
                <div style={{
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  marginBottom: 4
                }}>
                  <span style={{
                    fontSize: 14,
                    color: isPending ? 'var(--color-text-dim)' : 'var(--color-text-primary)',
                    fontWeight: isPending ? 400 : 500
                  }}>
                    {step.name}
                  </span>
                  <span style={{
                    fontSize: 13,
                    color: isCompleted ? 'var(--color-success)' : isRunning ? 'var(--color-info)' : 'var(--color-text-dim)'
                  }}>
                    {step.progress !== undefined ? `${step.progress}%` : ''}
                  </span>
                </div>

                {/* 进度条 */}
                <Progress
                  percent={step.progress ?? (isCompleted ? 100 : 0)}
                  showInfo={false}
                  strokeColor={isCompleted ? 'var(--color-success)' : isRunning ? 'var(--color-info)' : 'var(--color-border)'}
                  trailColor="var(--color-bg-card)"
                  size="small"
                />
              </div>
            </div>
          )
        })}
      </div>
    </div>
  )
}

// 模拟流水线数据
export const mockPipelineSteps: PipelineStep[] = [
  { id: '1', name: '数据采集', status: 'completed', progress: 100 },
  { id: '2', name: '特征工程', status: 'completed', progress: 100 },
  { id: '3', name: '模型训练', status: 'running', progress: 67 },
  { id: '4', name: '模型评估', status: 'pending', progress: 0 },
  { id: '5', name: '模型部署', status: 'pending', progress: 0 }
]
