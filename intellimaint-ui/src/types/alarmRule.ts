export interface AlarmRule {
  ruleId: string
  name: string
  description?: string | null
  tagId: string
  deviceId?: string | null
  conditionType: string // gt/gte/lt/lte/eq/ne
  threshold: number
  durationMs: number
  severity: number // 1-5
  messageTemplate?: string | null
  enabled: boolean
  createdUtc: number
  updatedUtc: number
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
}

export const ConditionTypeOptions = [
  { label: '大于 (>)', value: 'gt' },
  { label: '大于等于 (>=)', value: 'gte' },
  { label: '小于 (<)', value: 'lt' },
  { label: '小于等于 (<=)', value: 'lte' },
  { label: '等于 (=)', value: 'eq' },
  { label: '不等于 (!=)', value: 'ne' }
]

export const SeverityOptions = [
  { label: '1 - 信息', value: 1 },
  { label: '2 - 警告', value: 2 },
  { label: '3 - 一般', value: 3 },
  { label: '4 - 严重', value: 4 },
  { label: '5 - 紧急', value: 5 }
]
