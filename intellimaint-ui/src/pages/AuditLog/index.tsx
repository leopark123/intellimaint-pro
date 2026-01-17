import { useCallback, useEffect, useMemo, useState } from 'react'
import { Button, Card, DatePicker, Form, Input, Select, Space, Table, Tag, Typography, message } from 'antd'
import type { ColumnsType, TablePaginationConfig } from 'antd/es/table'
import dayjs from 'dayjs'
import { getAuditActions, getAuditResourceTypes, queryAuditLogs } from '../../api/audit'
import type { AuditLogEntry, PagedAuditLogResult } from '../../types/audit'
import { ActionLabels, ResourceTypeLabels } from '../../types/audit'
import { logError } from '../../utils/logger'

const { RangePicker } = DatePicker
const { Text } = Typography

function formatTime(ts: number) {
  return dayjs(ts).format('YYYY-MM-DD HH:mm:ss')
}

function safeLabel(map: Record<string, string>, key: string) {
  return map[key] ?? key
}

export default function AuditLog() {
  const [form] = Form.useForm()

  const [loading, setLoading] = useState(false)
  const [actions, setActions] = useState<string[]>([])
  const [resourceTypes, setResourceTypes] = useState<string[]>([])
  const [data, setData] = useState<PagedAuditLogResult>({
    items: [],
    totalCount: 0,
    limit: 50,
    offset: 0
  })

  const fetchMeta = useCallback(async () => {
    try {
      const [a, r] = await Promise.all([getAuditActions(), getAuditResourceTypes()])
      if (a.success && a.data) setActions(a.data)
      if (r.success && r.data) setResourceTypes(r.data)
    } catch (err) {
      logError('加载筛选项失败', err, 'AuditLog')
      message.error('加载筛选项失败')
    }
  }, [])

  const fetchList = useCallback(
    async (offset: number) => {
      setLoading(true)
      try {
        const values = form.getFieldsValue()
        const range = values.timeRange as [dayjs.Dayjs, dayjs.Dayjs] | undefined

        const startTs = range?.[0] ? range[0].valueOf() : undefined
        const endTs = range?.[1] ? range[1].valueOf() : undefined

        const res = await queryAuditLogs({
          action: values.action || undefined,
          resourceType: values.resourceType || undefined,
          resourceId: values.resourceId || undefined,
          userId: values.userId || undefined,
          startTs,
          endTs,
          limit: typeof values.limit === 'number' ? values.limit : 50,
          offset
        })

        if (!res.success || !res.data) {
          message.error(res.error || '查询失败')
          return
        }

        setData(res.data)
      } catch (err) {
        logError('查询审计日志失败', err, 'AuditLog')
        message.error('查询失败')
      } finally {
        setLoading(false)
      }
    },
    [form]
  )

  useEffect(() => {
    form.setFieldsValue({
      limit: 50,
      offset: 0
    })
    fetchMeta()
    fetchList(0)
  }, [fetchMeta, fetchList, form])

  const columns: ColumnsType<AuditLogEntry> = useMemo(
    () => [
      {
        title: '时间',
        dataIndex: 'ts',
        width: 180,
        render: (v: number) => <Text>{formatTime(v)}</Text>
      },
      {
        title: '用户',
        dataIndex: 'userName',
        width: 140,
        render: (_: unknown, r) => (
          <Space direction="vertical" size={0}>
            <Text>{r.userName}</Text>
            <Text type="secondary" style={{ fontSize: 12 }}>
              {r.userId}
            </Text>
          </Space>
        )
      },
      {
        title: '操作',
        dataIndex: 'action',
        width: 140,
        render: (v: string) => <Tag>{safeLabel(ActionLabels, v)}</Tag>
      },
      {
        title: '资源类型',
        dataIndex: 'resourceType',
        width: 120,
        render: (v: string) => <Tag>{safeLabel(ResourceTypeLabels, v)}</Tag>
      },
      {
        title: '资源ID',
        dataIndex: 'resourceId',
        width: 180,
        render: (v: string | null | undefined) => <Text>{v || '-'}</Text>
      },
      {
        title: '详情',
        dataIndex: 'details',
        render: (v: string | null | undefined) => <Text>{v || '-'}</Text>
      },
      {
        title: 'IP',
        dataIndex: 'ipAddress',
        width: 140,
        render: (v: string | null | undefined) => <Text>{v || '-'}</Text>
      }
    ],
    []
  )

  const pagination: TablePaginationConfig = useMemo(() => {
    const current = Math.floor((data.offset || 0) / (data.limit || 50)) + 1
    return {
      current,
      pageSize: data.limit || 50,
      total: data.totalCount || 0,
      showSizeChanger: true,
      pageSizeOptions: [20, 50, 100, 200],
      showTotal: (total) => `共 ${total} 条`
    }
  }, [data.limit, data.offset, data.totalCount])

  const onTableChange = useCallback(
    (p: TablePaginationConfig) => {
      const pageSize = typeof p.pageSize === 'number' ? p.pageSize : 50
      const current = typeof p.current === 'number' ? p.current : 1
      const nextOffset = (current - 1) * pageSize

      form.setFieldsValue({ limit: pageSize })
      fetchList(nextOffset)
    },
    [fetchList, form]
  )

  const onSearch = useCallback(async () => {
    await fetchList(0)
  }, [fetchList])

  const onReset = useCallback(async () => {
    form.resetFields()
    form.setFieldsValue({ limit: 50 })
    await fetchList(0)
  }, [fetchList, form])

  return (
    <div>
      {/* 页面标题 */}
      <div style={{ marginBottom: 24 }}>
        <h1 style={{ fontSize: 24, fontWeight: 700, color: 'var(--color-text-primary)', margin: '0 0 8px 0' }}>审计日志</h1>
        <p style={{ fontSize: 14, color: 'var(--color-text-muted)', margin: 0 }}>查看系统操作记录和用户行为追溯</p>
      </div>

      <Card>
        <Form
        form={form}
        layout="inline"
        initialValues={{ limit: 50 }}
        style={{ marginBottom: 16, rowGap: 12, flexWrap: 'wrap' }}
      >
        <Form.Item name="action" label="操作">
          <Select
            allowClear
            style={{ width: 200 }}
            placeholder="选择操作"
            options={actions.map((a) => ({ label: safeLabel(ActionLabels, a), value: a }))}
          />
        </Form.Item>

        <Form.Item name="resourceType" label="资源类型">
          <Select
            allowClear
            style={{ width: 180 }}
            placeholder="选择资源类型"
            options={resourceTypes.map((t) => ({ label: safeLabel(ResourceTypeLabels, t), value: t }))}
          />
        </Form.Item>

        <Form.Item name="resourceId" label="资源ID">
          <Input style={{ width: 200 }} placeholder="输入资源ID" />
        </Form.Item>

        <Form.Item name="userId" label="用户ID">
          <Input style={{ width: 160 }} placeholder="输入用户ID" />
        </Form.Item>

        <Form.Item name="timeRange" label="时间范围">
          <RangePicker showTime />
        </Form.Item>

        <Form.Item name="limit" hidden>
          <Input />
        </Form.Item>

        <Form.Item>
          <Space>
            <Button type="primary" onClick={onSearch} loading={loading}>
              查询
            </Button>
            <Button onClick={onReset} disabled={loading}>
              重置
            </Button>
          </Space>
        </Form.Item>
      </Form>

      <Table<AuditLogEntry>
        rowKey={(r) => r.id}
        loading={loading}
        columns={columns}
        dataSource={data.items}
        pagination={pagination}
        onChange={onTableChange}
        size="middle"
      />
      </Card>
    </div>
  )
}
