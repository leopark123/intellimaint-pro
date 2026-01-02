export interface CollectorHealth {
  protocol: string
  state: number // 1=Connected, 2=Degraded, 3=Disconnected
  lastSuccessTime: string
  consecutiveErrors: number
  typeMismatchCount: number
  avgLatencyMs: number
  p95LatencyMs: number
  lastError?: string | null
  activeConnections: number
  totalTagCount: number
  healthyTagCount: number
}

export interface HealthSnapshot {
  utcTime: string
  overallState: number // 1=Healthy, 2=Degraded, 3=NotReady
  databaseState: number // 1=Healthy, 2=Slow, 3=Unavailable
  queueState: number // 1=Normal, 2=Backpressure, 3=Full
  queueDepth: number
  droppedPoints: number
  writeLatencyMsP95: number
  collectors: Record<string, CollectorHealth>
  mqttConnected: boolean
  outboxDepth: number
  memoryUsedMb: number
}

export interface SystemStats {
  totalDevices: number
  enabledDevices: number
  totalTags: number
  enabledTags: number
  totalAlarms: number
  openAlarms: number
  totalTelemetryPoints: number
  last24HoursTelemetryPoints: number
  databaseSizeBytes: number
}

export const HealthStateOptions = [
  { label: '健康', value: 1, color: 'success' },
  { label: '降级', value: 2, color: 'warning' },
  { label: '未就绪', value: 3, color: 'error' }
] as const

export const DatabaseStateOptions = [
  { label: '健康', value: 1, color: 'success' },
  { label: '缓慢', value: 2, color: 'warning' },
  { label: '不可用', value: 3, color: 'error' }
] as const

export const QueueStateOptions = [
  { label: '正常', value: 1, color: 'success' },
  { label: '背压', value: 2, color: 'warning' },
  { label: '已满', value: 3, color: 'error' }
] as const

export const CollectorStateOptions = [
  { label: '已连接', value: 1, color: 'success' },
  { label: '降级', value: 2, color: 'warning' },
  { label: '断开', value: 3, color: 'error' }
] as const
