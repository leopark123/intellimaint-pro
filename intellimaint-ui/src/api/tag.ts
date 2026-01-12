import apiClient from './client'
import type { Tag, CreateTagRequest, UpdateTagRequest } from '../types/tag'
import type { ApiResponse } from '../types/telemetry'

// 获取标签（deviceId 可选，不传则获取所有标签）
export async function getTags(deviceId?: string): Promise<Tag[]> {
  const params = deviceId ? { deviceId } : undefined
  const response = await apiClient.get('/tags', { params }) as ApiResponse<Tag[]>
  if (!response.success) {
    throw new Error(response.error || '获取标签列表失败')
  }
  return response.data || []
}

export async function getTagsByDevice(deviceId: string): Promise<Tag[]> {
  const response = await apiClient.get('/tags', { params: { deviceId } }) as ApiResponse<Tag[]>
  if (!response.success) {
    throw new Error(response.error || '获取标签列表失败')
  }
  return response.data || []
}

export async function getTag(tagId: string): Promise<Tag> {
  const response = await apiClient.get(`/tags/${encodeURIComponent(tagId)}`) as ApiResponse<Tag>
  if (!response.success) {
    throw new Error(response.error || '获取标签失败')
  }
  return response.data!
}

export async function createTag(request: CreateTagRequest): Promise<Tag> {
  const response = await apiClient.post('/tags', request) as ApiResponse<Tag>
  if (!response.success) {
    throw new Error(response.error || '创建标签失败')
  }
  return response.data!
}

export async function updateTag(tagId: string, request: UpdateTagRequest): Promise<Tag> {
  const response = await apiClient.put(`/tags/${encodeURIComponent(tagId)}`, request) as ApiResponse<Tag>
  if (!response.success) {
    throw new Error(response.error || '更新标签失败')
  }
  return response.data!
}

export async function deleteTag(tagId: string): Promise<void> {
  const response = await apiClient.delete(`/tags/${encodeURIComponent(tagId)}`) as ApiResponse<void>
  if (!response.success) {
    throw new Error(response.error || '删除标签失败')
  }
}
