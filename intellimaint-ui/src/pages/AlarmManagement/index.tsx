import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  Button,
  Card,
  DatePicker,
  Descriptions,
  Dropdown,
  Form,
  Input,
  Modal,
  Popconfirm,
  Select,
  Space,
  Table,
  Tag,
  message,
  Statistic,
  Row,
  Col,
  Progress,
  Tooltip
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import {
  DownloadOutlined,
  AlertOutlined,
  CheckCircleOutlined,
  ClockCircleOutlined,
  WarningOutlined,
  FireOutlined,
  SyncOutlined
} from '@ant-design/icons'
import dayjs from 'dayjs'
import { AreaChart, Area, XAxis, YAxis, Tooltip as RechartsTooltip, ResponsiveContainer, CartesianGrid } from 'recharts'
import { getDevices } from '../../api/device'
import { ackAlarm, closeAlarm, createAlarm, getAlarmStats, getAlarmGroupStats, getAlarmTrend, queryAlarms } from '../../api/alarm'
import { exportAlarmsCsv, exportAlarmsXlsx } from '../../api/export'
import type { Device } from '../../types/device'
import type { Alarm, AlarmQuery, AlarmTrendPoint, AlarmGroupStats } from '../../types/alarm'
import { SeverityOptions, StatusOptions } from '../../types/alarm'

const { RangePicker } = DatePicker

function severityTag(severity: number) {
  const opt = SeverityOptions.find(o => o.value === severity)
  if (!opt) return <Tag>{severity}</Tag>
  return <Tag color={opt.color as any}>{opt.label}</Tag>
}

function statusTag(status: number) {
  const opt = StatusOptions.find(o => o.value === status)
  if (!opt) return <Tag>{status}</Tag>
  return <Tag color={opt.color as any}>{opt.label}</Tag>
}

export default function AlarmManagement() {
  const [devices, setDevices] = useState<Device[]>([])
  const [loading, setLoading] = useState(false)
  const [alarms, setAlarms] = useState<Alarm[]>([])
  const [hasMore, setHasMore] = useState(false)
  const [nextToken, setNextToken] = useState<string | undefined>(undefined)
  const [totalCount, setTotalCount] = useState(0)
  const [openCount, setOpenCount] = useState(0)

  // v62: 增强的统计和趋势数据
  const [groupStats, setGroupStats] = useState<AlarmGroupStats>({ openCount: 0, acknowledgedCount: 0, closedCount: 0 })
  const [trendData, setTrendData] = useState<AlarmTrendPoint[]>([])
  const [refreshing, setRefreshing] = useState(false)

  const [filters, setFilters] = useState<AlarmQuery>({
    limit: 100
  })

  const filtersRef = useRef(filters)
  useEffect(() => {
    filtersRef.current = filters
  }, [filters])

  const [ackModalOpen, setAckModalOpen] = useState(false)
  const [ackTarget, setAckTarget] = useState<Alarm | null>(null)
  const [ackSubmitting, setAckSubmitting] = useState(false)
  const [ackForm] = Form.useForm()

  const [createModalOpen, setCreateModalOpen] = useState(false)
  const [createSubmitting, setCreateSubmitting] = useState(false)
  const [createForm] = Form.useForm()

  // 详情模态框
  const [detailModalOpen, setDetailModalOpen] = useState(false)
  const [detailAlarm, setDetailAlarm] = useState<Alarm | null>(null)

  const loadDevices = useCallback(async () => {
    try {
      const deviceList = await getDevices()
      setDevices(deviceList)
    } catch (err) {
      console.error(err)
      message.error('加载设备列表失败')
      setDevices([])
    }
  }, [])

  const loadStats = useCallback(async (deviceId?: string) => {
    try {
      const [statsRes, groupStatsRes] = await Promise.all([
        getAlarmStats(deviceId),
        getAlarmGroupStats()
      ])
      if (statsRes.success && statsRes.data) {
        setOpenCount(statsRes.data.openCount ?? 0)
      }
      if (groupStatsRes.success && groupStatsRes.data) {
        setGroupStats(groupStatsRes.data)
      }
    } catch (err) {
      console.error(err)
    }
  }, [])

  const loadTrend = useCallback(async (deviceId?: string) => {
    try {
      const res = await getAlarmTrend(7, deviceId)
      if (res.success && res.data) {
        // 转换数据格式用于图表
        const chartData = res.data.map(p => ({
          ...p,
          time: dayjs(p.bucket).format('MM-DD HH:mm')
        }))
        setTrendData(chartData as any)
      }
    } catch (err) {
      console.error(err)
    }
  }, [])

  const loadFirstPage = useCallback(async () => {
    setLoading(true)
    try {
      const q: AlarmQuery = { ...filtersRef.current, after: undefined }
      const res = await queryAlarms(q)
      if (!res.success || !res.data) {
        message.error(res.error || '查询告警失败')
        setAlarms([])
        setHasMore(false)
        setNextToken(undefined)
        setTotalCount(0)
        return
      }

      setAlarms(res.data.items || [])
      setHasMore(res.data.hasMore)
      setNextToken(res.data.nextToken || undefined)
      setTotalCount(res.data.totalCount || 0)

      await Promise.all([
        loadStats(q.deviceId),
        loadTrend(q.deviceId)
      ])
    } catch (err) {
      console.error(err)
      message.error('查询告警失败')
    } finally {
      setLoading(false)
    }
  }, [loadStats, loadTrend])

  const loadNextPage = useCallback(async () => {
    if (!hasMore || !nextToken) return

    setLoading(true)
    try {
      const q: AlarmQuery = { ...filtersRef.current, after: nextToken }
      const res = await queryAlarms(q)
      if (!res.success || !res.data) {
        message.error(res.error || '加载下一页失败')
        return
      }

      const data = res.data
      setAlarms(prev => [...prev, ...(data.items || [])])
      setHasMore(data.hasMore ?? false)
      setNextToken(data.nextToken || undefined)
      setTotalCount(data.totalCount || 0)
    } catch (err) {
      console.error(err)
      message.error('加载下一页失败')
    } finally {
      setLoading(false)
    }
  }, [hasMore, nextToken])

  useEffect(() => {
    void loadDevices()
    void loadFirstPage()
  }, [loadDevices, loadFirstPage])

  const handleExportCsv = useCallback(async () => {
    const params = {
      deviceId: filters.deviceId,
      status: filters.status,
      minSeverity: filters.minSeverity,
      startTs: filters.startTs,
      endTs: filters.endTs,
      limit: 10000
    }
    try {
      message.loading({ content: '正在导出 CSV...', key: 'export' })
      await exportAlarmsCsv(params)
      message.success({ content: 'CSV 导出成功', key: 'export' })
    } catch (err) {
      console.error(err)
      message.error({ content: '导出失败', key: 'export' })
    }
  }, [filters])

  const handleExportXlsx = useCallback(async () => {
    const params = {
      deviceId: filters.deviceId,
      status: filters.status,
      minSeverity: filters.minSeverity,
      startTs: filters.startTs,
      endTs: filters.endTs,
      limit: 10000
    }
    try {
      message.loading({ content: '正在导出 Excel...', key: 'export' })
      await exportAlarmsXlsx(params)
      message.success({ content: 'Excel 导出成功', key: 'export' })
    } catch (err) {
      console.error(err)
      message.error({ content: '导出失败', key: 'export' })
    }
  }, [filters])

  const onSearch = useCallback(async (values: any) => {
    const deviceId = values.deviceId || undefined
    const status = typeof values.status === 'number' ? values.status : undefined
    const minSeverity = typeof values.minSeverity === 'number' ? values.minSeverity : undefined

    let startTs: number | undefined
    let endTs: number | undefined
    if (values.timeRange && values.timeRange.length === 2) {
      startTs = values.timeRange[0].valueOf()
      endTs = values.timeRange[1].valueOf()
    }

    const limit = typeof values.limit === 'number' ? values.limit : 100

    setFilters({
      deviceId,
      status,
      minSeverity,
      startTs,
      endTs,
      limit
    })

    // 立即按新条件加载第一页
    setTimeout(() => {
      void loadFirstPage()
    }, 0)
  }, [loadFirstPage])

  const openAckModal = useCallback((alarm: Alarm) => {
    setAckTarget(alarm)
    ackForm.setFieldsValue({
      ackedBy: '',
      ackNote: ''
    })
    setAckModalOpen(true)
  }, [ackForm])

  const submitAck = useCallback(async () => {
    try {
      const values = await ackForm.validateFields()
      if (!ackTarget) return

      setAckSubmitting(true)
      const res = await ackAlarm(ackTarget.alarmId, {
        ackedBy: values.ackedBy,
        ackNote: values.ackNote
      })

      if (!res.success || !res.data) {
        message.error(res.error || '确认告警失败')
        return
      }

      message.success('告警已确认')
      setAckModalOpen(false)
      setAckTarget(null)
      await loadFirstPage()
    } catch (err) {
      // 表单校验失败会抛错
      if (err) console.error(err)
    } finally {
      setAckSubmitting(false)
    }
  }, [ackForm, ackTarget, loadFirstPage])

  const doClose = useCallback(async (alarm: Alarm) => {
    try {
      const res = await closeAlarm(alarm.alarmId)
      if (!res.success || !res.data) {
        message.error(res.error || '关闭告警失败')
        return
      }
      message.success('告警已关闭')
      await loadFirstPage()
    } catch (err) {
      console.error(err)
      message.error('关闭告警失败')
    }
  }, [loadFirstPage])

  const openCreateModal = useCallback(() => {
    createForm.setFieldsValue({
      deviceId: devices.length > 0 ? devices[0].deviceId : undefined,
      tagId: '',
      severity: 3,
      code: 'TEST',
      message: '测试告警'
    })
    setCreateModalOpen(true)
  }, [createForm, devices])

  const submitCreate = useCallback(async () => {
    try {
      const values = await createForm.validateFields()
      setCreateSubmitting(true)

      const res = await createAlarm({
        deviceId: values.deviceId,
        tagId: values.tagId || undefined,
        severity: values.severity,
        code: values.code,
        message: values.message
      })

      if (!res.success || !res.data) {
        message.error(res.error || '创建告警失败')
        return
      }

      message.success('告警已创建')
      setCreateModalOpen(false)
      await loadFirstPage()
    } catch (err) {
      if (err) console.error(err)
    } finally {
      setCreateSubmitting(false)
    }
  }, [createForm, loadFirstPage])

  // 查看详情
  const openDetailModal = useCallback((alarm: Alarm) => {
    setDetailAlarm(alarm)
    setDetailModalOpen(true)
  }, [])

  const columns: ColumnsType<Alarm> = useMemo(() => [
    {
      title: '时间',
      dataIndex: 'ts',
      key: 'ts',
      width: 180,
      render: (v: number) => dayjs(v).format('YYYY-MM-DD HH:mm:ss')
    },
    {
      title: '设备',
      dataIndex: 'deviceId',
      key: 'deviceId',
      width: 140
    },
    {
      title: '标签',
      dataIndex: 'tagId',
      key: 'tagId',
      width: 160,
      render: (v: string | null | undefined) => v ?? '-'
    },
    {
      title: '级别',
      dataIndex: 'severity',
      key: 'severity',
      width: 100,
      render: (v: number) => severityTag(v)
    },
    {
      title: '代码',
      dataIndex: 'code',
      key: 'code',
      width: 120
    },
    {
      title: '消息',
      dataIndex: 'message',
      key: 'message',
      ellipsis: true
    },
    {
      title: '状态',
      dataIndex: 'status',
      key: 'status',
      width: 100,
      render: (v: number) => statusTag(v)
    },
    {
      title: '确认人',
      dataIndex: 'ackedBy',
      key: 'ackedBy',
      width: 120,
      render: (v: string | null | undefined) => v ?? '-'
    },
    {
      title: '操作',
      key: 'actions',
      width: 220,
      fixed: 'right',
      render: (_, record) => {
        const isClosed = record.status === 2
        const isAcked = record.status === 1

        return (
          <Space>
            <Button
              size="small"
              type="link"
              onClick={() => openDetailModal(record)}
            >
              详情
            </Button>

            <Button
              size="small"
              onClick={() => openAckModal(record)}
              disabled={isClosed || isAcked}
            >
              确认
            </Button>

            <Popconfirm
              title="确定关闭该告警？"
              okText="关闭"
              cancelText="取消"
              onConfirm={() => doClose(record)}
              disabled={isClosed}
            >
              <Button size="small" danger disabled={isClosed}>
                关闭
              </Button>
            </Popconfirm>
          </Space>
        )
      }
    }
  ], [doClose, openAckModal, openDetailModal])

  // 计算告警统计百分比
  const totalGroupCount = groupStats.openCount + groupStats.acknowledgedCount + groupStats.closedCount
  const openPercent = totalGroupCount > 0 ? Math.round((groupStats.openCount / totalGroupCount) * 100) : 0
  const ackedPercent = totalGroupCount > 0 ? Math.round((groupStats.acknowledgedCount / totalGroupCount) * 100) : 0

  const handleRefresh = async () => {
    setRefreshing(true)
    await loadFirstPage()
    setRefreshing(false)
    message.success('数据已刷新')
  }

  return (
    <Space direction="vertical" size="middle" style={{ width: '100%' }}>
      {/* 页面标题 */}
      <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between' }}>
        <div>
          <h1 style={{ fontSize: 24, fontWeight: 700, color: 'var(--color-text-primary)', margin: '0 0 8px 0' }}>告警管理</h1>
          <p style={{ fontSize: 14, color: 'var(--color-text-muted)', margin: 0 }}>查看、确认和关闭系统告警</p>
        </div>
        <Space>
          <Button onClick={openCreateModal}>创建测试告警</Button>
          <Button
            icon={<SyncOutlined spin={refreshing} />}
            onClick={handleRefresh}
            loading={refreshing}
          >
            刷新
          </Button>
        </Space>
      </div>

      {/* v62: 增强的统计卡片 */}
      <Row gutter={16}>
        <Col span={6}>
          <Card>
            <Statistic
              title={<span><AlertOutlined style={{ marginRight: 8, color: '#ff4d4f' }} />未处理告警</span>}
              value={groupStats.openCount}
              valueStyle={{ color: '#ff4d4f', fontSize: 32 }}
              suffix={<span style={{ fontSize: 14, color: '#999' }}>/ {totalGroupCount}</span>}
            />
            <Progress
              percent={openPercent}
              strokeColor="#ff4d4f"
              showInfo={false}
              size="small"
              style={{ marginTop: 8 }}
            />
          </Card>
        </Col>
        <Col span={6}>
          <Card>
            <Statistic
              title={<span><ClockCircleOutlined style={{ marginRight: 8, color: '#faad14' }} />已确认</span>}
              value={groupStats.acknowledgedCount}
              valueStyle={{ color: '#faad14', fontSize: 32 }}
              suffix={<span style={{ fontSize: 14, color: '#999' }}>/ {totalGroupCount}</span>}
            />
            <Progress
              percent={ackedPercent}
              strokeColor="#faad14"
              showInfo={false}
              size="small"
              style={{ marginTop: 8 }}
            />
          </Card>
        </Col>
        <Col span={6}>
          <Card>
            <Statistic
              title={<span><CheckCircleOutlined style={{ marginRight: 8, color: '#52c41a' }} />已关闭</span>}
              value={groupStats.closedCount}
              valueStyle={{ color: '#52c41a', fontSize: 32 }}
            />
          </Card>
        </Col>
        <Col span={6}>
          <Card>
            <Statistic
              title="告警总数"
              value={totalCount}
              valueStyle={{ fontSize: 32 }}
              prefix={<FireOutlined style={{ color: '#666' }} />}
            />
          </Card>
        </Col>
      </Row>

      {/* v62: 告警趋势图 */}
      {trendData.length > 0 && (
        <Card title="7天告警趋势" size="small">
          <div style={{ width: '100%', height: 200 }}>
            <ResponsiveContainer>
              <AreaChart data={trendData} margin={{ top: 10, right: 30, left: 0, bottom: 0 }}>
                <CartesianGrid strokeDasharray="3 3" stroke="#f0f0f0" />
                <XAxis dataKey="time" tick={{ fontSize: 12 }} />
                <YAxis tick={{ fontSize: 12 }} />
                <RechartsTooltip
                  formatter={(value: any, name: any) => {
                    const nameMap: Record<string, string> = {
                      totalCount: '总数',
                      openCount: '未处理',
                      criticalCount: '严重',
                      warningCount: '警告'
                    }
                    return [value, nameMap[name] || name]
                  }}
                />
                <Area
                  type="monotone"
                  dataKey="totalCount"
                  stackId="1"
                  stroke="#1890ff"
                  fill="#1890ff"
                  fillOpacity={0.3}
                  name="totalCount"
                />
                <Area
                  type="monotone"
                  dataKey="criticalCount"
                  stackId="2"
                  stroke="#ff4d4f"
                  fill="#ff4d4f"
                  fillOpacity={0.6}
                  name="criticalCount"
                />
                <Area
                  type="monotone"
                  dataKey="warningCount"
                  stackId="2"
                  stroke="#faad14"
                  fill="#faad14"
                  fillOpacity={0.6}
                  name="warningCount"
                />
              </AreaChart>
            </ResponsiveContainer>
          </div>
        </Card>
      )}

      {/* 筛选卡片 */}
      <Card title="查询条件" size="small">
        <Form layout="inline" onFinish={onSearch} initialValues={{ limit: 100 }}>
          <Form.Item label="设备" name="deviceId">
            <Select
              allowClear
              style={{ width: 180 }}
              placeholder="全部设备"
              options={devices.map(d => ({ label: d.name ? `${d.deviceId} - ${d.name}` : d.deviceId, value: d.deviceId }))}
            />
          </Form.Item>

          <Form.Item label="状态" name="status">
            <Select
              allowClear
              style={{ width: 120 }}
              placeholder="全部状态"
              options={StatusOptions.map(o => ({ label: o.label, value: o.value }))}
            />
          </Form.Item>

          <Form.Item label="最小级别" name="minSeverity">
            <Select
              allowClear
              style={{ width: 120 }}
              placeholder="全部级别"
              options={SeverityOptions.map(o => ({ label: o.label, value: o.value }))}
            />
          </Form.Item>

          <Form.Item label="时间范围" name="timeRange">
            <RangePicker showTime />
          </Form.Item>

          <Form.Item label="每页" name="limit">
            <Select
              style={{ width: 100 }}
              options={[
                { label: '50', value: 50 },
                { label: '100', value: 100 },
                { label: '200', value: 200 }
              ]}
            />
          </Form.Item>

          <Form.Item>
            <Space>
              <Button type="primary" htmlType="submit">查询</Button>
              <Dropdown
                menu={{
                  items: [
                    { key: 'csv', label: '导出 CSV', onClick: handleExportCsv },
                    { key: 'xlsx', label: '导出 Excel', onClick: handleExportXlsx }
                  ]
                }}
              >
                <Button icon={<DownloadOutlined />}>导出</Button>
              </Dropdown>
            </Space>
          </Form.Item>
        </Form>
      </Card>

      <Card title={`告警列表（已加载 ${alarms.length} / ${totalCount}）`}>
        <Table
          rowKey="alarmId"
          columns={columns}
          dataSource={alarms}
          loading={loading}
          pagination={false}
          size="middle"
          scroll={{ x: 1300 }}
        />

        <div style={{ marginTop: 16, display: 'flex', justifyContent: 'center' }}>
          <Space>
            <Button onClick={() => void loadFirstPage()} disabled={loading}>回到第一页</Button>
            <Button onClick={() => void loadNextPage()} disabled={loading || !hasMore}>
              {hasMore ? '加载下一页' : '没有更多了'}
            </Button>
          </Space>
        </div>
      </Card>

      <Modal
        title="确认告警"
        open={ackModalOpen}
        onCancel={() => setAckModalOpen(false)}
        onOk={() => void submitAck()}
        confirmLoading={ackSubmitting}
        okText="确认"
        cancelText="取消"
      >
        <Form form={ackForm} layout="vertical">
          <Form.Item label="确认人" name="ackedBy" rules={[{ required: true, message: '请输入确认人' }]}>
            <Input placeholder="例如：operator1" />
          </Form.Item>
          <Form.Item label="备注" name="ackNote">
            <Input.TextArea placeholder="可选" rows={3} />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        title="创建测试告警"
        open={createModalOpen}
        onCancel={() => setCreateModalOpen(false)}
        onOk={() => void submitCreate()}
        confirmLoading={createSubmitting}
        okText="创建"
        cancelText="取消"
      >
        <Form form={createForm} layout="vertical">
          <Form.Item label="设备" name="deviceId" rules={[{ required: true, message: '请选择设备' }]}>
            <Select
              options={devices.map(d => ({ label: d.name ? `${d.deviceId} - ${d.name}` : d.deviceId, value: d.deviceId }))}
            />
          </Form.Item>

          <Form.Item label="标签（可选）" name="tagId">
            <Input placeholder="例如：Motor.Speed" />
          </Form.Item>

          <Form.Item label="严重级别" name="severity" rules={[{ required: true, message: '请选择严重级别' }]}>
            <Select options={SeverityOptions.map(o => ({ label: o.label, value: o.value }))} />
          </Form.Item>

          <Form.Item label="代码" name="code" rules={[{ required: true, message: '请输入代码' }]}>
            <Input />
          </Form.Item>

          <Form.Item label="消息" name="message" rules={[{ required: true, message: '请输入消息' }]}>
            <Input.TextArea rows={3} />
          </Form.Item>
        </Form>
      </Modal>

      {/* 详情模态框 */}
      <Modal
        title="告警详情"
        open={detailModalOpen}
        onCancel={() => setDetailModalOpen(false)}
        footer={
          <Space>
            {detailAlarm && detailAlarm.status === 0 && (
              <Button onClick={() => {
                setDetailModalOpen(false)
                openAckModal(detailAlarm)
              }}>
                确认告警
              </Button>
            )}
            {detailAlarm && detailAlarm.status !== 2 && (
              <Popconfirm
                title="确定关闭该告警？"
                okText="关闭"
                cancelText="取消"
                onConfirm={async () => {
                  if (detailAlarm) {
                    await doClose(detailAlarm)
                    setDetailModalOpen(false)
                  }
                }}
              >
                <Button danger>关闭告警</Button>
              </Popconfirm>
            )}
            <Button onClick={() => setDetailModalOpen(false)}>取消</Button>
          </Space>
        }
        width={700}
      >
        {detailAlarm && (
          <Descriptions bordered column={2} size="small">
            <Descriptions.Item label="告警ID" span={2}>
              <code style={{ fontSize: 12 }}>{detailAlarm.alarmId}</code>
            </Descriptions.Item>
            <Descriptions.Item label="设备ID">{detailAlarm.deviceId}</Descriptions.Item>
            <Descriptions.Item label="标签">{detailAlarm.tagId || '-'}</Descriptions.Item>
            <Descriptions.Item label="触发时间">
              {dayjs(detailAlarm.ts).format('YYYY-MM-DD HH:mm:ss')}
            </Descriptions.Item>
            <Descriptions.Item label="创建时间">
              {dayjs(detailAlarm.createdUtc).format('YYYY-MM-DD HH:mm:ss')}
            </Descriptions.Item>
            <Descriptions.Item label="级别">{severityTag(detailAlarm.severity)}</Descriptions.Item>
            <Descriptions.Item label="状态">{statusTag(detailAlarm.status)}</Descriptions.Item>
            <Descriptions.Item label="告警代码">{detailAlarm.code}</Descriptions.Item>
            <Descriptions.Item label="更新时间">
              {dayjs(detailAlarm.updatedUtc).format('YYYY-MM-DD HH:mm:ss')}
            </Descriptions.Item>
            <Descriptions.Item label="消息" span={2}>
              {detailAlarm.message}
            </Descriptions.Item>
            {detailAlarm.ackedBy && (
              <>
                <Descriptions.Item label="确认人">{detailAlarm.ackedBy}</Descriptions.Item>
                <Descriptions.Item label="确认时间">
                  {detailAlarm.ackedUtc ? dayjs(detailAlarm.ackedUtc).format('YYYY-MM-DD HH:mm:ss') : '-'}
                </Descriptions.Item>
                {detailAlarm.ackNote && (
                  <Descriptions.Item label="确认备注" span={2}>
                    {detailAlarm.ackNote}
                  </Descriptions.Item>
                )}
              </>
            )}
          </Descriptions>
        )}
      </Modal>
    </Space>
  )
}
