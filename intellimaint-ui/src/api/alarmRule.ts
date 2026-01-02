import apiClient from './client'
import type { ApiResponse } from '../types/telemetry'
import type { AlarmRule, CreateAlarmRuleRequest, UpdateAlarmRuleRequest } from '../types/alarmRule'

export async function getAlarmRules(): Promise<ApiResponse<AlarmRule[]>> {
  return apiClient.get('/alarm-rules')
}

export async function getAlarmRule(ruleId: string): Promise<ApiResponse<AlarmRule>> {
  return apiClient.get(`/alarm-rules/${encodeURIComponent(ruleId)}`)
}

export async function createAlarmRule(request: CreateAlarmRuleRequest): Promise<ApiResponse<AlarmRule>> {
  return apiClient.post('/alarm-rules', request)
}

export async function updateAlarmRule(ruleId: string, request: UpdateAlarmRuleRequest): Promise<ApiResponse<AlarmRule>> {
  return apiClient.put(`/alarm-rules/${encodeURIComponent(ruleId)}`, request)
}

export async function deleteAlarmRule(ruleId: string): Promise<ApiResponse<void>> {
  return apiClient.delete(`/alarm-rules/${encodeURIComponent(ruleId)}`)
}

export async function enableAlarmRule(ruleId: string): Promise<ApiResponse<void>> {
  return apiClient.put(`/alarm-rules/${encodeURIComponent(ruleId)}/enable`)
}

export async function disableAlarmRule(ruleId: string): Promise<ApiResponse<void>> {
  return apiClient.put(`/alarm-rules/${encodeURIComponent(ruleId)}/disable`)
}
