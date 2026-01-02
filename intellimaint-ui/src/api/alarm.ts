import apiClient from './client'
import type { ApiResponse } from '../types/telemetry'
import type { 
  Alarm, 
  PagedAlarmResult, 
  AlarmQuery, 
  CreateAlarmRequest, 
  AckAlarmRequest,
  AlarmStats 
} from '../types/alarm'

// 查询告警 - 使用正确的整数状态参数 (0=Open, 1=Acknowledged, 2=Closed)
export async function queryAlarms(params?: AlarmQuery): Promise<ApiResponse<PagedAlarmResult>> {
  const query = new URLSearchParams()
  if (params?.deviceId) query.append('deviceId', params.deviceId)
  // status 必须是整数 (0=Open, 1=Acknowledged, 2=Closed)，不能是字符串 "Active"
  if (params?.status !== undefined) query.append('status', params.status.toString())
  if (params?.minSeverity !== undefined) query.append('minSeverity', params.minSeverity.toString())
  if (params?.startTs) query.append('startTs', params.startTs.toString())
  if (params?.endTs) query.append('endTs', params.endTs.toString())
  if (params?.limit) query.append('limit', params.limit.toString())
  if (params?.after) query.append('after', params.after)
  
  const queryString = query.toString()
  return apiClient.get(`/alarms${queryString ? `?${queryString}` : ''}`)
}

// 兼容别名
export const getAlarms = queryAlarms

// 获取单个告警
export async function getAlarm(alarmId: string): Promise<ApiResponse<Alarm>> {
  return apiClient.get(`/alarms/${encodeURIComponent(alarmId)}`)
}

// 创建告警 - 需要 code 和 message 字段
export async function createAlarm(request: CreateAlarmRequest): Promise<ApiResponse<Alarm>> {
  return apiClient.post('/alarms', request)
}

// 确认告警 - 后端路径是 /{alarmId}/ack (不是 /acknowledge)
export async function ackAlarm(alarmId: string, request: AckAlarmRequest): Promise<ApiResponse<Alarm>> {
  return apiClient.post(`/alarms/${encodeURIComponent(alarmId)}/ack`, request)
}

// 兼容别名
export const acknowledgeAlarm = ackAlarm

// 关闭告警 - 后端路径是 /{alarmId}/close (不是 /resolve)
export async function closeAlarm(alarmId: string): Promise<ApiResponse<Alarm>> {
  return apiClient.post(`/alarms/${encodeURIComponent(alarmId)}/close`, {})
}

// 兼容别名
export const resolveAlarm = closeAlarm

// 告警统计 - 后端返回 { openCount: number }
export async function getAlarmStats(deviceId?: string): Promise<ApiResponse<AlarmStats>> {
  const query = deviceId ? `?deviceId=${encodeURIComponent(deviceId)}` : ''
  return apiClient.get(`/alarms/stats${query}`)
}
