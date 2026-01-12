import { apiClient } from './client';
import type {
  EdgeConfig,
  EdgeSummary,
  EdgeStatus,
  TagProcessingConfig,
  PagedTagConfigResult,
  EdgeApiResponse,
} from '../types/edge';

const BASE_URL = '/edge-config';

/** 获取所有 Edge 列表 */
export async function listEdges(): Promise<EdgeSummary[]> {
  const res = await apiClient.get<EdgeApiResponse<EdgeSummary[]>>(BASE_URL) as unknown as EdgeApiResponse<EdgeSummary[]>;
  return res.data ?? [];
}

/** 获取 Edge 配置 */
export async function getEdgeConfig(edgeId: string): Promise<EdgeConfig | null> {
  const res = await apiClient.get<EdgeApiResponse<EdgeConfig>>(`${BASE_URL}/${edgeId}`) as unknown as EdgeApiResponse<EdgeConfig>;
  return res.data ?? null;
}

/** 更新 Edge 配置 */
export async function updateEdgeConfig(edgeId: string, config: EdgeConfig): Promise<EdgeConfig> {
  const res = await apiClient.put<EdgeApiResponse<EdgeConfig>>(`${BASE_URL}/${edgeId}`, config) as unknown as EdgeApiResponse<EdgeConfig>;
  if (!res.success || !res.data) {
    throw new Error(res.message ?? 'Failed to update edge config');
  }
  return res.data;
}

/** 获取标签配置列表 */
export async function getTagConfigs(
  edgeId: string,
  page = 1,
  pageSize = 50,
  search?: string
): Promise<PagedTagConfigResult> {
  const params = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize),
  });
  if (search) {
    params.append('search', search);
  }
  const res = await apiClient.get<EdgeApiResponse<PagedTagConfigResult>>(
    `${BASE_URL}/${edgeId}/tags?${params}`
  ) as unknown as EdgeApiResponse<PagedTagConfigResult>;
  return res.data ?? { items: [], total: 0, page, pageSize };
}

/** 批量更新标签配置 */
export async function batchUpdateTagConfigs(
  edgeId: string,
  tags: TagProcessingConfig[]
): Promise<void> {
  const res = await apiClient.put<EdgeApiResponse<unknown>>(`${BASE_URL}/${edgeId}/tags`, { tags }) as unknown as EdgeApiResponse<unknown>;
  if (!res.success) {
    throw new Error(res.message ?? 'Failed to update tag configs');
  }
}

/** 删除标签配置 */
export async function deleteTagConfig(edgeId: string, tagId: string): Promise<void> {
  await apiClient.delete(`${BASE_URL}/${edgeId}/tags/${tagId}`);
}

/** 获取 Edge 状态 */
export async function getEdgeStatus(edgeId: string): Promise<EdgeStatus | null> {
  const res = await apiClient.get<EdgeApiResponse<EdgeStatus>>(`${BASE_URL}/${edgeId}/status`) as unknown as EdgeApiResponse<EdgeStatus>;
  return res.data ?? null;
}

/** 通知 Edge 同步配置 */
export async function notifyConfigSync(edgeId: string): Promise<void> {
  const res = await apiClient.post<EdgeApiResponse<unknown>>(`${BASE_URL}/${edgeId}/sync`) as unknown as EdgeApiResponse<unknown>;
  if (!res.success) {
    throw new Error(res.message ?? 'Failed to notify sync');
  }
}
