// v55: 添加 CipType 支持

export interface Tag {
  tagId: string
  deviceId: string
  name?: string | null
  description?: string | null
  unit?: string | null
  dataType: number
  enabled: boolean
  address?: string | null
  scanIntervalMs?: number | null
  tagGroup?: string | null
  createdUtc: number
  updatedUtc: number
  metadata?: Record<string, string> | null
}

export interface CreateTagRequest {
  tagId: string
  deviceId: string
  name?: string
  description?: string
  unit?: string
  dataType: number
  enabled?: boolean
  address?: string
  scanIntervalMs?: number
  tagGroup?: string
  metadata?: Record<string, string>
}

export interface UpdateTagRequest {
  name?: string
  description?: string
  unit?: string
  dataType?: number
  enabled?: boolean
  address?: string
  scanIntervalMs?: number
  tagGroup?: string
  metadata?: Record<string, string>
}

export const DataTypeOptions = [
  { label: 'Bool', value: 1 },
  { label: 'Int8', value: 2 },
  { label: 'UInt8', value: 3 },
  { label: 'Int16', value: 4 },
  { label: 'UInt16', value: 5 },
  { label: 'Int32', value: 6 },
  { label: 'UInt32', value: 7 },
  { label: 'Int64', value: 8 },
  { label: 'UInt64', value: 9 },
  { label: 'Float32', value: 10 },
  { label: 'Float64', value: 11 },
  { label: 'String', value: 12 },
  { label: 'DateTime', value: 13 },
  { label: 'ByteArray', value: 14 }
]

export const TagGroupOptions = [
  { label: 'Fast (100ms)', value: 'Fast' },
  { label: 'Normal (1s)', value: 'Normal' },
  { label: 'Slow (5s)', value: 'Slow' }
]

// v55: LibPlcTag CIP 数据类型
export const CipTypeOptions = [
  { label: 'BOOL', value: 'BOOL', dataType: 1 },
  { label: 'SINT (Int8)', value: 'SINT', dataType: 2 },
  { label: 'USINT (UInt8)', value: 'USINT', dataType: 3 },
  { label: 'INT (Int16)', value: 'INT', dataType: 4 },
  { label: 'UINT (UInt16)', value: 'UINT', dataType: 5 },
  { label: 'DINT (Int32)', value: 'DINT', dataType: 6 },
  { label: 'UDINT (UInt32)', value: 'UDINT', dataType: 7 },
  { label: 'LINT (Int64)', value: 'LINT', dataType: 8 },
  { label: 'ULINT (UInt64)', value: 'ULINT', dataType: 9 },
  { label: 'REAL (Float32)', value: 'REAL', dataType: 10 },
  { label: 'LREAL (Float64)', value: 'LREAL', dataType: 11 },
  { label: 'STRING', value: 'STRING', dataType: 12 }
]

// v55: 根据 CipType 获取对应的 DataType
export function cipTypeToDataType(cipType: string): number {
  const found = CipTypeOptions.find(o => o.value === cipType.toUpperCase())
  return found?.dataType ?? 10  // 默认 Float32
}

// v55: 根据 DataType 获取对应的 CipType
export function dataTypeToCipType(dataType: number): string {
  const found = CipTypeOptions.find(o => o.dataType === dataType)
  return found?.value ?? 'REAL'
}
