// v50: 通用组件库索引

// 指标卡片
export { default as MetricCard } from './MetricCard'

// 告警面板
export { default as AlertPanel } from './AlertPanel'

// 设备状态
export { default as EquipmentStatus } from './EquipmentStatus'

// 图表容器
export { default as ChartCard } from './ChartCard'

// 状态标签
export { 
  default as StatusBadge,
  HealthyBadge,
  WarningBadge,
  CriticalBadge,
  RunningBadge,
  StoppedBadge,
  HighPriorityBadge,
  MediumPriorityBadge,
  LowPriorityBadge,
  SevereBadge,
  ModerateBadge,
  MinorBadge
} from './StatusBadge'

// 趋势标签
export { default as TrendBadge, SimpleTrend } from './TrendBadge'

// 热力图
export { default as HeatmapGrid, generateMockHeatmapData } from './HeatmapGrid'

// 算法列表
export { default as AlgorithmList, mockAlgorithms } from './AlgorithmList'

// 事件列表
export { default as EventList, mockEvents } from './EventList'

// RUL 预测卡片
export { default as RULCard, mockRULData } from './RULCard'

// 工单卡片
export { default as WorkOrderCard, mockWorkOrders } from './WorkOrderCard'

// 故障案例卡片
export { default as CaseCard, mockCases } from './CaseCard'

// 流水线步骤
export { default as PipelineSteps, mockPipelineSteps } from './PipelineSteps'

// 策略建议卡片
export { default as StrategyCard, mockStrategies } from './StrategyCard'

// A/B 测试实验卡片
export { default as ExperimentCard, mockExperiments } from './ExperimentCard'
