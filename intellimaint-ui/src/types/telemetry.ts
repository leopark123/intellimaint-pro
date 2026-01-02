// API 响应通用结构
export interface ApiResponse<T> {
  success: boolean
  data?: T
  error?: string
  message?: string
}

// 分页结果
export interface PagedResult<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}

// 遥测数据点 - 匹配后端 TelemetryDataPoint
export interface TelemetryPoint {
  deviceId: string
  tagId: string
  ts: number
  value: any  // 可能是 number, string, boolean 等
  valueType?: string
  quality: number
  unit?: string
}

// 遥测数据点（后端返回的格式）
export interface TelemetryDataPoint {
  deviceId: string
  tagId: string
  ts: number
  value: any
  valueType?: string  // v41.1 改为可选
  quality: number
  unit?: string
}

// 遥测统计
export interface TelemetryStats {
  totalCount: number
  deviceCount?: number
  tagCount?: number
  latestTs?: number
  oldestTs?: number
}

// 标签信息
export interface TagInfo {
  deviceId: string
  tagId: string
  valueType: string
  unit?: string
  lastTs?: number
  pointCount?: number
}

// 聚合数据点
export interface AggregatedPoint {
  ts: number
  value: number
  count?: number
  min?: number
  max?: number
}

// 查询参数
export interface TelemetryQueryParams {
  deviceId?: string
  tagId?: string
  startTs?: number
  endTs?: number
  limit?: number
  [key: string]: string | number | undefined
}

// 导出参数
export interface TelemetryExportParams {
  deviceId?: string
  tagId?: string
  startTs?: number
  endTs?: number
  format?: 'csv' | 'json'
  [key: string]: string | number | undefined
}

export interface AlarmExportParams {
  deviceId?: string
  status?: number
  severity?: number
  startTs?: number
  endTs?: number
  format?: 'csv' | 'json'
  [key: string]: string | number | undefined
}
