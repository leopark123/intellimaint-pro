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
