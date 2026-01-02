import apiClient from './client'
import type { Device, CreateDeviceRequest, UpdateDeviceRequest, DeviceStats } from '../types/device'

interface ApiResponse<T> {
  success: boolean;
  data?: T;
  error?: string;
}

export async function getDevices(): Promise<Device[]> {
  const response = await apiClient.get<ApiResponse<Device[]>>('/devices');
  if (!response.success) {
    throw new Error(response.error || '获取设备列表失败');
  }
  return response.data || [];
}

export async function getDevice(deviceId: string): Promise<Device> {
  const response = await apiClient.get<ApiResponse<Device>>(`/devices/${encodeURIComponent(deviceId)}`);
  if (!response.success) {
    throw new Error(response.error || '获取设备失败');
  }
  return response.data!;
}

export async function createDevice(request: CreateDeviceRequest): Promise<Device> {
  const response = await apiClient.post<ApiResponse<Device>>('/devices', request);
  if (!response.success) {
    throw new Error(response.error || '创建设备失败');
  }
  return response.data!;
}

export async function updateDevice(deviceId: string, request: UpdateDeviceRequest): Promise<Device> {
  const response = await apiClient.put<ApiResponse<Device>>(`/devices/${encodeURIComponent(deviceId)}`, request);
  if (!response.success) {
    throw new Error(response.error || '更新设备失败');
  }
  return response.data!;
}

export async function deleteDevice(deviceId: string): Promise<void> {
  const response = await apiClient.delete<ApiResponse<void>>(`/devices/${encodeURIComponent(deviceId)}`);
  if (!response.success) {
    throw new Error(response.error || '删除设备失败');
  }
}

export async function getDeviceStats(): Promise<DeviceStats> {
  const response = await apiClient.get<ApiResponse<DeviceStats>>('/devices/stats');
  if (!response.success) {
    throw new Error(response.error || '获取设备统计失败');
  }
  return response.data!;
}
