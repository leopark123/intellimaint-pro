export interface Alarm {
  alarmId: string
  deviceId: string
  tagId?: string | null
  ts: number
  severity: number
  code: string
  message: string
  status: number // 0=Open, 1=Acknowledged, 2=Closed
  createdUtc: number
  updatedUtc: number
  ackedBy?: string | null
  ackedUtc?: number | null
  ackNote?: string | null
}

export interface PagedAlarmResult {
  items: Alarm[]
  nextToken?: string | null
  hasMore: boolean
  totalCount: number
}

export interface AlarmQuery {
  deviceId?: string
  status?: number
  minSeverity?: number
  startTs?: number
  endTs?: number
  limit?: number
  after?: string
}

export interface CreateAlarmRequest {
  deviceId: string
  tagId?: string
  severity: number
  code: string
  message: string
}

export interface AckAlarmRequest {
  ackedBy: string
  ackNote?: string
}

export interface AlarmStats {
  openCount: number
}

export const SeverityOptions = [
  { label: 'Info', value: 1, color: 'blue' },
  { label: 'Warning', value: 2, color: 'gold' },
  { label: 'Alarm', value: 3, color: 'orange' },
  { label: 'Critical', value: 4, color: 'red' }
]

export const StatusOptions = [
  { label: '未处理', value: 0, color: 'error' },
  { label: '已确认', value: 1, color: 'warning' },
  { label: '已关闭', value: 2, color: 'default' }
]
