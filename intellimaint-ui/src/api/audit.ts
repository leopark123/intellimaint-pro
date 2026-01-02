import apiClient from './client'
import type { ApiResponse } from '../types/telemetry'
import type { AuditLogQuery, PagedAuditLogResult } from '../types/audit'

export async function queryAuditLogs(query: AuditLogQuery): Promise<ApiResponse<PagedAuditLogResult>> {
  const params = new URLSearchParams()
  if (query.action) params.append('action', query.action)
  if (query.resourceType) params.append('resourceType', query.resourceType)
  if (query.resourceId) params.append('resourceId', query.resourceId)
  if (query.userId) params.append('userId', query.userId)
  if (typeof query.startTs === 'number') params.append('startTs', query.startTs.toString())
  if (typeof query.endTs === 'number') params.append('endTs', query.endTs.toString())
  if (typeof query.limit === 'number') params.append('limit', query.limit.toString())
  if (typeof query.offset === 'number') params.append('offset', query.offset.toString())

  const qs = params.toString()
  return apiClient.get(qs ? `/audit-logs?${qs}` : '/audit-logs')
}

export async function getAuditActions(): Promise<ApiResponse<string[]>> {
  return apiClient.get('/audit-logs/actions')
}

export async function getAuditResourceTypes(): Promise<ApiResponse<string[]>> {
  return apiClient.get('/audit-logs/resource-types')
}
