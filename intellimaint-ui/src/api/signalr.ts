import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  HttpTransportType,
  LogLevel
} from '@microsoft/signalr'
import { getToken, refreshTokenIfNeeded, isTokenExpiringSoon } from '../store/authStore'
import { logError, logInfo, logWarn } from '../utils/logger'

export interface TelemetryDataPoint {
  deviceId: string
  tagId: string
  ts: number
  value: number | string | boolean | null
  valueType?: string  // v41.1 改为可选
  quality: number
  unit?: string | null
}

type DataCallback = (points: TelemetryDataPoint[]) => void
type ConnectionCallback = (connected: boolean, state: HubConnectionState) => void

export class TelemetrySignalR {
  private connection: HubConnection | null = null
  private dataCallbacks: DataCallback[] = []
  private connectionCallbacks: ConnectionCallback[] = []

  public async connect(): Promise<void> {
    if (this.connection && this.connection.state !== HubConnectionState.Disconnected) {
      return
    }

    this.connection = new HubConnectionBuilder()
      .withUrl('/hubs/telemetry', {
        // Allow WebSocket / SSE / LongPolling fallback
        transport:
          HttpTransportType.WebSockets |
          HttpTransportType.ServerSentEvents |
          HttpTransportType.LongPolling,
        withCredentials: true,
        // v43: 添加 JWT Token，支持自动刷新
        accessTokenFactory: async () => {
          // 检查 token 是否快过期，如果是则刷新
          if (isTokenExpiringSoon()) {
            await refreshTokenIfNeeded()
          }
          return getToken() || ''
        }
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 20000])
      .configureLogging(LogLevel.Information)
      .build()

    this.connection.on('ReceiveData', (payload: TelemetryDataPoint[]) => {
      for (const cb of this.dataCallbacks) {
        try {
          cb(payload)
        } catch (e) {
          logError('Data callback error', e, 'SignalR')
        }
      }
    })

    this.connection.onreconnecting((err) => {
      logWarn('SignalR reconnecting...', err, 'SignalR')
      this.emitConnection(false)
    })

    this.connection.onreconnected(() => {
      logInfo('SignalR reconnected', undefined, 'SignalR')
      this.emitConnection(true)
      // Re-subscribe after reconnect (hook will call subscribeAll)
    })

    this.connection.onclose((err) => {
      logWarn('SignalR closed', err, 'SignalR')
      this.emitConnection(false)
    })

    await this.connection.start()
    logInfo('SignalR connected', undefined, 'SignalR')
    this.emitConnection(true)
  }

  public async disconnect(): Promise<void> {
    if (!this.connection) return
    try {
      await this.connection.stop()
    } catch (e) {
      logError('Disconnect error', e, 'SignalR')
    } finally {
      this.emitConnection(false)
    }
  }

  public async subscribeAll(): Promise<void> {
    if (!this.connection || this.connection.state !== HubConnectionState.Connected) return
    try {
      await this.connection.invoke('SubscribeAll')
      logInfo('Subscribed to all data', undefined, 'SignalR')
    } catch (e) {
      logError('SubscribeAll error', e, 'SignalR')
    }
  }

  public async subscribeDevice(deviceId: string): Promise<void> {
    if (!deviceId) return
    if (!this.connection || this.connection.state !== HubConnectionState.Connected) return
    try {
      await this.connection.invoke('SubscribeDevice', deviceId)
      logInfo('Subscribed to device', deviceId, 'SignalR')
    } catch (e) {
      logError('SubscribeDevice error', e, 'SignalR')
    }
  }

  public async unsubscribeDevice(deviceId: string): Promise<void> {
    if (!deviceId) return
    if (!this.connection || this.connection.state !== HubConnectionState.Connected) return
    try {
      await this.connection.invoke('UnsubscribeDevice', deviceId)
      logInfo('Unsubscribed from device', deviceId, 'SignalR')
    } catch (e) {
      logError('UnsubscribeDevice error', e, 'SignalR')
    }
  }

  public async unsubscribeAll(): Promise<void> {
    if (!this.connection || this.connection.state !== HubConnectionState.Connected) return
    try {
      await this.connection.invoke('UnsubscribeAll')
      logInfo('Unsubscribed from all data', undefined, 'SignalR')
    } catch (e) {
      logError('UnsubscribeAll error', e, 'SignalR')
    }
  }

  /**
   * 智能切换订阅：根据 deviceId 自动选择订阅全部或单设备
   */
  public async switchSubscription(deviceId?: string): Promise<void> {
    if (!this.connection || this.connection.state !== HubConnectionState.Connected) {
      await this.connect()
    }

    // 先取消所有订阅
    await this.unsubscribeAll()

    // 建立新订阅
    if (deviceId) {
      await this.subscribeDevice(deviceId)
    } else {
      await this.subscribeAll()
    }
  }

  public onData(callback: DataCallback): () => void {
    this.dataCallbacks.push(callback)
    return () => {
      this.dataCallbacks = this.dataCallbacks.filter((x) => x !== callback)
    }
  }

  public onConnectionChange(callback: ConnectionCallback): () => void {
    this.connectionCallbacks.push(callback)
    return () => {
      this.connectionCallbacks = this.connectionCallbacks.filter((x) => x !== callback)
    }
  }

  public isConnected(): boolean {
    return !!this.connection && this.connection.state === HubConnectionState.Connected
  }

  public getState(): HubConnectionState {
    return this.connection?.state ?? HubConnectionState.Disconnected
  }

  private emitConnection(connected: boolean) {
    const state = this.getState()
    for (const cb of this.connectionCallbacks) {
      try {
        cb(connected, state)
      } catch (e) {
        logError('Connection callback error', e, 'SignalR')
      }
    }
  }
}

// Singleton instance
export const telemetrySignalR = new TelemetrySignalR()
