import { useState, useEffect, useMemo, useCallback } from 'react'
import { Card, Form, Select, DatePicker, Button, Table, message, InputNumber, Space, Dropdown } from 'antd'
import { SearchOutlined, DownloadOutlined } from '@ant-design/icons'
import dayjs from 'dayjs'
import { queryTelemetry, getTags } from '../../api/telemetry'
import { exportTelemetryCsv, exportTelemetryXlsx } from '../../api/export'
import type { TelemetryPoint, TagInfo, TelemetryQueryParams } from '../../types/telemetry'

const { RangePicker } = DatePicker

export default function DataExplorer() {
  const [form] = Form.useForm()
  const [loading, setLoading] = useState(false)
  const [tags, setTags] = useState<TagInfo[]>([])
  const [data, setData] = useState<TelemetryPoint[]>([])

  useEffect(() => {
    loadTags()
  }, [])

  const loadTags = async () => {
    try {
      const res = await getTags()
      if (res.success && res.data) {
        setTags(res.data)
      }
    } catch (error) {
      console.error(error)
    }
  }

  const handleSearch = async () => {
    try {
      const values = await form.validateFields()
      setLoading(true)

      const params: TelemetryQueryParams = {
        limit: values.limit || 1000
      }

      if (values.deviceId) {
        params.deviceId = values.deviceId
      }
      if (values.tagId) {
        params.tagId = values.tagId
      }
      if (values.timeRange && values.timeRange.length === 2) {
        params.startTs = values.timeRange[0].valueOf()
        params.endTs = values.timeRange[1].valueOf()
      }

      const res = await queryTelemetry(params)
      if (res.success && res.data) {
        setData(res.data)
        message.success(`查询到 ${res.data.length} 条记录`)
      } else {
        message.warning('未查询到数据')
        setData([])
      }
    } catch (error) {
      message.error('查询失败')
      console.error(error)
    } finally {
      setLoading(false)
    }
  }

  const getExportParams = () => {
    const values = form.getFieldsValue()
    const params: TelemetryQueryParams = {
      limit: values.limit || 10000
    }
    if (values.deviceId) params.deviceId = values.deviceId
    if (values.tagId) params.tagId = values.tagId
    if (values.timeRange && values.timeRange.length === 2) {
      params.startTs = values.timeRange[0].valueOf()
      params.endTs = values.timeRange[1].valueOf()
    }
    return params
  }

  const handleExportCsv = () => {
    const params = getExportParams()
    exportTelemetryCsv(params)
    message.success('正在导出 CSV...')
  }

  const handleExportXlsx = useCallback(() => {
    const params = getExportParams()
    exportTelemetryXlsx(params)
    message.success('正在导出 Excel...')
  }, [getExportParams])

  // v56.1: 使用 useMemo 优化 - 避免每次渲染重新创建列配置
  const columns = useMemo(() => [
    {
      title: '时间',
      dataIndex: 'ts',
      key: 'ts',
      width: 180,
      render: (ts: number) => new Date(ts).toLocaleString('zh-CN')
    },
    {
      title: '设备',
      dataIndex: 'deviceId',
      key: 'deviceId',
      width: 120
    },
    {
      title: '标签',
      dataIndex: 'tagId',
      key: 'tagId',
      width: 150
    },
    {
      title: '值',
      dataIndex: 'value',
      key: 'value',
      width: 120,
      render: (value: number | string | boolean | null) => (
        <span style={{ fontWeight: 'bold' }}>{String(value)}</span>
      )
    },
    {
      title: '类型',
      dataIndex: 'valueType',
      key: 'valueType',
      width: 100
    },
    {
      title: '质量',
      dataIndex: 'quality',
      key: 'quality',
      width: 80
    }
  ], [])

  // v56.1: 使用 useMemo 优化 - 只在 tags 变化时重新计算
  const deviceOptions = useMemo(() =>
    [...new Set(tags.map(t => t.deviceId))].map(d => ({
      label: d,
      value: d
    })),
    [tags]
  )

  const tagOptions = useMemo(() =>
    tags.map(t => ({
      label: `${t.tagId} (${t.deviceId})`,
      value: t.tagId
    })),
    [tags]
  )

  return (
    <div>
      {/* 页面标题 */}
      <div style={{ marginBottom: 24 }}>
        <h1 style={{ fontSize: 24, fontWeight: 700, color: 'var(--color-text-primary)', margin: '0 0 8px 0' }}>数据查询</h1>
        <p style={{ fontSize: 14, color: 'var(--color-text-muted)', margin: 0 }}>查询和导出历史遥测数据</p>
      </div>

      <Card style={{ marginBottom: 16 }}>
        <Form form={form} layout="inline" style={{ flexWrap: 'wrap', gap: 8 }}>
          <Form.Item name="deviceId" label="设备">
            <Select
              style={{ width: 150 }}
              placeholder="全部"
              allowClear
              options={deviceOptions}
            />
          </Form.Item>
          <Form.Item name="tagId" label="标签">
            <Select
              style={{ width: 200 }}
              placeholder="全部"
              allowClear
              showSearch
              options={tagOptions}
            />
          </Form.Item>
          <Form.Item name="timeRange" label="时间范围">
            <RangePicker
              showTime
              format="YYYY-MM-DD HH:mm:ss"
              presets={[
                { label: '最近1小时', value: [dayjs().subtract(1, 'hour'), dayjs()] },
                { label: '最近24小时', value: [dayjs().subtract(24, 'hour'), dayjs()] },
                { label: '最近7天', value: [dayjs().subtract(7, 'day'), dayjs()] }
              ]}
            />
          </Form.Item>
          <Form.Item name="limit" label="限制条数" initialValue={1000}>
            <InputNumber min={1} max={10000} style={{ width: 100 }} />
          </Form.Item>
          <Form.Item>
            <Space>
              <Button
                type="primary"
                icon={<SearchOutlined />}
                onClick={handleSearch}
                loading={loading}
              >
                查询
              </Button>
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

      <Card title={`查询结果 (${data.length} 条)`}>
        <Table
          dataSource={data}
          columns={columns}
          rowKey={(record) => `${record.deviceId}-${record.tagId}-${record.ts}`}
          pagination={{
            pageSize: 50,
            showSizeChanger: true,
            showQuickJumper: true,
            showTotal: (total) => `共 ${total} 条`
          }}
          size="small"
          scroll={{ y: 400 }}
          loading={loading}
        />
      </Card>
    </div>
  )
}
