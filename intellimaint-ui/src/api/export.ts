import apiClient from './client'
import { logError } from '../utils/logger'

export interface TelemetryExportParams {
  deviceId?: string
  tagId?: string
  startTs?: number
  endTs?: number
  limit?: number
  [key: string]: string | number | undefined
}

export interface AlarmExportParams {
  deviceId?: string
  status?: number
  minSeverity?: number
  startTs?: number
  endTs?: number
  limit?: number
  [key: string]: string | number | undefined
}

function buildQueryString(params: Record<string, string | number | undefined>): string {
  const query = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') {
      query.append(key, String(value))
    }
  }
  const qs = query.toString()
  return qs ? `?${qs}` : ''
}

// v62: 修复导出功能 - 使用 fetch 携带认证 Token 下载文件
async function downloadFile(url: string, filename: string): Promise<void> {
  try {
    const response = await apiClient.getBlob(url)

    // 创建 Blob URL 并触发下载
    const blob = new Blob([response], { type: response.type || 'application/octet-stream' })
    const blobUrl = window.URL.createObjectURL(blob)

    const link = document.createElement('a')
    link.href = blobUrl
    link.download = filename
    link.style.display = 'none'
    document.body.appendChild(link)
    link.click()

    // 清理
    setTimeout(() => {
      document.body.removeChild(link)
      window.URL.revokeObjectURL(blobUrl)
    }, 100)
  } catch (error) {
    logError('Download failed', error, 'Export')
    throw error
  }
}

function generateFilename(prefix: string, ext: string): string {
  const now = new Date()
  const timestamp = now.toISOString().replace(/[:.]/g, '-').slice(0, 19)
  return `${prefix}_${timestamp}.${ext}`
}

export async function exportTelemetryCsv(params: TelemetryExportParams): Promise<void> {
  const url = `/export/telemetry/csv${buildQueryString(params)}`
  await downloadFile(url, generateFilename('telemetry', 'csv'))
}

export async function exportTelemetryXlsx(params: TelemetryExportParams): Promise<void> {
  const url = `/export/telemetry/xlsx${buildQueryString(params)}`
  await downloadFile(url, generateFilename('telemetry', 'xlsx'))
}

export async function exportAlarmsCsv(params: AlarmExportParams): Promise<void> {
  const url = `/export/alarms/csv${buildQueryString(params)}`
  await downloadFile(url, generateFilename('alarms', 'csv'))
}

export async function exportAlarmsXlsx(params: AlarmExportParams): Promise<void> {
  const url = `/export/alarms/xlsx${buildQueryString(params)}`
  await downloadFile(url, generateFilename('alarms', 'xlsx'))
}
