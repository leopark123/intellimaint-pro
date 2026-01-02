import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  HttpTransportType,
  LogLevel
} from '@microsoft/signalr'
import { getToken, refreshTokenIfNeeded, isTokenExpiringSoon } from '../store/authStore'

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
          console.error('Data callback error:', e)
        }
      }
    })

    this.connection.onreconnecting((err) => {
      console.warn('SignalR reconnecting...', err)
      this.emitConnection(false)
    })

    this.connection.onreconnected(() => {
      console.info('SignalR reconnected')
      this.emitConnection(true)
      // Re-subscribe after reconnect (hook will call subscribeAll)
    })

    this.connection.onclose((err) => {
      console.warn('SignalR closed', err)
      this.emitConnection(false)
    })

    await this.connection.start()
    console.info('SignalR connected')
    this.emitConnection(true)
  }

  public async disconnect(): Promise<void> {
    if (!this.connection) return
    try {
      await this.connection.stop()
    } catch (e) {
      console.error('Disconnect error:', e)
    } finally {
      this.emitConnection(false)
    }
  }

  public async subscribeAll(): Promise<void> {
    if (!this.connection || this.connection.state !== HubConnectionState.Connected) return
    try {
      await this.connection.invoke('SubscribeAll')
      console.info('Subscribed to all data')
    } catch (e) {
      console.error('SubscribeAll error:', e)
    }
  }

  public async subscribeDevice(deviceId: string): Promise<void> {
    if (!deviceId) return
    if (!this.connection || this.connection.state !== HubConnectionState.Connected) return
    try {
      await this.connection.invoke('SubscribeDevice', deviceId)
      console.info('Subscribed to device:', deviceId)
    } catch (e) {
      console.error('SubscribeDevice error:', e)
    }
  }

  public async unsubscribeDevice(deviceId: string): Promise<void> {
    if (!deviceId) return
    if (!this.connection || this.connection.state !== HubConnectionState.Connected) return
    try {
      await this.connection.invoke('UnsubscribeDevice', deviceId)
      console.info('Unsubscribed from device:', deviceId)
    } catch (e) {
      console.error('UnsubscribeDevice error:', e)
    }
  }

  public async unsubscribeAll(): Promise<void> {
    if (!this.connection || this.connection.state !== HubConnectionState.Connected) return
    try {
      await this.connection.invoke('UnsubscribeAll')
      console.info('Unsubscribed from all data')
    } catch (e) {
      console.error('UnsubscribeAll error:', e)
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
        console.error('Connection callback error:', e)
      }
    }
  }
}

// Singleton instance
export const telemetrySignalR = new TelemetrySignalR()
