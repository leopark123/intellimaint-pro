import { useCallback, useEffect, useMemo, useState } from 'react'
import { Card, Col, Row, Space, Statistic, Table, Tag, Typography, message } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { getCurrentHealth, getHealthHistory, getSystemStats } from '../../api/health'
import type { CollectorHealth, HealthSnapshot, SystemStats } from '../../types/health'
import {
  CollectorStateOptions,
  DatabaseStateOptions,
  HealthStateOptions,
  QueueStateOptions
} from '../../types/health'

const { Title, Text } = Typography

function findOption(
  opts: readonly { label: string; value: number; color: string }[],
  value: number
) {
  return opts.find(o => o.value === value)
}

function formatBytes(bytes: number) {
  if (!Number.isFinite(bytes) || bytes < 0) return '-'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  let v = bytes
  let i = 0
  while (v >= 1024 && i < units.length - 1) {
    v /= 1024
    i++
  }
  return `${v.toFixed(i === 0 ? 0 : 2)} ${units[i]}`
}

function formatLocalTime(iso: string) {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  return d.toLocaleString()
}

export default function SystemHealth() {
  const [loading, setLoading] = useState(false)
  const [snapshot, setSnapshot] = useState<HealthSnapshot | null>(null)
  const [stats, setStats] = useState<SystemStats | null>(null)
  const [history, setHistory] = useState<HealthSnapshot[]>([])
  const [error, setError] = useState<string | null>(null)

  const refresh = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const [healthRes, statsRes, histRes] = await Promise.all([
        getCurrentHealth(),
        getSystemStats(),
        getHealthHistory(60)
      ])

      if (!healthRes.success || !healthRes.data) {
        throw new Error(healthRes.error ?? '获取健康状态失败')
      }
      if (!statsRes.success || !statsRes.data) {
        throw new Error(statsRes.error ?? '获取系统统计失败')
      }

      setSnapshot(healthRes.data)
      setStats(statsRes.data)
      setHistory(histRes.success && histRes.data ? histRes.data : [])
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e)
      setError(msg)
      message.error(msg)
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    refresh()
    const t = window.setInterval(() => refresh(), 10_000)
    return () => window.clearInterval(t)
  }, [refresh])

  const overallTag = useMemo(() => {
    if (!snapshot) return null
    const opt = findOption(HealthStateOptions, snapshot.overallState)
    if (!opt) return <Tag>未知</Tag>
    return <Tag color={opt.color}>{opt.label}</Tag>
  }, [snapshot])

  const dbTag = useMemo(() => {
    if (!snapshot) return null
    const opt = findOption(DatabaseStateOptions, snapshot.databaseState)
    if (!opt) return <Tag>未知</Tag>
    return <Tag color={opt.color}>{opt.label}</Tag>
  }, [snapshot])

  const queueTag = useMemo(() => {
    if (!snapshot) return null
    const opt = findOption(QueueStateOptions, snapshot.queueState)
    if (!opt) return <Tag>未知</Tag>
    return <Tag color={opt.color}>{opt.label}</Tag>
  }, [snapshot])

  const collectorRows = useMemo(() => {
    if (!snapshot) return []
    const entries = Object.entries(snapshot.collectors ?? {})
    return entries.map(([key, c]) => ({
      key,
      protocol: c.protocol ?? key,
      state: c.state,
      lastSuccessTime: c.lastSuccessTime,
      consecutiveErrors: c.consecutiveErrors,
      avgLatencyMs: c.avgLatencyMs,
      p95LatencyMs: c.p95LatencyMs,
      activeConnections: c.activeConnections,
      totalTagCount: c.totalTagCount,
      healthyTagCount: c.healthyTagCount,
      lastError: c.lastError
    }))
  }, [snapshot])

  const columns: ColumnsType<{
    key: string
    protocol: string
    state: number
    lastSuccessTime: string
    consecutiveErrors: number
    avgLatencyMs: number
    p95LatencyMs: number
    activeConnections: number
    totalTagCount: number
    healthyTagCount: number
    lastError?: string | null
  }> = [
    {
      title: '协议',
      dataIndex: 'protocol',
      key: 'protocol'
    },
    {
      title: '状态',
      dataIndex: 'state',
      key: 'state',
      render: (v: number) => {
        const opt = findOption(CollectorStateOptions, v)
        if (!opt) return <Tag>未知</Tag>
        return <Tag color={opt.color}>{opt.label}</Tag>
      }
    },
    {
      title: '最后成功时间',
      dataIndex: 'lastSuccessTime',
      key: 'lastSuccessTime',
      render: (v: string) => <Text>{formatLocalTime(v)}</Text>
    },
    {
      title: '连续错误',
      dataIndex: 'consecutiveErrors',
      key: 'consecutiveErrors'
    },
    {
      title: '平均延迟(ms)',
      dataIndex: 'avgLatencyMs',
      key: 'avgLatencyMs',
      render: (v: number) => (Number.isFinite(v) ? v.toFixed(2) : '-')
    },
    {
      title: 'P95延迟(ms)',
      dataIndex: 'p95LatencyMs',
      key: 'p95LatencyMs',
      render: (v: number) => (Number.isFinite(v) ? v.toFixed(2) : '-')
    },
    {
      title: '活跃连接数',
      dataIndex: 'activeConnections',
      key: 'activeConnections'
    },
    {
      title: '标签(健康/总)',
      key: 'tags',
      render: (_, row) => `${row.healthyTagCount}/${row.totalTagCount}`
    },
    {
      title: '最后错误',
      dataIndex: 'lastError',
      key: 'lastError',
      ellipsis: true,
      render: (v?: string | null) => (v ? <Text type="secondary">{v}</Text> : '-')
    }
  ]

  return (
    <Space direction="vertical" size="middle" style={{ width: '100%' }}>
      {/* 页面标题 */}
      <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between' }}>
        <div>
          <h1 style={{ fontSize: 24, fontWeight: 700, color: 'var(--color-text-primary)', margin: '0 0 8px 0' }}>系统健康</h1>
          <p style={{ fontSize: 14, color: 'var(--color-text-muted)', margin: 0 }}>监控系统运行状态和服务健康度</p>
        </div>
        <Space>
          <span style={{ color: 'var(--color-text-muted)' }}>整体状态：</span>
          {overallTag}
          <span style={{ color: 'var(--color-text-dim)', fontSize: 13 }}>
            最后更新：{snapshot ? formatLocalTime(snapshot.utcTime) : '-'}
          </span>
        </Space>
      </div>

      {error && (
        <Card>
          <span style={{ color: 'var(--color-danger)' }}>{error}</span>
        </Card>
      )}

      <Row gutter={[16, 16]}>
        <Col xs={24} md={12} xl={8}>
          <Card title="系统统计" loading={loading}>
            <Row gutter={[16, 16]}>
              <Col span={12}>
                <Statistic
                  title="设备（启用/总数）"
                  value={
                    stats ? `${stats.enabledDevices}/${stats.totalDevices}` : '-'
                  }
                />
              </Col>
              <Col span={12}>
                <Statistic
                  title="标签（启用/总数）"
                  value={stats ? `${stats.enabledTags}/${stats.totalTags}` : '-'}
                />
              </Col>
              <Col span={12}>
                <Statistic
                  title="告警（未处理/总数）"
                  value={stats ? `${stats.openAlarms}/${stats.totalAlarms}` : '-'}
                />
              </Col>
              <Col span={12}>
                <Statistic
                  title="遥测点（24h/总数）"
                  value={
                    stats
                      ? `${stats.last24HoursTelemetryPoints}/${stats.totalTelemetryPoints}`
                      : '-'
                  }
                />
              </Col>
              <Col span={24}>
                <Statistic
                  title="数据库大小"
                  value={stats ? formatBytes(stats.databaseSizeBytes) : '-'}
                />
              </Col>
            </Row>
          </Card>
        </Col>

        <Col xs={24} md={12} xl={8}>
          <Card title="组件状态" loading={loading}>
            <Row gutter={[16, 16]}>
              <Col span={12}>
                <Space>
                  <Text>数据库：</Text>
                  {dbTag}
                </Space>
              </Col>
              <Col span={12}>
                <Space>
                  <Text>队列：</Text>
                  {queueTag}
                </Space>
              </Col>
              <Col span={12}>
                <Statistic title="队列深度" value={snapshot ? snapshot.queueDepth : 0} />
              </Col>
              <Col span={12}>
                <Statistic title="丢弃点数" value={snapshot ? snapshot.droppedPoints : 0} />
              </Col>
              <Col span={12}>
                <Statistic
                  title="写入延迟 P95(ms)"
                  value={snapshot ? snapshot.writeLatencyMsP95 : 0}
                  precision={2}
                />
              </Col>
              <Col span={12}>
                <Statistic
                  title="内存使用(MB)"
                  value={snapshot ? snapshot.memoryUsedMb : 0}
                />
              </Col>
              <Col span={12}>
                <Statistic
                  title="MQTT 连接"
                  value={snapshot ? (snapshot.mqttConnected ? '已连接' : '未连接') : '-'}
                />
              </Col>
              <Col span={12}>
                <Statistic
                  title="Outbox 深度"
                  value={snapshot ? snapshot.outboxDepth : 0}
                />
              </Col>
            </Row>
          </Card>
        </Col>

        <Col xs={24} md={24} xl={8}>
          <Card title="最近快照（Top 5）" loading={loading}>
            {(history ?? []).slice(0, 5).map((h, idx) => {
              const opt = findOption(HealthStateOptions, h.overallState)
              return (
                <div key={idx} style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 8 }}>
                  <Text>{formatLocalTime(h.utcTime)}</Text>
                  <Tag color={opt?.color}>{opt?.label ?? '未知'}</Tag>
                </div>
              )
            })}
            {(history ?? []).length === 0 ? <Text type="secondary">暂无历史快照</Text> : null}
          </Card>
        </Col>
      </Row>

      <Card title="采集器状态" loading={loading}>
        <Table
          rowKey="key"
          columns={columns}
          dataSource={collectorRows}
          pagination={false}
          size="middle"
        />
      </Card>
    </Space>
  )
}
