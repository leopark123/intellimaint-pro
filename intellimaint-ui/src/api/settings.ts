import apiClient from './client'
import type { ApiResponse } from '../types/telemetry'

export interface SystemInfo {
  version: string
  edgeId: string
  databasePath: string
  databaseSizeBytes: number
  uptimeSeconds: number
  startTime: string
  totalTelemetryPoints: number
  totalAlarms: number
  totalDevices: number
  totalTags: number
}

export interface SystemSetting {
  key: string
  value: string
  updatedUtc: number
}

export interface CleanupResult {
  deletedTelemetryPoints: number
  deletedAlarms: number
  deletedHealthSnapshots: number
  freedBytes: number
}

export async function getSystemInfo(): Promise<ApiResponse<SystemInfo>> {
  return apiClient.get('/settings/info')
}

export async function getAllSettings(): Promise<ApiResponse<SystemSetting[]>> {
  return apiClient.get('/settings')
}

export async function setSetting(key: string, value: string): Promise<ApiResponse<void>> {
  return apiClient.put(`/settings/${encodeURIComponent(key)}`, { value })
}

export async function cleanupData(): Promise<ApiResponse<CleanupResult>> {
  return apiClient.post('/settings/cleanup')
}
