import apiClient from './client'

export interface HealthScore {
  deviceId: string
  timestamp: number
  index: number
  level: string
  levelCode: number
  deviationScore: number
  trendScore: number
  stabilityScore: number
  alarmScore: number
  hasBaseline: boolean
  problemTags: string[]
  diagnosticMessage: string | null
}

export interface DeviceBaseline {
  deviceId: string
  createdUtc: number
  updatedUtc: number
  sampleCount: number
  learningHours: number
  tagCount: number
  tags: TagBaseline[]
}

export interface TagBaseline {
  tagId: string
  normalMean: number
  normalStdDev: number
  normalMin: number
  normalMax: number
  normalCV: number
}

// v60: 健康快照（用于历史趋势）
export interface HealthSnapshot {
  deviceId: string
  timestamp: number
  index: number
  level: string
  deviationScore: number
  trendScore: number
  stabilityScore: number
  alarmScore: number
}

// v60: 健康历史数据
export interface HealthHistory {
  deviceId: string
  startTs: number
  endTs: number
  count: number
  snapshots: HealthSnapshot[]
}

// v60: 健康汇总统计
export interface HealthSummary {
  assessedDevices: number
  avgHealthIndex: number
  distribution: {
    healthy: number
    attention: number
    warning: number
    critical: number
  }
  devices: {
    deviceId: string
    index: number
    level: string
    timestamp: number
  }[]
}

interface ApiResponse<T> {
  success: boolean
  data: T
  error?: string
  message?: string
}

/**
 * 获取单个设备的健康评分
 */
export async function getDeviceHealth(
  deviceId: string,
  windowMinutes?: number
): Promise<ApiResponse<HealthScore>> {
  const params = windowMinutes ? `?windowMinutes=${windowMinutes}` : ''
  return apiClient.get(`/health-assessment/devices/${deviceId}${params}`)
}

/**
 * 获取所有设备的健康评分
 */
export async function getAllDevicesHealth(
  windowMinutes?: number
): Promise<ApiResponse<HealthScore[]>> {
  const params = windowMinutes ? `?windowMinutes=${windowMinutes}` : ''
  return apiClient.get(`/health-assessment/devices${params}`)
}

/**
 * 获取设备基线
 */
export async function getDeviceBaseline(
  deviceId: string
): Promise<ApiResponse<DeviceBaseline>> {
  return apiClient.get(`/health-assessment/baselines/${deviceId}`)
}

/**
 * 学习设备基线
 */
export async function learnBaseline(
  deviceId: string,
  learningHours: number = 24
): Promise<ApiResponse<DeviceBaseline>> {
  return apiClient.post(`/health-assessment/baselines/${deviceId}/learn`, {
    learningHours
  })
}

/**
 * 删除设备基线
 */
export async function deleteBaseline(
  deviceId: string
): Promise<ApiResponse<void>> {
  return apiClient.delete(`/health-assessment/baselines/${deviceId}`)
}

/**
 * 获取所有基线列表
 */
export async function listBaselines(): Promise<ApiResponse<DeviceBaseline[]>> {
  return apiClient.get('/health-assessment/baselines')
}

/**
 * v60: 获取设备健康历史趋势
 */
export async function getDeviceHealthHistory(
  deviceId: string,
  startTs?: number,
  endTs?: number
): Promise<ApiResponse<HealthHistory>> {
  const params = new URLSearchParams()
  if (startTs) params.append('startTs', startTs.toString())
  if (endTs) params.append('endTs', endTs.toString())
  const query = params.toString() ? `?${params.toString()}` : ''
  return apiClient.get(`/health-assessment/devices/${deviceId}/history${query}`)
}

/**
 * v60: 获取健康汇总统计
 */
export async function getHealthSummary(): Promise<ApiResponse<HealthSummary>> {
  return apiClient.get('/health-assessment/summary')
}
