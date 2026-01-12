// v64: 电机故障预测 API

import apiClient from './client'
import type {
  MotorModel,
  MotorInstance,
  MotorInstanceDetail,
  MotorType,
  BaselineProfile,
  MotorLearningTaskState,
  MotorDiagnosisResult,
  MotorParameterMapping,
  OperationMode,
  CreateMotorModelRequest,
  CreateMotorInstanceRequest,
  UpdateMotorInstanceRequest,
  CreateParameterMappingRequest,
  CreateOperationModeRequest,
  DiagnoseRequest,
} from '../types/motor'

interface ApiResponse<T> {
  success: boolean
  data: T
  error?: string
  message?: string
}

// ==================== 电机模型 ====================

/**
 * 获取所有电机模型
 */
export async function getMotorModels(): Promise<ApiResponse<MotorModel[]>> {
  return apiClient.get('/motor-models')
}

/**
 * 获取单个电机模型
 */
export async function getMotorModel(modelId: string): Promise<ApiResponse<MotorModel>> {
  return apiClient.get(`/motor-models/${modelId}`)
}

/**
 * 创建电机模型
 */
export async function createMotorModel(request: CreateMotorModelRequest): Promise<ApiResponse<MotorModel>> {
  return apiClient.post('/motor-models', request)
}

/**
 * 更新电机模型
 */
export async function updateMotorModel(modelId: string, request: CreateMotorModelRequest): Promise<ApiResponse<MotorModel>> {
  return apiClient.put(`/motor-models/${modelId}`, request)
}

/**
 * 删除电机模型
 */
export async function deleteMotorModel(modelId: string): Promise<ApiResponse<void>> {
  return apiClient.delete(`/motor-models/${modelId}`)
}

// ==================== 电机类型 ====================

/**
 * 获取所有电机类型
 */
export async function getMotorTypes(): Promise<ApiResponse<MotorType[]>> {
  return apiClient.get('/motor-types')
}

/**
 * 获取单个电机类型
 */
export async function getMotorType(typeId: string): Promise<ApiResponse<MotorType>> {
  return apiClient.get(`/motor-types/${typeId}`)
}

/**
 * 创建电机类型
 */
export async function createMotorType(
  request: Partial<MotorType>
): Promise<ApiResponse<MotorType>> {
  return apiClient.post('/motor-types', request)
}

// ==================== 电机实例 ====================

/**
 * 获取所有电机实例
 */
export async function getMotorInstances(): Promise<ApiResponse<MotorInstance[]>> {
  return apiClient.get('/motor-instances')
}

/**
 * 获取单个电机实例
 */
export async function getMotorInstance(instanceId: string): Promise<ApiResponse<MotorInstance>> {
  return apiClient.get(`/motor-instances/${instanceId}`)
}

/**
 * 创建电机实例
 */
export async function createMotorInstance(
  request: CreateMotorInstanceRequest
): Promise<ApiResponse<MotorInstance>> {
  return apiClient.post('/motor-instances', request)
}

/**
 * 更新电机实例
 */
export async function updateMotorInstance(
  instanceId: string,
  request: UpdateMotorInstanceRequest
): Promise<ApiResponse<MotorInstance>> {
  return apiClient.put(`/motor-instances/${instanceId}`, request)
}

/**
 * 删除电机实例
 */
export async function deleteMotorInstance(instanceId: string): Promise<ApiResponse<void>> {
  return apiClient.delete(`/motor-instances/${instanceId}`)
}

// ==================== 基线学习 ====================

/**
 * 获取电机实例的所有基线
 */
export async function getBaselines(instanceId: string): Promise<ApiResponse<BaselineProfile[]>> {
  return apiClient.get(`/motor-instances/${instanceId}/baselines`)
}

/**
 * 获取基线学习任务列表
 */
export async function getBaselineLearningStatus(
  instanceId: string
): Promise<ApiResponse<MotorLearningTaskState[]>> {
  return apiClient.get(`/motor-instances/${instanceId}/learning-tasks`)
}

/**
 * 开始基线学习（指定模式）
 */
export async function startBaselineLearning(
  instanceId: string,
  modeId: string
): Promise<ApiResponse<{ message: string; taskId: string }>> {
  return apiClient.post(`/motor-instances/${instanceId}/learn`, { modeId })
}

/**
 * 学习所有模式的基线
 */
export async function learnAllModes(
  instanceId: string
): Promise<ApiResponse<{ message: string; taskId: string }>> {
  return apiClient.post(`/motor-instances/${instanceId}/learn-all`, {})
}

/**
 * 删除基线
 */
export async function deleteBaseline(
  instanceId: string,
  modeId?: string
): Promise<ApiResponse<void>> {
  const params = modeId ? `?modeId=${modeId}` : ''
  return apiClient.delete(`/motor-instances/${instanceId}/baselines${params}`)
}

// ==================== 故障诊断 ====================

/**
 * 执行诊断
 */
export async function diagnoseMotor(
  instanceId: string,
  request?: DiagnoseRequest
): Promise<ApiResponse<MotorDiagnosisResult>> {
  return apiClient.post(`/motor-instances/${instanceId}/diagnose`, request || {})
}

/**
 * 获取最新诊断结果
 */
export async function getLatestDiagnosis(
  instanceId: string
): Promise<ApiResponse<MotorDiagnosisResult>> {
  return apiClient.get(`/motor-instances/${instanceId}/diagnosis`)
}

/**
 * 获取所有电机的最新诊断结果
 */
export async function getAllDiagnoses(): Promise<ApiResponse<MotorDiagnosisResult[]>> {
  return apiClient.get('/motor-diagnoses')
}

// ==================== 运行模式 ====================

/**
 * 获取电机实例的运行模式
 */
export async function getOperationModes(
  instanceId: string
): Promise<ApiResponse<{ modeId: string; name: string; description: string }[]>> {
  return apiClient.get(`/motor-instances/${instanceId}/modes`)
}

/**
 * 检测当前运行模式
 */
export async function detectCurrentMode(
  instanceId: string
): Promise<ApiResponse<{ modeId: string; modeName: string; confidence: number }>> {
  return apiClient.get(`/motor-instances/${instanceId}/current-mode`)
}

// ==================== 电机实例详情 ====================

/**
 * 获取电机实例详情（包含模型、映射、模式）
 */
export async function getMotorInstanceDetail(instanceId: string): Promise<ApiResponse<MotorInstanceDetail>> {
  return apiClient.get(`/motor-instances/${instanceId}/detail`)
}

// ==================== 参数映射 ====================

/**
 * 获取电机实例的参数映射
 */
export async function getParameterMappings(instanceId: string): Promise<ApiResponse<MotorParameterMapping[]>> {
  return apiClient.get(`/motor-instances/${instanceId}/mappings`)
}

/**
 * 创建参数映射
 */
export async function createParameterMapping(
  instanceId: string,
  request: CreateParameterMappingRequest
): Promise<ApiResponse<MotorParameterMapping>> {
  return apiClient.post(`/motor-instances/${instanceId}/mappings`, request)
}

/**
 * 批量创建参数映射
 */
export async function createParameterMappingBatch(
  instanceId: string,
  mappings: CreateParameterMappingRequest[]
): Promise<ApiResponse<{ created: number }>> {
  return apiClient.post(`/motor-instances/${instanceId}/mappings/batch`, mappings)
}

/**
 * 删除参数映射
 */
export async function deleteParameterMapping(instanceId: string, mappingId: string): Promise<ApiResponse<void>> {
  return apiClient.delete(`/motor-instances/${instanceId}/mappings/${mappingId}`)
}

// ==================== 操作模式管理 ====================

/**
 * 创建操作模式
 */
export async function createOperationMode(
  instanceId: string,
  request: CreateOperationModeRequest
): Promise<ApiResponse<OperationMode>> {
  return apiClient.post(`/motor-instances/${instanceId}/modes`, request)
}

/**
 * 更新操作模式
 */
export async function updateOperationMode(
  instanceId: string,
  modeId: string,
  request: CreateOperationModeRequest
): Promise<ApiResponse<OperationMode>> {
  return apiClient.put(`/motor-instances/${instanceId}/modes/${modeId}`, request)
}

/**
 * 删除操作模式
 */
export async function deleteOperationMode(instanceId: string, modeId: string): Promise<ApiResponse<void>> {
  return apiClient.delete(`/motor-instances/${instanceId}/modes/${modeId}`)
}

/**
 * 设置操作模式启用状态
 */
export async function setOperationModeEnabled(
  instanceId: string,
  modeId: string,
  enabled: boolean
): Promise<ApiResponse<{ modeId: string; enabled: boolean }>> {
  return apiClient.patch(`/motor-instances/${instanceId}/modes/${modeId}/enable?enabled=${enabled}`)
}
