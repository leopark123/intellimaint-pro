// v63: 预测与预警类型定义

/** 预测告警级别 */
export type PredictionAlertLevel = 'none' | 'low' | 'medium' | 'high' | 'critical'

/** 劣化类型 */
export type DegradationType =
  | 'None'
  | 'GradualIncrease'
  | 'GradualDecrease'
  | 'IncreasingVariance'
  | 'CycleAnomaly'

/** RUL 状态 */
export type RulStatus =
  | 'Healthy'
  | 'NormalDegradation'
  | 'AcceleratedDegradation'
  | 'NearFailure'
  | 'InsufficientData'

/** RUL 风险等级 */
export type RulRiskLevel = 'low' | 'medium' | 'high' | 'critical'

/** 标签趋势预测 */
export interface TagTrendPrediction {
  tagId: string
  currentValue: number
  predictedValue: number
  trendSlope: number
  trendDirection: string
  confidence: number
  alertLevel: PredictionAlertLevel
  alertLevelCode: number
  hoursToThreshold: number | null
  alertMessage: string | null
}

/** 设备趋势汇总 */
export interface DeviceTrendSummary {
  deviceId: string
  timestamp: number
  maxAlertLevel: PredictionAlertLevel
  maxAlertLevelCode: number
  riskTagCount: number
  riskSummary: string | null
  predictions: TagTrendPrediction[]
}

/** 趋势预测响应 */
export interface TrendPredictionResponse {
  success: boolean
  data: DeviceTrendSummary[]
  summary: {
    totalDevices: number
    devicesWithAlerts: number
    criticalAlerts: number
    highAlerts: number
  }
}

/** 劣化检测结果 */
export interface DegradationResult {
  tagId: string
  timestamp: number
  isDegrading: boolean
  degradationType: DegradationType
  degradationRate: number
  startValue: number
  currentValue: number
  changePercent: number
  description: string
}

/** 设备劣化汇总 */
export interface DeviceDegradationSummary {
  deviceId: string
  degradingTags: number
  results: DegradationResult[]
}

/** 劣化检测响应 */
export interface DegradationResponse {
  success: boolean
  data: DeviceDegradationSummary[]
  summary: {
    totalDevices: number
    totalDegradingTags: number
    byType: {
      gradualIncrease: number
      gradualDecrease: number
      increasingVariance: number
    }
  }
}

/** 趋势预警 */
export interface TrendAlert {
  type: 'trend'
  deviceId: string
  tagId: string
  level: PredictionAlertLevel
  levelCode: number
  message: string
  hoursToThreshold: number | null
  confidence: number
}

/** 劣化预警 */
export interface DegradationAlert {
  type: 'degradation'
  deviceId: string
  tagId: string
  degradationType: DegradationType
  rate: number
  changePercent: number
  description: string
}

/** 预警汇总响应 */
export interface AlertsSummaryResponse {
  success: boolean
  data: {
    trendAlerts: TrendAlert[]
    degradationAlerts: DegradationAlert[]
    summary: {
      totalTrendAlerts: number
      totalDegradationAlerts: number
      criticalCount: number
      highCount: number
    }
  }
}

/** RUL 影响因素 */
export interface RulFactor {
  name: string
  tagId: string
  weight: number
  currentStatus: string
  contribution: number
}

/** RUL 预测结果 */
export interface RulPrediction {
  deviceId: string
  timestamp: number
  currentHealthIndex: number
  remainingUsefulLifeHours: number | null
  remainingUsefulLifeDays: number | null
  predictedFailureTime: number | null
  confidence: number
  degradationRate: number
  modelType: string
  status: RulStatus
  statusCode: number
  riskLevel: RulRiskLevel
  riskLevelCode: number
  recommendedMaintenanceTime: number | null
  diagnosticMessage: string
  factors: RulFactor[]
}

/** RUL 预测响应 */
export interface RulPredictionResponse {
  success: boolean
  data: RulPrediction[]
  summary: {
    totalDevices: number
    riskDistribution: {
      critical: number
      high: number
      medium: number
      low: number
    }
    statusDistribution: {
      healthy: number
      normalDegradation: number
      acceleratedDegradation: number
      nearFailure: number
      insufficientData: number
    }
    averageRulDays: number
  }
}

/** 单设备 RUL 响应 */
export interface SingleRulResponse {
  success: boolean
  data: RulPrediction
}

/** 单设备趋势响应 */
export interface SingleTrendResponse {
  success: boolean
  data: DeviceTrendSummary
}

/** 单设备劣化响应 */
export interface SingleDegradationResponse {
  success: boolean
  data: {
    deviceId: string
    degradingTags: number
    results: DegradationResult[]
  }
}

// 风险等级颜色映射
export const RiskLevelColors: Record<RulRiskLevel, string> = {
  low: '#52c41a',
  medium: '#faad14',
  high: '#ff7a45',
  critical: '#f5222d'
}

// RUL 状态颜色映射
export const RulStatusColors: Record<RulStatus, string> = {
  Healthy: '#52c41a',
  NormalDegradation: '#faad14',
  AcceleratedDegradation: '#ff7a45',
  NearFailure: '#f5222d',
  InsufficientData: '#8c8c8c'
}

// RUL 状态标签映射
export const RulStatusLabels: Record<RulStatus, string> = {
  Healthy: '健康',
  NormalDegradation: '正常老化',
  AcceleratedDegradation: '加速劣化',
  NearFailure: '临近失效',
  InsufficientData: '数据不足'
}

// 劣化类型标签映射
export const DegradationTypeLabels: Record<DegradationType, string> = {
  None: '无',
  GradualIncrease: '渐进上升',
  GradualDecrease: '渐进下降',
  IncreasingVariance: '波动增大',
  CycleAnomaly: '周期异常'
}
