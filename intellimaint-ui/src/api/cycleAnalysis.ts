import { apiClient } from './client';
import type {
  WorkCycle,
  CycleAnalysisResult,
  CycleStatsSummary,
  DeviceBaseline,
  AnalyzeCyclesRequest,
  LearnBaselinesRequest,
  WorkCycleQuery,
  CurrentAngleModel,
  MotorBalanceModel,
} from '../types/cycleAnalysis';

// ========== 周期分析 API ==========

export async function analyzeCycles(
  request: AnalyzeCyclesRequest,
  save: boolean = false
): Promise<CycleAnalysisResult> {
  const url = save ? '/cycle-analysis/analyze?save=true' : '/cycle-analysis/analyze';
  const response = await apiClient.post<{ success: boolean; data: CycleAnalysisResult; error?: string }>(
    url,
    request
  );
  if (!response.success) {
    throw new Error(response.error || '周期分析失败');
  }
  return response.data;
}

export async function getCycles(query?: WorkCycleQuery): Promise<WorkCycle[]> {
  const params = new URLSearchParams();
  if (query?.deviceId) params.set('deviceId', query.deviceId);
  if (query?.startTime !== undefined) params.set('startTime', String(query.startTime));
  if (query?.endTime !== undefined) params.set('endTime', String(query.endTime));
  if (query?.isAnomaly !== undefined) params.set('isAnomaly', String(query.isAnomaly));
  if (query?.anomalyType) params.set('anomalyType', query.anomalyType);
  if (query?.limit !== undefined) params.set('limit', String(query.limit));

  const queryStr = params.toString();
  const url = queryStr ? `/cycle-analysis/cycles?${queryStr}` : '/cycle-analysis/cycles';

  const response = await apiClient.get<{ success: boolean; data: WorkCycle[]; error?: string }>(url);
  if (!response.success) {
    throw new Error(response.error || '获取周期列表失败');
  }
  return response.data;
}

export async function getCycle(id: number): Promise<WorkCycle> {
  const response = await apiClient.get<{ success: boolean; data: WorkCycle; error?: string }>(
    `/cycle-analysis/cycles/${id}`
  );
  if (!response.success) {
    throw new Error(response.error || '获取周期详情失败');
  }
  return response.data;
}

export async function getRecentCycles(deviceId: string, count: number = 20): Promise<WorkCycle[]> {
  const response = await apiClient.get<{ success: boolean; data: WorkCycle[]; error?: string }>(
    `/cycle-analysis/cycles/recent/${encodeURIComponent(deviceId)}?count=${count}`
  );
  if (!response.success) {
    throw new Error(response.error || '获取最近周期失败');
  }
  return response.data;
}

export async function getAnomalyCycles(
  deviceId: string,
  after?: number,
  limit: number = 50
): Promise<WorkCycle[]> {
  const params = new URLSearchParams();
  if (after !== undefined) params.set('after', String(after));
  params.set('limit', String(limit));

  const response = await apiClient.get<{ success: boolean; data: WorkCycle[]; error?: string }>(
    `/cycle-analysis/cycles/anomalies/${encodeURIComponent(deviceId)}?${params}`
  );
  if (!response.success) {
    throw new Error(response.error || '获取异常周期失败');
  }
  return response.data;
}

export async function getCycleStats(
  deviceId: string,
  startTime?: number,
  endTime?: number
): Promise<CycleStatsSummary | null> {
  const params = new URLSearchParams();
  if (startTime !== undefined) params.set('startTime', String(startTime));
  if (endTime !== undefined) params.set('endTime', String(endTime));

  const queryStr = params.toString();
  const url = queryStr
    ? `/cycle-analysis/stats/${encodeURIComponent(deviceId)}?${queryStr}`
    : `/cycle-analysis/stats/${encodeURIComponent(deviceId)}`;

  const response = await apiClient.get<{ success: boolean; data: CycleStatsSummary | null; error?: string }>(url);
  if (!response.success) {
    throw new Error(response.error || '获取周期统计失败');
  }
  return response.data;
}

export async function deleteCycle(id: number): Promise<void> {
  const response = await apiClient.delete<{ success: boolean; error?: string }>(
    `/cycle-analysis/cycles/${id}`
  );
  if (!response.success) {
    throw new Error(response.error || '删除周期失败');
  }
}

// ========== 基线 API ==========

export async function getBaselines(deviceId: string): Promise<DeviceBaseline[]> {
  const response = await apiClient.get<{ success: boolean; data: DeviceBaseline[]; error?: string }>(
    `/baselines/${encodeURIComponent(deviceId)}`
  );
  if (!response.success) {
    throw new Error(response.error || '获取基线失败');
  }
  return response.data;
}

export async function learnBaselines(request: LearnBaselinesRequest): Promise<void> {
  const response = await apiClient.post<{ success: boolean; data: unknown; error?: string }>(
    '/baselines/learn',
    request
  );
  if (!response.success) {
    throw new Error(response.error || '基线学习失败');
  }
}

export async function learnCurrentAngleBaseline(
  deviceId: string,
  angleTagId: string,
  currentTagId: string,
  startTimeUtc: number,
  endTimeUtc: number
): Promise<CurrentAngleModel> {
  const response = await apiClient.post<{ success: boolean; data: CurrentAngleModel; error?: string }>(
    '/baselines/learn/current-angle',
    { deviceId, angleTagId, currentTagId, startTimeUtc, endTimeUtc }
  );
  if (!response.success) {
    throw new Error(response.error || '电流-角度基线学习失败');
  }
  return response.data;
}

export async function learnMotorBalanceBaseline(
  deviceId: string,
  motor1TagId: string,
  motor2TagId: string,
  startTimeUtc: number,
  endTimeUtc: number
): Promise<MotorBalanceModel> {
  const response = await apiClient.post<{ success: boolean; data: MotorBalanceModel; error?: string }>(
    '/baselines/learn/motor-balance',
    { deviceId, motor1TagId, motor2TagId, startTimeUtc, endTimeUtc }
  );
  if (!response.success) {
    throw new Error(response.error || '电机平衡基线学习失败');
  }
  return response.data;
}

export async function deleteBaseline(deviceId: string, baselineType: string): Promise<void> {
  const response = await apiClient.delete<{ success: boolean; error?: string }>(
    `/baselines/${encodeURIComponent(deviceId)}/${encodeURIComponent(baselineType)}`
  );
  if (!response.success) {
    throw new Error(response.error || '删除基线失败');
  }
}
