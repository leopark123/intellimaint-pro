import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  Button,
  Card,
  DatePicker,
  Form,
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
  Input,
  Descriptions,
  Badge
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import dayjs from 'dayjs'
import { getDevices } from '../../api/device'
import {
  queryAlarmGroups,
  getAlarmGroupStats,
  getAlarmGroupDetail,
  ackAlarmGroup,
  closeAlarmGroup
} from '../../api/alarm'
import type { Device } from '../../types/device'
import type { AlarmGroup, AlarmGroupQuery, Alarm } from '../../types/alarm'
import { SeverityOptions, StatusOptions } from '../../types/alarm'
import { logError } from '../../utils/logger'

const { RangePicker } = DatePicker

function severityTag(severity: number) {
  const opt = SeverityOptions.find(o => o.value === severity)
  if (!opt) return <Tag>{severity}</Tag>
  return <Tag color={opt.color as string}>{opt.label}</Tag>
}

function statusTag(status: number) {
  const opt = StatusOptions.find(o => o.value === status)
  if (!opt) return <Tag>{status}</Tag>
  return <Tag color={opt.color as string}>{opt.label}</Tag>
}

export default function AlarmGroups() {
  const [devices, setDevices] = useState<Device[]>([])
  const [loading, setLoading] = useState(false)
  const [groups, setGroups] = useState<AlarmGroup[]>([])
  const [hasMore, setHasMore] = useState(false)
  const [nextToken, setNextToken] = useState<string | undefined>(undefined)
  const [totalCount, setTotalCount] = useState(0)
  const [openCount, setOpenCount] = useState(0)

  const [filters, setFilters] = useState<AlarmGroupQuery>({
    limit: 50
  })

  const filtersRef = useRef(filters)
  useEffect(() => {
    filtersRef.current = filters
  }, [filters])

  // 详情模态框
  const [detailModalOpen, setDetailModalOpen] = useState(false)
  const [detailGroup, setDetailGroup] = useState<AlarmGroup | null>(null)
  const [detailAlarms, setDetailAlarms] = useState<Alarm[]>([])
  const [detailLoading, setDetailLoading] = useState(false)

  // 确认模态框
  const [ackModalOpen, setAckModalOpen] = useState(false)
  const [ackTarget, setAckTarget] = useState<AlarmGroup | null>(null)
  const [ackSubmitting, setAckSubmitting] = useState(false)
  const [ackForm] = Form.useForm()

  const loadDevices = useCallback(async () => {
    try {
      const deviceList = await getDevices()
      setDevices(deviceList)
    } catch (err) {
      logError('加载设备列表失败', err, 'AlarmGroups')
      message.error('加载设备列表失败')
      setDevices([])
    }
  }, [])

  const loadStats = useCallback(async () => {
    try {
      const res = await getAlarmGroupStats()
      if (res.success && res.data) {
        setOpenCount(res.data.openCount ?? 0)
      }
    } catch (err) {
      logError('加载统计失败', err, 'AlarmGroups')
    }
  }, [])

  const loadFirstPage = useCallback(async () => {
    setLoading(true)
    try {
      const q: AlarmGroupQuery = { ...filtersRef.current, after: undefined }
      const res = await queryAlarmGroups(q)
      if (!res.success || !res.data) {
        message.error(res.error || '查询聚合告警失败')
        setGroups([])
        setHasMore(false)
        setNextToken(undefined)
        setTotalCount(0)
        return
      }

      setGroups(res.data.items || [])
      setHasMore(res.data.hasMore)
      setNextToken(res.data.nextToken || undefined)
      setTotalCount(res.data.totalCount || 0)

      await loadStats()
    } catch (err) {
      logError('查询聚合告警失败', err, 'AlarmGroups')
      message.error('查询聚合告警失败')
    } finally {
      setLoading(false)
    }
  }, [loadStats])

  const loadNextPage = useCallback(async () => {
    if (!hasMore || !nextToken) return

    setLoading(true)
    try {
      const q: AlarmGroupQuery = { ...filtersRef.current, after: nextToken }
      const res = await queryAlarmGroups(q)
      if (!res.success || !res.data) {
        message.error(res.error || '加载下一页失败')
        return
      }

      const data = res.data
      setGroups(prev => [...prev, ...(data.items || [])])
      setHasMore(data.hasMore ?? false)
      setNextToken(data.nextToken || undefined)
      setTotalCount(data.totalCount || 0)
    } catch (err) {
      logError('加载下一页失败', err, 'AlarmGroups')
      message.error('加载下一页失败')
    } finally {
      setLoading(false)
    }
  }, [hasMore, nextToken])

  useEffect(() => {
    void loadDevices()
    void loadFirstPage()
  }, [loadDevices, loadFirstPage])

  const onSearch = useCallback(async (values: Record<string, unknown>) => {
    const deviceId = (values.deviceId as string) || undefined
    const status = typeof values.status === 'number' ? values.status : undefined
    const minSeverity = typeof values.minSeverity === 'number' ? values.minSeverity : undefined

    let startTs: number | undefined
    let endTs: number | undefined
    const timeRange = values.timeRange as [dayjs.Dayjs, dayjs.Dayjs] | undefined
    if (timeRange && timeRange.length === 2) {
      startTs = timeRange[0].valueOf()
      endTs = timeRange[1].valueOf()
    }

    const limit = typeof values.limit === 'number' ? values.limit : 50

    setFilters({
      deviceId,
      status,
      minSeverity,
      startTs,
      endTs,
      limit
    })

    setTimeout(() => {
      void loadFirstPage()
    }, 0)
  }, [loadFirstPage])

  // 查看详情
  const openDetailModal = useCallback(async (group: AlarmGroup) => {
    setDetailGroup(group)
    setDetailAlarms([])
    setDetailModalOpen(true)
    setDetailLoading(true)

    try {
      const res = await getAlarmGroupDetail(group.groupId)
      if (res.success && res.data) {
        setDetailAlarms(res.data.children || [])
      } else {
        message.error(res.error || '获取详情失败')
      }
    } catch (err) {
      logError('获取详情失败', err, 'AlarmGroups')
      message.error('获取详情失败')
    } finally {
      setDetailLoading(false)
    }
  }, [])

  // 确认聚合组
  const openAckModal = useCallback((group: AlarmGroup) => {
    setAckTarget(group)
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
      const res = await ackAlarmGroup(ackTarget.groupId, {
        ackedBy: values.ackedBy,
        ackNote: values.ackNote
      })

      if (!res.success || !res.data) {
        message.error(res.error || '确认失败')
        return
      }

      message.success(`已确认聚合组及其 ${ackTarget.alarmCount} 条告警`)
      setAckModalOpen(false)
      setAckTarget(null)
      await loadFirstPage()
    } catch (err) {
      if (err) logError('确认聚合组失败', err, 'AlarmGroups')
    } finally {
      setAckSubmitting(false)
    }
  }, [ackForm, ackTarget, loadFirstPage])

  // 关闭聚合组
  const doClose = useCallback(async (group: AlarmGroup) => {
    try {
      const res = await closeAlarmGroup(group.groupId)
      if (!res.success || !res.data) {
        message.error(res.error || '关闭失败')
        return
      }
      message.success(`已关闭聚合组及其 ${group.alarmCount} 条告警`)
      await loadFirstPage()
    } catch (err) {
      logError('关闭聚合组失败', err, 'AlarmGroups')
      message.error('关闭失败')
    }
  }, [loadFirstPage])

  const columns: ColumnsType<AlarmGroup> = useMemo(() => [
    {
      title: '首次发生',
      dataIndex: 'firstOccurredUtc',
      key: 'firstOccurredUtc',
      width: 160,
      render: (v: number) => dayjs(v).format('YYYY-MM-DD HH:mm:ss')
    },
    {
      title: '最后发生',
      dataIndex: 'lastOccurredUtc',
      key: 'lastOccurredUtc',
      width: 160,
      render: (v: number) => dayjs(v).format('YYYY-MM-DD HH:mm:ss')
    },
    {
      title: '设备',
      dataIndex: 'deviceId',
      key: 'deviceId',
      width: 140
    },
    {
      title: '规则',
      dataIndex: 'ruleId',
      key: 'ruleId',
      width: 160,
      ellipsis: true
    },
    {
      title: '告警数',
      dataIndex: 'alarmCount',
      key: 'alarmCount',
      width: 80,
      render: (v: number) => <Badge count={v} showZero color={v > 10 ? 'red' : v > 5 ? 'orange' : 'blue'} />
    },
    {
      title: '级别',
      dataIndex: 'severity',
      key: 'severity',
      width: 100,
      render: (v: number) => severityTag(v)
    },
    {
      title: '消息',
      dataIndex: 'message',
      key: 'message',
      ellipsis: true
    },
    {
      title: '状态',
      dataIndex: 'aggregateStatus',
      key: 'aggregateStatus',
      width: 100,
      render: (v: number) => statusTag(v)
    },
    {
      title: '操作',
      key: 'actions',
      width: 220,
      fixed: 'right',
      render: (_, record) => {
        const isClosed = record.aggregateStatus === 2
        const isAcked = record.aggregateStatus === 1

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
              title={`确定关闭该聚合组及其 ${record.alarmCount} 条告警？`}
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

  // 详情模态框中的告警列表列
  const alarmColumns: ColumnsType<Alarm> = useMemo(() => [
    {
      title: '时间',
      dataIndex: 'ts',
      key: 'ts',
      width: 160,
      render: (v: number) => dayjs(v).format('YYYY-MM-DD HH:mm:ss')
    },
    {
      title: '标签',
      dataIndex: 'tagId',
      key: 'tagId',
      width: 140,
      render: (v: string | null | undefined) => v ?? '-'
    },
    {
      title: '级别',
      dataIndex: 'severity',
      key: 'severity',
      width: 80,
      render: (v: number) => severityTag(v)
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
      width: 80,
      render: (v: number) => statusTag(v)
    }
  ], [])

  return (
    <Space direction="vertical" size="middle" style={{ width: '100%' }}>
      {/* 页面标题 */}
      <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between' }}>
        <div>
          <h1 style={{ fontSize: 24, fontWeight: 700, color: 'var(--color-text-primary)', margin: '0 0 8px 0' }}>告警聚合</h1>
          <p style={{ fontSize: 14, color: 'var(--color-text-muted)', margin: 0 }}>相同设备和规则触发的告警自动聚合，批量处理更高效</p>
        </div>
        <Button onClick={() => void loadFirstPage()}>刷新</Button>
      </div>

      {/* 筛选卡片 */}
      <Card>
        <Row gutter={16}>
          <Col span={6}>
            <Statistic title="未处理聚合组" value={openCount} valueStyle={{ color: 'var(--color-danger)' }} />
          </Col>
          <Col span={18}>
            <Form layout="inline" onFinish={onSearch} initialValues={{ limit: 50 }}>
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
                  style={{ width: 80 }}
                  options={[
                    { label: '20', value: 20 },
                    { label: '50', value: 50 },
                    { label: '100', value: 100 }
                  ]}
                />
              </Form.Item>

              <Form.Item>
                <Button type="primary" htmlType="submit">查询</Button>
              </Form.Item>
            </Form>
          </Col>
        </Row>
      </Card>

      {/* 列表 */}
      <Card title={`聚合告警（已加载 ${groups.length} / ${totalCount}）`}>
        <Table
          rowKey="groupId"
          columns={columns}
          dataSource={groups}
          loading={loading}
          pagination={false}
          size="middle"
          scroll={{ x: 1400 }}
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

      {/* 详情模态框 */}
      <Modal
        title="聚合组详情"
        open={detailModalOpen}
        onCancel={() => setDetailModalOpen(false)}
        footer={null}
        width={900}
      >
        {detailGroup && (
          <>
            <Descriptions bordered column={2} size="small" style={{ marginBottom: 16 }}>
              <Descriptions.Item label="聚合组ID">{detailGroup.groupId}</Descriptions.Item>
              <Descriptions.Item label="设备">{detailGroup.deviceId}</Descriptions.Item>
              <Descriptions.Item label="规则">{detailGroup.ruleId}</Descriptions.Item>
              <Descriptions.Item label="告警数量">{detailGroup.alarmCount}</Descriptions.Item>
              <Descriptions.Item label="首次发生">{dayjs(detailGroup.firstOccurredUtc).format('YYYY-MM-DD HH:mm:ss')}</Descriptions.Item>
              <Descriptions.Item label="最后发生">{dayjs(detailGroup.lastOccurredUtc).format('YYYY-MM-DD HH:mm:ss')}</Descriptions.Item>
              <Descriptions.Item label="级别">{severityTag(detailGroup.severity)}</Descriptions.Item>
              <Descriptions.Item label="状态">{statusTag(detailGroup.aggregateStatus)}</Descriptions.Item>
              <Descriptions.Item label="消息" span={2}>{detailGroup.message}</Descriptions.Item>
            </Descriptions>

            <h4>子告警列表</h4>
            <Table
              rowKey="alarmId"
              columns={alarmColumns}
              dataSource={detailAlarms}
              loading={detailLoading}
              pagination={{ pageSize: 10 }}
              size="small"
            />
          </>
        )}
      </Modal>

      {/* 确认模态框 */}
      <Modal
        title="确认聚合组"
        open={ackModalOpen}
        onCancel={() => setAckModalOpen(false)}
        onOk={() => void submitAck()}
        confirmLoading={ackSubmitting}
        okText="确认"
        cancelText="取消"
      >
        {ackTarget && (
          <p style={{ marginBottom: 16, color: 'var(--color-text-secondary)' }}>
            将确认聚合组 <strong>{ackTarget.groupId}</strong> 及其 <strong>{ackTarget.alarmCount}</strong> 条告警
          </p>
        )}
        <Form form={ackForm} layout="vertical">
          <Form.Item label="确认人" name="ackedBy" rules={[{ required: true, message: '请输入确认人' }]}>
            <Input placeholder="例如：operator1" />
          </Form.Item>
          <Form.Item label="备注" name="ackNote">
            <Input.TextArea placeholder="可选" rows={3} />
          </Form.Item>
        </Form>
      </Modal>
    </Space>
  )
}
