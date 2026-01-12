import apiClient from './client'
import type { Device, CreateDeviceRequest, UpdateDeviceRequest, DeviceStats } from '../types/device'
import type { ApiResponse } from '../types/telemetry'

export async function getDevices(): Promise<Device[]> {
  const response = await apiClient.get('/devices') as ApiResponse<Device[]>
  if (!response.success) {
    throw new Error(response.error || '获取设备列表失败')
  }
  return response.data || []
}

export async function getDevice(deviceId: string): Promise<Device> {
  const response = await apiClient.get(`/devices/${encodeURIComponent(deviceId)}`) as ApiResponse<Device>
  if (!response.success) {
    throw new Error(response.error || '获取设备失败')
  }
  return response.data!
}

export async function createDevice(request: CreateDeviceRequest): Promise<Device> {
  const response = await apiClient.post('/devices', request) as ApiResponse<Device>
  if (!response.success) {
    throw new Error(response.error || '创建设备失败')
  }
  return response.data!
}

export async function updateDevice(deviceId: string, request: UpdateDeviceRequest): Promise<Device> {
  const response = await apiClient.put(`/devices/${encodeURIComponent(deviceId)}`, request) as ApiResponse<Device>
  if (!response.success) {
    throw new Error(response.error || '更新设备失败')
  }
  return response.data!
}

export async function deleteDevice(deviceId: string): Promise<void> {
  const response = await apiClient.delete(`/devices/${encodeURIComponent(deviceId)}`) as ApiResponse<void>
  if (!response.success) {
    throw new Error(response.error || '删除设备失败')
  }
}

export async function getDeviceStats(): Promise<DeviceStats> {
  const response = await apiClient.get('/devices/stats') as ApiResponse<DeviceStats>
  if (!response.success) {
    throw new Error(response.error || '获取设备统计失败')
  }
  return response.data!
}
