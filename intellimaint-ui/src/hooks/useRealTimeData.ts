import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { telemetrySignalR, TelemetryDataPoint } from '../api/signalr'
import { getLatestTelemetry } from '../api/telemetry'
import { logError } from '../utils/logger'

export type ConnectionMode = 'realtime' | 'polling' | 'disconnected'

type LatestMap = Map<string, TelemetryDataPoint>  // key = deviceId|tagId
type HistoryMap = Map<string, TelemetryDataPoint[]>  // key = deviceId|tagId

function makeKey(deviceId: string, tagId: string) {
  return `${deviceId}|${tagId}`
}

export function useRealTimeData() {
  const [mode, setMode] = useState<ConnectionMode>('disconnected')
  const [error, setError] = useState<string | null>(null)

  const latestRef = useRef<LatestMap>(new Map())
  const historyRef = useRef<HistoryMap>(new Map())

  // Force UI refresh when data changes
  const [tick, setTick] = useState(0)

  const pollingTimerRef = useRef<number | null>(null)
  const disposedRef = useRef(false)

  const maxHistoryPerKey = 300

  // Apply incoming data points to state
  const applyPoints = useCallback((points: TelemetryDataPoint[]) => {
    const latest = latestRef.current
    const history = historyRef.current

    let changed = false

    for (const p of points) {
      const key = makeKey(p.deviceId, p.tagId)
      const existing = latest.get(key)

      // Only accept newer points
      if (existing && existing.ts >= p.ts) continue

      latest.set(key, p)

      const arr = history.get(key) ?? []
      arr.push(p)
      if (arr.length > maxHistoryPerKey) {
        arr.splice(0, arr.length - maxHistoryPerKey)
      }
      history.set(key, arr)

      changed = true
    }

    if (changed) setTick((x) => x + 1)
  }, [])

  // Stop polling
  const stopPolling = useCallback(() => {
    if (pollingTimerRef.current) {
      window.clearInterval(pollingTimerRef.current)
      pollingTimerRef.current = null
    }
  }, [])

  // Start polling as fallback
  const startPolling = useCallback(() => {
    stopPolling()
    setMode('polling')
    setError('WebSocket 连接失败，已切换到轮询模式')

    pollingTimerRef.current = window.setInterval(async () => {
      try {
        const resp = await getLatestTelemetry()
        if (resp.success && resp.data) {
          // Convert API response to TelemetryDataPoint format
          const points: TelemetryDataPoint[] = resp.data.map(d => ({
            deviceId: d.deviceId,
            tagId: d.tagId,
            ts: d.ts,
            value: d.value,
            valueType: d.valueType,
            quality: d.quality,
            unit: d.unit
          }))
          applyPoints(points)
        }
      } catch (e: unknown) {
        logError('Polling failed', e, 'useRealTimeData')
        // Continue polling even on failure
      }
    }, 1000)
  }, [applyPoints, stopPolling])

  // Connect to SignalR
  const connectSignalR = useCallback(async () => {
    try {
      await telemetrySignalR.connect()
      await telemetrySignalR.subscribeAll()
      stopPolling()
      setMode('realtime')
      setError(null)
    } catch (e: unknown) {
      logError('SignalR connect failed', e, 'useRealTimeData')
      setError((e as Error)?.message ?? 'SignalR connect failed')
      startPolling()
    }
  }, [startPolling, stopPolling])

  // Setup effect
  useEffect(() => {
    disposedRef.current = false

    // Handle incoming data
    const offData = telemetrySignalR.onData((points) => {
      applyPoints(points)
    })

    // Handle connection state changes
    const offConn = telemetrySignalR.onConnectionChange(async (connected) => {
      if (disposedRef.current) return

      if (connected) {
        try {
          await telemetrySignalR.subscribeAll()
        } catch {
          // ignore
        }
        stopPolling()
        setMode('realtime')
        setError(null)
      } else {
        // Fall back to polling when disconnected
        startPolling()
      }
    })

    // Initial connection
    connectSignalR()

    return () => {
      disposedRef.current = true
      offData()
      offConn()
      stopPolling()
      telemetrySignalR.disconnect().catch(() => void 0)
    }
  }, [applyPoints, connectSignalR, startPolling, stopPolling])

  // Export latest data as array for Table rendering
  const latestData = useMemo(() => {
    // tick dependency ensures this updates when data changes
    void tick
    return Array.from(latestRef.current.values()).sort((a, b) => b.ts - a.ts)
  }, [tick])

  const connected = mode === 'realtime'

  // Get history for a specific tag
  const getHistory = useCallback((deviceId: string, tagId: string) => {
    const key = makeKey(deviceId, tagId)
    return historyRef.current.get(key) ?? []
  }, [])

  return {
    latestData,
    mode,
    connected,
    error,
    getHistory
  }
}
