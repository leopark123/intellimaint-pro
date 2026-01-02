import { apiClient } from './client';
import type {
  CollectionRule,
  CollectionSegment,
  CreateCollectionRuleRequest,
  UpdateCollectionRuleRequest,
  TestConditionRequest,
  TestConditionResult,
} from '../types/collectionRule';

// ========== 采集规则 API ==========

export async function getCollectionRules(params?: {
  deviceId?: string;
  enabledOnly?: boolean;
}): Promise<CollectionRule[]> {
  const searchParams = new URLSearchParams();
  if (params?.deviceId) searchParams.set('deviceId', params.deviceId);
  if (params?.enabledOnly !== undefined) searchParams.set('enabledOnly', String(params.enabledOnly));
  
  const query = searchParams.toString();
  const url = query ? `/collection-rules?${query}` : '/collection-rules';
  
  const response = await apiClient.get<{ success: boolean; data: CollectionRule[]; error?: string }>(url);
  if (!response.success) {
    throw new Error(response.error || '获取采集规则失败');
  }
  return response.data;
}

export async function getCollectionRule(ruleId: string): Promise<CollectionRule> {
  const response = await apiClient.get<{ success: boolean; data: CollectionRule; error?: string }>(
    `/collection-rules/${encodeURIComponent(ruleId)}`
  );
  if (!response.success) {
    throw new Error(response.error || '获取采集规则失败');
  }
  return response.data;
}

export async function createCollectionRule(request: CreateCollectionRuleRequest): Promise<CollectionRule> {
  const response = await apiClient.post<{ success: boolean; data: CollectionRule; error?: string }>(
    '/collection-rules',
    request
  );
  if (!response.success) {
    throw new Error(response.error || '创建采集规则失败');
  }
  return response.data;
}

export async function updateCollectionRule(
  ruleId: string,
  request: UpdateCollectionRuleRequest
): Promise<CollectionRule> {
  const response = await apiClient.put<{ success: boolean; data: CollectionRule; error?: string }>(
    `/collection-rules/${encodeURIComponent(ruleId)}`,
    request
  );
  if (!response.success) {
    throw new Error(response.error || '更新采集规则失败');
  }
  return response.data;
}

export async function deleteCollectionRule(ruleId: string): Promise<void> {
  const response = await apiClient.delete<{ success: boolean; error?: string }>(
    `/collection-rules/${encodeURIComponent(ruleId)}`
  );
  if (!response.success) {
    throw new Error(response.error || '删除采集规则失败');
  }
}

export async function enableCollectionRule(ruleId: string): Promise<void> {
  const response = await apiClient.put<{ success: boolean; error?: string }>(
    `/collection-rules/${encodeURIComponent(ruleId)}/enable`
  );
  if (!response.success) {
    throw new Error(response.error || '启用采集规则失败');
  }
}

export async function disableCollectionRule(ruleId: string): Promise<void> {
  const response = await apiClient.put<{ success: boolean; error?: string }>(
    `/collection-rules/${encodeURIComponent(ruleId)}/disable`
  );
  if (!response.success) {
    throw new Error(response.error || '禁用采集规则失败');
  }
}

export async function testCondition(request: TestConditionRequest): Promise<TestConditionResult> {
  const response = await apiClient.post<{ success: boolean; data: TestConditionResult; error?: string }>(
    '/collection-rules/test',
    request
  );
  if (!response.success) {
    throw new Error(response.error || '测试条件失败');
  }
  return response.data;
}

// ========== 采集片段 API ==========

export async function getCollectionSegments(params?: {
  ruleId?: string;
  deviceId?: string;
  status?: number;
  startTime?: number;
  endTime?: number;
  limit?: number;
}): Promise<CollectionSegment[]> {
  const searchParams = new URLSearchParams();
  if (params?.ruleId) searchParams.set('ruleId', params.ruleId);
  if (params?.deviceId) searchParams.set('deviceId', params.deviceId);
  if (params?.status !== undefined) searchParams.set('status', String(params.status));
  if (params?.startTime !== undefined) searchParams.set('startTime', String(params.startTime));
  if (params?.endTime !== undefined) searchParams.set('endTime', String(params.endTime));
  if (params?.limit !== undefined) searchParams.set('limit', String(params.limit));
  
  const query = searchParams.toString();
  const url = query ? `/collection-segments?${query}` : '/collection-segments';
  
  const response = await apiClient.get<{ success: boolean; data: CollectionSegment[]; error?: string }>(url);
  if (!response.success) {
    throw new Error(response.error || '获取采集片段失败');
  }
  return response.data;
}

export async function getCollectionSegment(id: number): Promise<CollectionSegment> {
  const response = await apiClient.get<{ success: boolean; data: CollectionSegment; error?: string }>(
    `/collection-segments/${id}`
  );
  if (!response.success) {
    throw new Error(response.error || '获取采集片段失败');
  }
  return response.data;
}

export async function deleteCollectionSegment(id: number): Promise<void> {
  const response = await apiClient.delete<{ success: boolean; error?: string }>(
    `/collection-segments/${id}`
  );
  if (!response.success) {
    throw new Error(response.error || '删除采集片段失败');
  }
}
