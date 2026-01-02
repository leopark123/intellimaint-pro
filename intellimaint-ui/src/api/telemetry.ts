import apiClient from './client'
import type { ApiResponse, TelemetryPoint, TelemetryStats } from '../types/telemetry'

export interface TelemetryQueryParams {
  deviceId?: string
  tagId?: string
  startTs?: number
  endTs?: number
  limit?: number
}

export interface TagInfo {
  deviceId: string
  tagId: string
  valueType: string
  unit?: string
  lastTs?: number
  pointCount?: number
}

// 查询遥测数据 - 正确路径是 /api/telemetry/query (不是 /api/telemetry)
export async function queryTelemetry(params: TelemetryQueryParams): Promise<ApiResponse<TelemetryPoint[]>> {
  const query = new URLSearchParams()
  if (params.deviceId) query.append('deviceId', params.deviceId)
  if (params.tagId) query.append('tagId', params.tagId)
  if (params.startTs) query.append('startTs', params.startTs.toString())
  if (params.endTs) query.append('endTs', params.endTs.toString())
  if (params.limit) query.append('limit', params.limit.toString())
  
  const queryString = query.toString()
  // 注意: 后端路径是 /telemetry/query 不是 /telemetry
  return apiClient.get(`/telemetry/query${queryString ? `?${queryString}` : ''}`)
}

// 兼容别名
export const getTelemetry = queryTelemetry

// 获取最新遥测数据
export async function getLatestTelemetry(deviceId?: string, tagId?: string): Promise<ApiResponse<TelemetryPoint[]>> {
  const query = new URLSearchParams()
  if (deviceId) query.append('deviceId', deviceId)
  if (tagId) query.append('tagId', tagId)
  
  const queryString = query.toString()
  return apiClient.get(`/telemetry/latest${queryString ? `?${queryString}` : ''}`)
}

// 获取标签列表
export async function getTags(deviceId?: string): Promise<ApiResponse<TagInfo[]>> {
  const query = deviceId ? `?deviceId=${encodeURIComponent(deviceId)}` : ''
  return apiClient.get(`/telemetry/tags${query}`)
}

export interface AggregateParams {
  deviceId: string
  tagId: string
  startTs: number
  endTs: number
  intervalMs: number
  func?: 'avg' | 'min' | 'max' | 'sum' | 'count'
}

// 获取聚合遥测数据
export async function getAggregatedTelemetry(params: AggregateParams): Promise<ApiResponse<any[]>> {
  const query = new URLSearchParams({
    deviceId: params.deviceId,
    tagId: params.tagId,
    startTs: params.startTs.toString(),
    endTs: params.endTs.toString(),
    intervalMs: params.intervalMs.toString()
  })
  if (params.func) query.append('function', params.func)
  
  return apiClient.get(`/telemetry/aggregate?${query.toString()}`)
}

// 获取遥测统计 - 后端没有专门的 /telemetry/stats 端点，使用 tags 模拟
export async function getTelemetryStats(deviceId?: string): Promise<ApiResponse<TelemetryStats>> {
  try {
    const tagsRes = await getTags(deviceId)
    if (tagsRes.success && tagsRes.data) {
      let totalCount = 0
      const deviceSet = new Set<string>()
      tagsRes.data.forEach((tag) => {
        totalCount += tag.pointCount || 0
        deviceSet.add(tag.deviceId)
      })
      return {
        success: true,
        data: {
          totalCount,
          deviceCount: deviceSet.size,
          tagCount: tagsRes.data.length
        }
      }
    }
    return {
      success: false,
      error: tagsRes.error || '获取统计失败'
    }
  } catch (err) {
    return {
      success: false,
      error: '获取统计失败'
    }
  }
}
