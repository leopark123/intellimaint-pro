import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  Button,
  Card,
  DatePicker,
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
  Col
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { DownloadOutlined } from '@ant-design/icons'
import dayjs from 'dayjs'
import { getDevices } from '../../api/device'
import { ackAlarm, closeAlarm, createAlarm, getAlarmStats, queryAlarms } from '../../api/alarm'
import { exportAlarmsCsv, exportAlarmsXlsx } from '../../api/export'
import type { Device } from '../../types/device'
import type { Alarm, AlarmQuery } from '../../types/alarm'
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
      const res = await getAlarmStats(deviceId)
      if (res.success && res.data) {
        setOpenCount(res.data.openCount ?? 0)
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

      await loadStats(q.deviceId)
    } catch (err) {
      console.error(err)
      message.error('查询告警失败')
    } finally {
      setLoading(false)
    }
  }, [loadStats])

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

  const handleExportCsv = useCallback(() => {
    const params = {
      deviceId: filters.deviceId,
      status: filters.status,
      minSeverity: filters.minSeverity,
      startTs: filters.startTs,
      endTs: filters.endTs,
      limit: 10000
    }
    exportAlarmsCsv(params)
    message.success('正在导出 CSV...')
  }, [filters])

  const handleExportXlsx = useCallback(() => {
    const params = {
      deviceId: filters.deviceId,
      status: filters.status,
      minSeverity: filters.minSeverity,
      startTs: filters.startTs,
      endTs: filters.endTs,
      limit: 10000
    }
    exportAlarmsXlsx(params)
    message.success('正在导出 Excel...')
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
      width: 180,
      fixed: 'right',
      render: (_, record) => {
        const isClosed = record.status === 2
        const isAcked = record.status === 1

        return (
          <Space>
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
  ], [doClose, openAckModal])

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
          <Button onClick={() => void loadFirstPage()}>刷新</Button>
        </Space>
      </div>

      {/* 筛选卡片 */}
      <Card>
        <Row gutter={16}>
          <Col span={6}>
            <Statistic title="未处理告警数量" value={openCount} valueStyle={{ color: 'var(--color-danger)' }} />
          </Col>
          <Col span={18}>
            <Form layout="inline" onFinish={onSearch} initialValues={{ limit: 100 }}>
              <Form.Item label="设备" name="deviceId">
                <Select
                  allowClear
                  style={{ width: 180 }}
                  options={devices.map(d => ({ label: d.name ? `${d.deviceId} - ${d.name}` : d.deviceId, value: d.deviceId }))}
                />
              </Form.Item>

              <Form.Item label="状态" name="status">
                <Select
                  allowClear
                  style={{ width: 120 }}
                  options={StatusOptions.map(o => ({ label: o.label, value: o.value }))}
                />
              </Form.Item>

              <Form.Item label="最小级别" name="minSeverity">
                <Select
                  allowClear
                  style={{ width: 120 }}
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
          </Col>
        </Row>
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
    </Space>
  )
}
