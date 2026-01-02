import { ArrowRight } from 'lucide-react'

interface FaultCase {
  id: string
  title: string
  severity: 'severe' | 'moderate' | 'minor'
  date: string
  symptom: string
  cause: string
  solution: string
  tags: string[]
}

interface CaseCardProps {
  case_: FaultCase
  onClick?: () => void
}

const severityConfig = {
  severe: { color: '#ef4444', label: '严重', bg: 'rgba(239, 68, 68, 0.15)' },
  moderate: { color: '#f59e0b', label: '中等', bg: 'rgba(245, 158, 11, 0.15)' },
  minor: { color: '#10b981', label: '轻微', bg: 'rgba(16, 185, 129, 0.15)' }
}

export default function CaseCard({ case_, onClick }: CaseCardProps) {
  const severity = severityConfig[case_.severity]

  return (
    <div 
      style={{
        background: 'var(--color-bg-dark)',
        border: '1px solid var(--color-border)',
        borderRadius: 12,
        padding: 20,
        cursor: onClick ? 'pointer' : 'default',
        transition: 'all 0.2s ease'
      }}
      onClick={onClick}
      onMouseEnter={(e) => {
        e.currentTarget.style.borderColor = 'var(--color-border-light)'
      }}
      onMouseLeave={(e) => {
        e.currentTarget.style.borderColor = 'var(--color-border)'
      }}
    >
      {/* 标题和严重程度 */}
      <div style={{
        display: 'flex',
        alignItems: 'flex-start',
        justifyContent: 'space-between',
        marginBottom: 8
      }}>
        <h4 style={{
          fontSize: 15,
          fontWeight: 600,
          color: 'var(--color-text-primary)',
          margin: 0,
          flex: 1
        }}>
          {case_.title}
        </h4>
        <span style={{
          padding: '4px 12px',
          background: severity.bg,
          color: severity.color,
          fontSize: 12,
          fontWeight: 500,
          borderRadius: 20,
          marginLeft: 12,
          flexShrink: 0
        }}>
          {severity.label}
        </span>
      </div>

      {/* 日期 */}
      <div style={{
        fontSize: 13,
        color: 'var(--color-text-dim)',
        marginBottom: 16
      }}>
        {case_.date}
      </div>

      {/* 故障现象 */}
      <div style={{ marginBottom: 12 }}>
        <div style={{
          fontSize: 12,
          color: 'var(--color-text-dim)',
          marginBottom: 4
        }}>
          故障现象
        </div>
        <p style={{
          fontSize: 13,
          color: 'var(--color-text-secondary)',
          margin: 0,
          lineHeight: 1.6
        }}>
          {case_.symptom}
        </p>
      </div>

      {/* 根本原因 */}
      <div style={{ marginBottom: 12 }}>
        <div style={{
          fontSize: 12,
          color: 'var(--color-text-dim)',
          marginBottom: 4
        }}>
          根本原因
        </div>
        <p style={{
          fontSize: 13,
          color: 'var(--color-text-secondary)',
          margin: 0,
          lineHeight: 1.6
        }}>
          {case_.cause}
        </p>
      </div>

      {/* 解决方案 */}
      <div style={{ marginBottom: 16 }}>
        <div style={{
          fontSize: 12,
          color: 'var(--color-text-dim)',
          marginBottom: 4
        }}>
          解决方案
        </div>
        <p style={{
          fontSize: 13,
          color: 'var(--color-text-secondary)',
          margin: 0,
          lineHeight: 1.6
        }}>
          {case_.solution}
        </p>
      </div>

      {/* 底部：标签和查看详情 */}
      <div style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between'
      }}>
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8 }}>
          {case_.tags.map((tag, idx) => (
            <span
              key={idx}
              style={{
                padding: '4px 10px',
                background: 'var(--color-bg-card)',
                border: '1px solid var(--color-border)',
                borderRadius: 4,
                fontSize: 12,
                color: 'var(--color-text-muted)'
              }}
            >
              {tag}
            </span>
          ))}
        </div>

        {onClick && (
          <button
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: 4,
              background: 'none',
              border: 'none',
              color: 'var(--color-info)',
              fontSize: 13,
              cursor: 'pointer',
              padding: 0
            }}
          >
            查看详情 <ArrowRight size={14} />
          </button>
        )}
      </div>
    </div>
  )
}

// 模拟故障案例数据
export const mockCases: FaultCase[] = [
  {
    id: '1',
    title: '电机轴承异常振动故障',
    severity: 'severe',
    date: '2024-01-10',
    symptom: '电机运行时振动值突然从2.1mm/s上升至7.2mm/s,伴随异常噪音',
    cause: '轴承内圈磨损严重,滚珠表面出现剥落,润滑脂失效',
    solution: '停机更换轴承6308,清洗轴承室,添加新润滑脂,重新校准转子平衡',
    tags: ['轴承', '振动', '电机']
  },
  {
    id: '2',
    title: '泵体温度异常升高',
    severity: 'moderate',
    date: '2024-01-08',
    symptom: '泵体温度从正常65°C逐渐升高至85°C,效率下降15%',
    cause: '机械密封磨损导致泄漏,冷却水路堵塞,叶轮磨损',
    solution: '更换机械密封,清洗冷却系统,检查叶轮间隙并调整',
    tags: ['温度', '密封', '泵']
  },
  {
    id: '3',
    title: '压缩机电流波动异常',
    severity: 'moderate',
    date: '2024-01-05',
    symptom: '运行电流波动范围从±2A扩大至±8A,功率因数降低',
    cause: '进气阀磨损导致气量不稳,活塞环磨损,排气阀积碳',
    solution: '更换进排气阀组,更换活塞环,清理气缸积碳',
    tags: ['电流', '压缩机', '气阀']
  },
  {
    id: '4',
    title: '减速机油温过高',
    severity: 'minor',
    date: '2024-01-03',
    symptom: '减速机油温从正常55°C上升至72°C,油位正常',
    cause: '齿轮磨损产生金属屑,润滑油老化变质,散热风扇故障',
    solution: '更换润滑油,清洗油箱,修复散热风扇,监测齿轮状态',
    tags: ['温度', '润滑', '减速机']
  }
]
