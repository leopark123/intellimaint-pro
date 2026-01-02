import apiClient from './client'
import type { Tag, CreateTagRequest, UpdateTagRequest } from '../types/tag'

interface ApiResponse<T> {
  success: boolean;
  data?: T;
  error?: string;
}

// 获取标签（deviceId 可选，不传则获取所有标签）
export async function getTags(deviceId?: string): Promise<Tag[]> {
  const params = deviceId ? { deviceId } : undefined;
  const response = await apiClient.get<ApiResponse<Tag[]>>('/tags', { params });
  if (!response.success) {
    throw new Error(response.error || '获取标签列表失败');
  }
  return response.data || [];
}

export async function getTagsByDevice(deviceId: string): Promise<Tag[]> {
  const response = await apiClient.get<ApiResponse<Tag[]>>('/tags', { params: { deviceId } });
  if (!response.success) {
    throw new Error(response.error || '获取标签列表失败');
  }
  return response.data || [];
}

export async function getTag(tagId: string): Promise<Tag> {
  const response = await apiClient.get<ApiResponse<Tag>>(`/tags/${encodeURIComponent(tagId)}`);
  if (!response.success) {
    throw new Error(response.error || '获取标签失败');
  }
  return response.data!;
}

export async function createTag(request: CreateTagRequest): Promise<Tag> {
  const response = await apiClient.post<ApiResponse<Tag>>('/tags', request);
  if (!response.success) {
    throw new Error(response.error || '创建标签失败');
  }
  return response.data!;
}

export async function updateTag(tagId: string, request: UpdateTagRequest): Promise<Tag> {
  const response = await apiClient.put<ApiResponse<Tag>>(`/tags/${encodeURIComponent(tagId)}`, request);
  if (!response.success) {
    throw new Error(response.error || '更新标签失败');
  }
  return response.data!;
}

export async function deleteTag(tagId: string): Promise<void> {
  const response = await apiClient.delete<ApiResponse<void>>(`/tags/${encodeURIComponent(tagId)}`);
  if (!response.success) {
    throw new Error(response.error || '删除标签失败');
  }
}
