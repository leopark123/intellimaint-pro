// v63: 预测与预警 API 客户端
import apiClient from './client'
import type {
  TrendPredictionResponse,
  SingleTrendResponse,
  DegradationResponse,
  SingleDegradationResponse,
  AlertsSummaryResponse,
  RulPredictionResponse,
  SingleRulResponse
} from '../types/prediction'

/**
 * 获取所有设备的趋势预测
 */
export async function getAllTrendPredictions(): Promise<TrendPredictionResponse> {
  return apiClient.get('/predictions/trend')
}

/**
 * 获取单个设备的趋势预测
 */
export async function getDeviceTrendPrediction(
  deviceId: string
): Promise<SingleTrendResponse> {
  return apiClient.get(`/predictions/trend/${deviceId}`)
}

/**
 * 获取所有设备的劣化检测
 */
export async function getAllDegradations(): Promise<DegradationResponse> {
  return apiClient.get('/predictions/degradation')
}

/**
 * 获取单个设备的劣化检测
 */
export async function getDeviceDegradation(
  deviceId: string
): Promise<SingleDegradationResponse> {
  return apiClient.get(`/predictions/degradation/${deviceId}`)
}

/**
 * 获取预警汇总
 */
export async function getPredictionAlerts(): Promise<AlertsSummaryResponse> {
  return apiClient.get('/predictions/alerts')
}

/**
 * 获取所有设备的 RUL 预测
 */
export async function getAllRulPredictions(): Promise<RulPredictionResponse> {
  return apiClient.get('/predictions/rul')
}

/**
 * 获取单个设备的 RUL 预测
 */
export async function getDeviceRulPrediction(
  deviceId: string
): Promise<SingleRulResponse> {
  return apiClient.get(`/predictions/rul/${deviceId}`)
}
