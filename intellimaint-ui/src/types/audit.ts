export interface AuditLogEntry {
  id: number
  ts: number
  userId: string
  userName: string
  action: string
  resourceType: string
  resourceId?: string | null
  details?: string | null
  ipAddress?: string | null
}

export interface AuditLogQuery {
  action?: string
  resourceType?: string
  resourceId?: string
  userId?: string
  startTs?: number
  endTs?: number
  limit?: number
  offset?: number
}

export interface PagedAuditLogResult {
  items: AuditLogEntry[]
  totalCount: number
  limit: number
  offset: number
}

export const ActionLabels: Record<string, string> = {
  'device.create': '创建设备',
  'device.update': '更新设备',
  'device.delete': '删除设备',
  'tag.create': '创建标签',
  'tag.update': '更新标签',
  'tag.delete': '删除标签',
  'alarm.create': '创建告警',
  'alarm.ack': '确认告警',
  'alarm.close': '关闭告警',
  'setting.update': '更新设置',
  'data.cleanup': '数据清理'
}

export const ResourceTypeLabels: Record<string, string> = {
  device: '设备',
  tag: '标签',
  alarm: '告警',
  setting: '设置',
  system: '系统'
}
