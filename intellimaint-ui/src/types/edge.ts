// v65: Edge 配置管理类型定义

/** 预处理配置 */
export interface ProcessingConfig {
  enabled: boolean;
  defaultDeadband: number;
  defaultDeadbandPercent: number;
  defaultMinIntervalMs: number;
  forceUploadIntervalMs: number;
  outlierEnabled: boolean;
  outlierSigmaThreshold: number;
  outlierAction: 'Drop' | 'Mark' | 'Pass';
}

/** 断网续传配置 */
export interface StoreForwardConfig {
  enabled: boolean;
  maxStoreSizeMB: number;
  retentionDays: number;
  compressionEnabled: boolean;
  compressionAlgorithm: 'Gzip' | 'Brotli';
}

/** 网络配置 */
export interface NetworkConfig {
  healthCheckIntervalMs: number;
  healthCheckTimeoutMs: number;
  offlineThreshold: number;
  sendBatchSize: number;
  sendIntervalMs: number;
}

/** Edge 完整配置 */
export interface EdgeConfig {
  edgeId: string;
  name: string;
  description?: string;
  processing: ProcessingConfig;
  storeForward: StoreForwardConfig;
  network: NetworkConfig;
  createdUtc: number;
  updatedUtc?: number;
  updatedBy?: string;
}

/** 标签级处理配置 */
export interface TagProcessingConfig {
  id: number;
  edgeId: string;
  tagId: string;
  tagName?: string;
  deadband?: number;
  deadbandPercent?: number;
  minIntervalMs?: number;
  bypass: boolean;
  priority: number;
  description?: string;
  createdUtc: number;
  updatedUtc?: number;
}

/** Edge 状态 */
export interface EdgeStatus {
  edgeId: string;
  isOnline: boolean;
  pendingPoints: number;
  filterRate: number;
  sentCount: number;
  storedMB: number;
  lastHeartbeatUtc: number;
  version?: string;
}

/** Edge 摘要（用于列表） */
export interface EdgeSummary {
  edgeId: string;
  name: string;
  description?: string;
  isOnline: boolean;
  lastHeartbeatUtc?: number;
  deviceCount: number;
  tagCount: number;
}

/** 分页标签配置结果 */
export interface PagedTagConfigResult {
  items: TagProcessingConfig[];
  total: number;
  page: number;
  pageSize: number;
}

/** 批量更新请求 */
export interface BatchUpdateTagConfigRequest {
  tags: TagProcessingConfig[];
}

/** API 响应 */
export interface EdgeApiResponse<T> {
  success: boolean;
  data?: T;
  message?: string;
}
