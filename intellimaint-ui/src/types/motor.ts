// v64: 电机故障预测类型定义

/**
 * 电机模型
 */
export interface MotorModel {
  modelId: string
  name: string
  description: string | null
  type: number
  ratedPower: number
  ratedVoltage: number
  ratedCurrent: number
  ratedSpeed: number
  ratedFrequency: number
  polePairs: number
  vfdModel: string | null
  bearingModel: string | null
  bearingRollingElements: number | null
  bearingBallDiameter: number | null
  bearingPitchDiameter: number | null
  bearingContactAngle: number | null
  createdUtc: number
  createdBy: string | null
  updatedUtc: number | null
}

/**
 * 电机实例
 */
export interface MotorInstance {
  instanceId: string
  modelId: string
  deviceId: string
  name: string
  location: string | null
  installDate: string | null
  assetNumber: string | null
  diagnosisEnabled: boolean
  createdUtc: number
  updatedUtc: number | null
}

/**
 * 电机参数映射
 */
export interface MotorParameterMapping {
  mappingId: string
  instanceId: string
  parameter: number
  tagId: string
  scaleFactor: number
  offset: number
  usedForDiagnosis: boolean
}

/**
 * 电机实例详情（包含关联数据）
 */
export interface MotorInstanceDetail {
  instance: MotorInstance
  model: MotorModel | null
  mappings: MotorParameterMapping[]
  modes: OperationMode[]
  baselineCount: number
}

/**
 * 电机类型
 */
export interface MotorType {
  typeId: string
  name: string
  manufacturer: string | null
  model: string | null
  description: string | null
  defaultRatedPower: number
  defaultRatedSpeed: number
  defaultRatedVoltage: number
  defaultRatedCurrent: number
  defaultPolePairs: number
  createdUtc: number
}

/**
 * 电机参数类型
 */
export enum MotorParameter {
  // 电气参数
  CurrentPhaseA = 0,
  CurrentPhaseB = 1,
  CurrentPhaseC = 2,
  VoltagePhaseA = 3,
  VoltagePhaseB = 4,
  VoltagePhaseC = 5,
  PowerFactor = 6,
  ActivePower = 7,
  ReactivePower = 8,
  // 机械参数
  Speed = 10,
  Torque = 11,
  Vibration = 12,
  VibrationX = 13,
  VibrationY = 14,
  VibrationZ = 15,
  // 温度参数
  TemperatureWinding = 20,
  TemperatureBearing = 21,
  TemperatureAmbient = 22,
}

/**
 * 故障类型
 */
export enum MotorFaultType {
  // 电气故障
  PhaseImbalance = 0,
  Overcurrent = 1,
  Undercurrent = 2,
  Overvoltage = 3,
  Undervoltage = 4,
  // 机械故障
  RotorEccentricity = 10,
  BrokenRotorBar = 11,
  BearingOuterRace = 12,
  BearingInnerRace = 13,
  BearingBall = 14,
  BearingCage = 15,
  Misalignment = 16,
  Unbalance = 17,
  // 热故障
  Overheating = 20,
  // 运行故障
  Overload = 30,
  LightLoad = 31,
  ParameterDrift = 32,
  Unknown = 99,
}

/**
 * 故障严重程度
 */
export enum FaultSeverity {
  Normal = 0,
  Minor = 1,
  Moderate = 2,
  Severe = 3,
  Critical = 4,
}

/**
 * 运行模式
 */
export interface OperationMode {
  modeId: string
  instanceId: string
  name: string
  description: string | null
  triggerTagId: string
  triggerMinValue: number
  triggerMaxValue: number
  minDurationMs: number
  maxDurationMs: number
  priority: number
  enabled: boolean
  createdUtc: number
  updatedUtc: number | null
}

/**
 * 基线配置文件
 */
export interface BaselineProfile {
  profileId: string
  instanceId: string
  modeId: string
  parameter: MotorParameter
  parameterName: string
  mean: number
  stdDev: number
  min: number
  max: number
  sampleCount: number
  lastUpdated: number
}

/**
 * 学习状态枚举
 */
export enum MotorLearningStatus {
  Pending = 0,
  Running = 1,
  Completed = 2,
  Failed = 3,
}

/**
 * 学习任务状态 (与后端 MotorLearningTaskState 匹配)
 */
export interface MotorLearningTaskState {
  taskId: string
  instanceId: string
  modeId: string
  status: MotorLearningStatus
  progress: string | null
  message: string | null
  startTime: number
  endTime: number | null
  dataStartTs: number
  dataEndTs: number
  minSamples: number
}

/**
 * 基线学习状态 (前端自定义格式 - 已废弃，使用 MotorLearningTaskState)
 */
export interface BaselineLearningStatus {
  instanceId: string
  modeId: string
  modeName: string
  isLearning: boolean
  progress: number
  sampleCount: number
  requiredSamples: number
  startedAt: number | null
  completedAt: number | null
  parameters: {
    parameter: MotorParameter
    parameterName: string
    sampleCount: number
    mean: number
    stdDev: number
  }[]
}

/**
 * 检测到的故障
 */
export interface DetectedFault {
  faultType: MotorFaultType
  faultTypeName: string
  severity: FaultSeverity
  severityName: string
  confidence: number
  description: string | null
  affectedParameters: MotorParameter[]
  recommendedAction: string | null
}

/**
 * 参数偏差
 */
export interface ParameterDeviation {
  parameter: MotorParameter
  parameterName: string
  currentValue: number
  baselineMean: number
  baselineStdDev: number
  deviationSigma: number
  severity: FaultSeverity
  severityName: string
}

/**
 * 诊断结果
 */
export interface MotorDiagnosisResult {
  diagnosisId: string
  instanceId: string
  modeId: string
  modeName: string
  timestamp: number
  healthScore: number
  overallSeverity: FaultSeverity
  overallSeverityName: string
  faults: DetectedFault[]
  deviations: ParameterDeviation[]
  summary: string | null
  recommendations: string[]
}

/**
 * 创建电机模型请求
 */
export interface CreateMotorModelRequest {
  name: string
  description?: string
  type: number
  ratedPower: number
  ratedVoltage: number
  ratedCurrent: number
  ratedSpeed?: number
  ratedFrequency?: number
  polePairs?: number
  vfdModel?: string
  bearingModel?: string
  bearingRollingElements?: number
  bearingBallDiameter?: number
  bearingPitchDiameter?: number
  bearingContactAngle?: number
}

/**
 * 创建电机实例请求
 */
export interface CreateMotorInstanceRequest {
  modelId: string
  deviceId: string
  name: string
  location?: string
  installDate?: string
  assetNumber?: string
}

/**
 * 更新电机实例请求
 */
export interface UpdateMotorInstanceRequest {
  modelId?: string
  deviceId?: string
  name?: string
  location?: string
  installDate?: string
  assetNumber?: string
  diagnosisEnabled?: boolean
}

/**
 * 创建参数映射请求
 */
export interface CreateParameterMappingRequest {
  parameter: number
  tagId: string
  scaleFactor?: number
  offset?: number
  usedForDiagnosis?: boolean
}

/**
 * 创建操作模式请求
 */
export interface CreateOperationModeRequest {
  name: string
  description?: string
  triggerTagId: string
  triggerMinValue: number
  triggerMaxValue: number
  minDurationMs?: number
  maxDurationMs?: number
  priority?: number
}

/**
 * 诊断请求
 */
export interface DiagnoseRequest {
  minorThreshold?: number
  moderateThreshold?: number
  severeThreshold?: number
  criticalThreshold?: number
  enableAlarmGeneration?: boolean
}

/**
 * 故障类型名称映射
 */
export const FaultTypeNames: Record<MotorFaultType, string> = {
  [MotorFaultType.PhaseImbalance]: '三相不平衡',
  [MotorFaultType.Overcurrent]: '过电流',
  [MotorFaultType.Undercurrent]: '欠电流',
  [MotorFaultType.Overvoltage]: '过电压',
  [MotorFaultType.Undervoltage]: '欠电压',
  [MotorFaultType.RotorEccentricity]: '转子偏心',
  [MotorFaultType.BrokenRotorBar]: '转子断条',
  [MotorFaultType.BearingOuterRace]: '轴承外圈故障',
  [MotorFaultType.BearingInnerRace]: '轴承内圈故障',
  [MotorFaultType.BearingBall]: '轴承滚动体故障',
  [MotorFaultType.BearingCage]: '轴承保持架故障',
  [MotorFaultType.Misalignment]: '不对中',
  [MotorFaultType.Unbalance]: '不平衡',
  [MotorFaultType.Overheating]: '过热',
  [MotorFaultType.Overload]: '过载',
  [MotorFaultType.LightLoad]: '轻载',
  [MotorFaultType.ParameterDrift]: '参数漂移',
  [MotorFaultType.Unknown]: '未知故障',
}

/**
 * 严重程度名称映射
 */
export const SeverityNames: Record<FaultSeverity, string> = {
  [FaultSeverity.Normal]: '正常',
  [FaultSeverity.Minor]: '轻微',
  [FaultSeverity.Moderate]: '中度',
  [FaultSeverity.Severe]: '严重',
  [FaultSeverity.Critical]: '危急',
}

/**
 * 严重程度颜色映射
 */
export const SeverityColors: Record<FaultSeverity, string> = {
  [FaultSeverity.Normal]: '#52c41a',
  [FaultSeverity.Minor]: '#faad14',
  [FaultSeverity.Moderate]: '#fa8c16',
  [FaultSeverity.Severe]: '#f5222d',
  [FaultSeverity.Critical]: '#cf1322',
}

/**
 * 参数名称映射
 */
export const ParameterNames: Record<MotorParameter, string> = {
  [MotorParameter.CurrentPhaseA]: 'A相电流',
  [MotorParameter.CurrentPhaseB]: 'B相电流',
  [MotorParameter.CurrentPhaseC]: 'C相电流',
  [MotorParameter.VoltagePhaseA]: 'A相电压',
  [MotorParameter.VoltagePhaseB]: 'B相电压',
  [MotorParameter.VoltagePhaseC]: 'C相电压',
  [MotorParameter.PowerFactor]: '功率因数',
  [MotorParameter.ActivePower]: '有功功率',
  [MotorParameter.ReactivePower]: '无功功率',
  [MotorParameter.Speed]: '转速',
  [MotorParameter.Torque]: '扭矩',
  [MotorParameter.Vibration]: '振动',
  [MotorParameter.VibrationX]: 'X轴振动',
  [MotorParameter.VibrationY]: 'Y轴振动',
  [MotorParameter.VibrationZ]: 'Z轴振动',
  [MotorParameter.TemperatureWinding]: '绕组温度',
  [MotorParameter.TemperatureBearing]: '轴承温度',
  [MotorParameter.TemperatureAmbient]: '环境温度',
}
