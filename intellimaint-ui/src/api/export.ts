const API_BASE = '/api'

export interface TelemetryExportParams {
  deviceId?: string
  tagId?: string
  startTs?: number
  endTs?: number
  limit?: number
  [key: string]: string | number | undefined  // v41.1 添加索引签名
}

export interface AlarmExportParams {
  deviceId?: string
  status?: number
  minSeverity?: number
  startTs?: number
  endTs?: number
  limit?: number
  [key: string]: string | number | undefined  // v41.1 添加索引签名
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

export function exportTelemetryCsv(params: TelemetryExportParams): void {
  const url = `${API_BASE}/export/telemetry/csv${buildQueryString(params)}`
  window.open(url, '_blank')
}

export function exportTelemetryXlsx(params: TelemetryExportParams): void {
  const url = `${API_BASE}/export/telemetry/xlsx${buildQueryString(params)}`
  window.open(url, '_blank')
}

export function exportAlarmsCsv(params: AlarmExportParams): void {
  const url = `${API_BASE}/export/alarms/csv${buildQueryString(params)}`
  window.open(url, '_blank')
}

export function exportAlarmsXlsx(params: AlarmExportParams): void {
  const url = `${API_BASE}/export/alarms/xlsx${buildQueryString(params)}`
  window.open(url, '_blank')
}
