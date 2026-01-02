import { Calendar, User, Wrench, Clock } from 'lucide-react'

interface WorkOrder {
  id: string
  title: string
  device: string
  description: string
  priority: 'high' | 'medium' | 'low'
  planDate: string
  assignee: string
  maintenanceType: string
  status: 'pending' | 'in_progress' | 'completed'
  parts?: string[]
}

interface WorkOrderCardProps {
  order: WorkOrder
}

const priorityConfig = {
  high: { color: '#ef4444', label: '高优先级', bg: 'rgba(239, 68, 68, 0.15)' },
  medium: { color: '#f59e0b', label: '中优先级', bg: 'rgba(245, 158, 11, 0.15)' },
  low: { color: '#3b82f6', label: '低优先级', bg: 'rgba(59, 130, 246, 0.15)' }
}

const statusLabels = {
  pending: '待执行',
  in_progress: '计划中',
  completed: '已完成'
}

export default function WorkOrderCard({ order }: WorkOrderCardProps) {
  const priority = priorityConfig[order.priority]

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
    }}
    onMouseLeave={(e) => {
      e.currentTarget.style.borderColor = 'var(--color-border)'
    }}
    >
      {/* 标题和优先级 */}
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
          {order.title}
        </h4>
        <span style={{
          padding: '4px 12px',
          background: priority.bg,
          color: priority.color,
          fontSize: 12,
          fontWeight: 500,
          borderRadius: 20,
          marginLeft: 12,
          flexShrink: 0
        }}>
          {priority.label}
        </span>
      </div>

      {/* 设备 */}
      <div style={{
        fontSize: 13,
        color: 'var(--color-text-muted)',
        marginBottom: 12
      }}>
        {order.device}
      </div>

      {/* 描述 */}
      <p style={{
        fontSize: 13,
        color: 'var(--color-text-secondary)',
        margin: '0 0 16px 0',
        lineHeight: 1.6
      }}>
        {order.description}
      </p>

      {/* 信息网格 */}
      <div style={{
        display: 'grid',
        gridTemplateColumns: '1fr 1fr',
        gap: '12px 24px',
        marginBottom: 16
      }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <Calendar size={14} color="#6b7280" />
          <div>
            <div style={{ fontSize: 11, color: 'var(--color-text-dim)' }}>计划日期</div>
            <div style={{ fontSize: 13, color: 'var(--color-text-primary)' }}>{order.planDate}</div>
          </div>
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <User size={14} color="#6b7280" />
          <div>
            <div style={{ fontSize: 11, color: 'var(--color-text-dim)' }}>负责人</div>
            <div style={{ fontSize: 13, color: 'var(--color-text-primary)' }}>{order.assignee}</div>
          </div>
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <Wrench size={14} color="#6b7280" />
          <div>
            <div style={{ fontSize: 11, color: 'var(--color-text-dim)' }}>维护类型</div>
            <div style={{ fontSize: 13, color: 'var(--color-text-primary)' }}>{order.maintenanceType}</div>
          </div>
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <Clock size={14} color="#6b7280" />
          <div>
            <div style={{ fontSize: 11, color: 'var(--color-text-dim)' }}>状态</div>
            <div style={{ fontSize: 13, color: 'var(--color-text-primary)' }}>{statusLabels[order.status]}</div>
          </div>
        </div>
      </div>

      {/* 所需备件 */}
      {order.parts && order.parts.length > 0 && (
        <div>
          <div style={{ fontSize: 12, color: 'var(--color-text-dim)', marginBottom: 8 }}>所需备件</div>
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8 }}>
            {order.parts.map((part, idx) => (
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
                {part}
              </span>
            ))}
          </div>
        </div>
      )}
    </div>
  )
}

// 模拟工单数据
export const mockWorkOrders: WorkOrder[] = [
  {
    id: '1',
    title: '风机F-401紧急维修',
    device: '风机F-401',
    description: '振动异常超标,需立即更换轴承并检查转子平衡',
    priority: 'high',
    planDate: '2024-01-15',
    assignee: '张工',
    maintenanceType: '紧急维修',
    status: 'pending'
  },
  {
    id: '2',
    title: '泵P-102预防性维护',
    device: '泵P-102',
    description: '温度趋势异常,建议更换机械密封并清洗冷却系统',
    priority: 'medium',
    planDate: '2024-01-20',
    assignee: '李工',
    maintenanceType: '预防性维护',
    status: 'in_progress'
  },
  {
    id: '3',
    title: '电机M-301定期保养',
    device: '电机M-301',
    description: '运行时间达到保养周期,进行常规检查和润滑',
    priority: 'low',
    planDate: '2024-01-25',
    assignee: '王工',
    maintenanceType: '定期保养',
    status: 'in_progress',
    parts: ['润滑油', '清洁剂']
  },
  {
    id: '4',
    title: '压缩机C-201密封更换',
    device: '压缩机C-201',
    description: 'RUL预测显示30天内需更换密封件,避免泄漏',
    priority: 'medium',
    planDate: '2024-02-01',
    assignee: '赵工',
    maintenanceType: '预防性维护',
    status: 'pending',
    parts: ['活塞环', '气缸密封', '阀片']
  }
]
