export interface AlarmRule {
  ruleId: string
  name: string
  description?: string | null
  tagId: string
  deviceId?: string | null
  conditionType: string // gt/gte/lt/lte/eq/ne/offline/roc_percent/roc_absolute
  threshold: number
  durationMs: number
  severity: number // 1-5
  messageTemplate?: string | null
  enabled: boolean
  createdUtc: number
  updatedUtc: number
  // v56 新增
  rocWindowMs?: number // 变化率时间窗口（毫秒）
  ruleType?: string // threshold | offline | roc
}

export interface CreateAlarmRuleRequest {
  ruleId: string
  name: string
  description?: string
  tagId: string
  deviceId?: string
  conditionType: string
  threshold: number
  durationMs?: number
  severity?: number
  messageTemplate?: string
  enabled?: boolean
  // v56 新增
  rocWindowMs?: number
}

export interface UpdateAlarmRuleRequest {
  name?: string
  description?: string
  tagId?: string
  deviceId?: string
  conditionType?: string
  threshold?: number
  durationMs?: number
  severity?: number
  messageTemplate?: string
  enabled?: boolean
  // v56 新增
  rocWindowMs?: number
}

// 阈值规则条件
export const ThresholdConditionOptions = [
  { label: '大于 (>)', value: 'gt' },
  { label: '大于等于 (>=)', value: 'gte' },
  { label: '小于 (<)', value: 'lt' },
  { label: '小于等于 (<=)', value: 'lte' },
  { label: '等于 (=)', value: 'eq' },
  { label: '不等于 (!=)', value: 'ne' }
]

// v56: 离线检测条件
export const OfflineConditionOptions = [
  { label: '离线检测', value: 'offline' }
]

// v56: 变化率条件
export const RocConditionOptions = [
  { label: '变化率 - 百分比', value: 'roc_percent' },
  { label: '变化率 - 绝对值', value: 'roc_absolute' }
]

// v58: 波动告警条件
export const VolatilityConditionOptions = [
  { label: '波动告警 - 标准差', value: 'volatility' }
]

// 所有条件类型（合并）
export const ConditionTypeOptions = [
  { label: '── 阈值规则 ──', value: '_divider_threshold', disabled: true },
  ...ThresholdConditionOptions,
  { label: '── 离线检测 ──', value: '_divider_offline', disabled: true },
  ...OfflineConditionOptions,
  { label: '── 变化率 ──', value: '_divider_roc', disabled: true },
  ...RocConditionOptions,
  { label: '── 波动告警 ──', value: '_divider_volatility', disabled: true },
  ...VolatilityConditionOptions
]

// 规则类型选项
export const RuleTypeOptions = [
  { label: '阈值规则', value: 'threshold' },
  { label: '离线检测', value: 'offline' },
  { label: '变化率', value: 'roc' },
  { label: '波动告警', value: 'volatility' }
]

// 根据条件类型获取规则类型
export function getRuleTypeFromCondition(conditionType: string): string {
  if (conditionType === 'offline') return 'offline'
  if (conditionType.startsWith('roc_')) return 'roc'
  if (conditionType === 'volatility') return 'volatility'
  return 'threshold'
}

// 根据条件类型获取阈值标签
export function getThresholdLabel(conditionType: string): string {
  if (conditionType === 'offline') return '超时时间（秒）'
  if (conditionType === 'roc_percent') return '变化阈值（%）'
  if (conditionType === 'roc_absolute') return '变化阈值（绝对值）'
  if (conditionType === 'volatility') return '标准差阈值'
  return '阈值'
}

// 判断是否需要显示时间窗口（变化率和波动告警都需要）
export function needsRocWindow(conditionType: string): boolean {
  return conditionType.startsWith('roc_') || conditionType === 'volatility'
}

export const SeverityOptions = [
  { label: '1 - 信息', value: 1 },
  { label: '2 - 警告', value: 2 },
  { label: '3 - 一般', value: 3 },
  { label: '4 - 严重', value: 4 },
  { label: '5 - 紧急', value: 5 }
]
