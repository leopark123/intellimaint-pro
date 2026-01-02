import apiClient from './client'
import type { ApiResponse } from '../types/telemetry'
import type { HealthSnapshot, SystemStats } from '../types/health'

export async function getCurrentHealth(): Promise<ApiResponse<HealthSnapshot>> {
  return apiClient.get('/health')
}

export async function getHealthHistory(count = 60): Promise<ApiResponse<HealthSnapshot[]>> {
  return apiClient.get('/health/history', { params: { count } })
}

export async function getSystemStats(): Promise<ApiResponse<SystemStats>> {
  return apiClient.get('/health/stats')
}
