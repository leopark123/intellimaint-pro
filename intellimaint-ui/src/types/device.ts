// v55: 添加 LibPlcTag 协议支持

// 设备状态
export type DeviceStatus = 'Running' | 'Stopped' | 'Fault' | 'Unknown'

// 协议类型 - v55: 添加 LibPlcTag
export type ProtocolType = 'ModbusTcp' | 'OpcUa' | 'S7' | 'Mqtt' | 'LibPlcTag'

// LibPlcTag PLC 类型
export type PlcType = 'ControlLogix' | 'CompactLogix' | 'Micro800' | 'MicroLogix' | 'PLC5' | 'SLC500'

// 设备
export interface Device {
  deviceId: string
  name?: string
  deviceType?: string
  description?: string
  location?: string
  model?: string
  connectionString?: string
  status: DeviceStatus
  enabled: boolean
  createdAt: number
  updatedAt?: number
  lastSeenAt?: number
  protocol?: ProtocolType
  host?: string
  port?: number
  metadata?: Record<string, any>
}

// 创建设备请求
export interface CreateDeviceRequest {
  deviceId: string
  name?: string
  deviceType?: string
  description?: string
  location?: string
  model?: string
  connectionString?: string
  protocol?: ProtocolType
  host?: string
  port?: number
  enabled?: boolean
  metadata?: Record<string, any>
}

// 更新设备请求
export interface UpdateDeviceRequest {
  name?: string
  deviceType?: string
  description?: string
  location?: string
  model?: string
  connectionString?: string
  enabled?: boolean
  protocol?: ProtocolType
  host?: string
  port?: number
  metadata?: Record<string, any>
}

// 设备统计
export interface DeviceStats {
  total: number
  running: number
  stopped: number
  fault: number
}

// 协议选项 - v55: 添加 LibPlcTag
export const ProtocolOptions = [
  { value: 'LibPlcTag', label: 'Allen-Bradley (LibPlcTag)' },
  { value: 'OpcUa', label: 'OPC UA' },
  { value: 'ModbusTcp', label: 'Modbus TCP' },
  { value: 'S7', label: 'Siemens S7' },
  { value: 'Mqtt', label: 'MQTT' }
]

// 默认端口 - v55: LibPlcTag 使用 44818 (EtherNet/IP)
export const DefaultPorts: Record<string, number> = {
  LibPlcTag: 44818,
  OpcUa: 4840,
  ModbusTcp: 502,
  S7: 102,
  Mqtt: 1883
}

// v55: LibPlcTag PLC 类型选项
export const PlcTypeOptions = [
  { value: 'ControlLogix', label: 'ControlLogix (L6x/L7x/L8x)' },
  { value: 'CompactLogix', label: 'CompactLogix (L1x/L2x/L3x)' },
  { value: 'Micro800', label: 'Micro800 (810/820/830/850)' },
  { value: 'MicroLogix', label: 'MicroLogix (1100/1400)' },
  { value: 'PLC5', label: 'PLC-5' },
  { value: 'SLC500', label: 'SLC 500' }
]

// v55: 判断是否是 LibPlcTag 协议
export function isLibPlcTag(protocol?: string): boolean {
  return protocol?.toLowerCase() === 'libplctag'
}
