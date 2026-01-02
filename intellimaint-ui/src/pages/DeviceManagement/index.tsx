import React, { useCallback, useEffect, useState } from 'react'
import {
  Button,
  Card,
  Form,
  Input,
  InputNumber,
  message,
  Modal,
  Popconfirm,
  Select,
  Space,
  Switch,
  Table,
  Tag,
  Tooltip,
  Typography
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { PlusOutlined } from '@ant-design/icons'
import type { Device, CreateDeviceRequest, UpdateDeviceRequest } from '../../types/device'
import { ProtocolOptions, DefaultPorts, PlcTypeOptions, isLibPlcTag } from '../../types/device'
import { createDevice, deleteDevice, getDevices, updateDevice } from '../../api/device'

type Mode = 'create' | 'edit'

function formatUtcMs(ms: number) {
  try {
    return new Date(ms).toLocaleString('zh-CN')
  } catch {
    return String(ms)
  }
}

function parseMetadataJson(input: string | undefined): Record<string, string> | undefined {
  const text = (input ?? '').trim()
  if (!text) return undefined
  const obj = JSON.parse(text)
  if (obj === null || typeof obj !== 'object' || Array.isArray(obj)) {
    throw new Error('Metadata 必须是 JSON 对象，例如：{"key":"value"}')
  }
  const out: Record<string, string> = {}
  for (const [k, v] of Object.entries(obj as Record<string, unknown>)) {
    if (typeof v !== 'string') {
      throw new Error(`Metadata 的值必须是 string：${k}`)
    }
    out[k] = v
  }
  return out
}

export default function DeviceManagement() {
  const [loading, setLoading] = useState(false)
  const [devices, setDevices] = useState<Device[]>([])

  const [modalOpen, setModalOpen] = useState(false)
  const [modalMode, setModalMode] = useState<Mode>('create')
  const [current, setCurrent] = useState<Device | null>(null)
  
  // v55: 跟踪当前选择的协议以显示/隐藏 LibPlcTag 字段
  const [selectedProtocol, setSelectedProtocol] = useState<string>('')

  const [form] = Form.useForm()

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const data = await getDevices()
      setDevices(data)
    } catch (e: any) {
      message.error(e?.message ?? '获取设备列表失败')
      setDevices([])
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    load()
  }, [load])

  const openCreate = useCallback(() => {
    setModalMode('create')
    setCurrent(null)
    form.resetFields()
    form.setFieldsValue({
      enabled: true,
      protocol: 'OpcUa',
      port: 4840
    })
    setSelectedProtocol('OpcUa')
    setModalOpen(true)
  }, [form])

  const openEdit = useCallback(
    (device: Device) => {
      setModalMode('edit')
      setCurrent(device)
      form.resetFields()
      
      // v55: 解析 LibPlcTag metadata
      const metadata = device.metadata || {}
      
      form.setFieldsValue({
        deviceId: device.deviceId,
        name: device.name ?? '',
        location: device.location ?? '',
        model: device.model ?? '',
        protocol: device.protocol ?? '',
        host: device.host ?? '',
        port: device.port ?? undefined,
        connectionString: device.connectionString ?? '',
        enabled: device.enabled,
        metadataJson: device.metadata ? JSON.stringify(device.metadata, null, 2) : '',
        // v55: LibPlcTag 字段
        plcType: metadata.PlcType || 'ControlLogix',
        plcPath: metadata.Path || '1,0',
        plcSlot: metadata.Slot ? parseInt(metadata.Slot) : 0,
        maxConnections: metadata.MaxConnections ? parseInt(metadata.MaxConnections) : 4,
        timeoutMs: metadata.TimeoutMs ? parseInt(metadata.TimeoutMs) : 5000
      })
      setSelectedProtocol(device.protocol || '')
      setModalOpen(true)
    },
    [form]
  )

  const closeModal = useCallback(() => {
    setModalOpen(false)
  }, [])

  const onProtocolChange = useCallback(
    (protocol: string | undefined) => {
      if (!protocol) return
      setSelectedProtocol(protocol)
      const p = DefaultPorts[protocol]
      if (p) {
        form.setFieldsValue({ port: p })
      }
    },
    [form]
  )

  const onSubmitModal = useCallback(async () => {
    try {
      const values = await form.validateFields()

      // 合并基础 metadata
      let metadata = parseMetadataJson(values.metadataJson)
      
      // v55: 合并 LibPlcTag 特有字段到 metadata
      if (isLibPlcTag(values.protocol)) {
        metadata = metadata || {}
        if (values.plcType) metadata.PlcType = values.plcType
        if (values.plcPath) metadata.Path = values.plcPath
        if (values.plcSlot !== undefined) metadata.Slot = String(values.plcSlot)
        if (values.maxConnections !== undefined) metadata.MaxConnections = String(values.maxConnections)
        if (values.timeoutMs !== undefined) metadata.TimeoutMs = String(values.timeoutMs)
      }

      if (modalMode === 'create') {
        const req: CreateDeviceRequest = {
          deviceId: values.deviceId.trim(),
          name: values.name?.trim() || undefined,
          location: values.location?.trim() || undefined,
          model: values.model?.trim() || undefined,
          protocol: values.protocol || undefined,
          host: values.host?.trim() || undefined,
          port: values.port || undefined,
          connectionString: values.connectionString?.trim() || undefined,
          enabled: !!values.enabled,
          metadata
        }

        setLoading(true)
        await createDevice(req)
        message.success('设备创建成功')
        setModalOpen(false)
        await load()
        return
      }

      // edit
      if (!current) {
        message.error('未选择要编辑的设备')
        return
      }

      const req: UpdateDeviceRequest = {
        name: values.name?.trim() || undefined,
        location: values.location?.trim() || undefined,
        model: values.model?.trim() || undefined,
        protocol: values.protocol || undefined,
        host: values.host?.trim() || undefined,
        port: values.port || undefined,
        connectionString: values.connectionString?.trim() || undefined,
        enabled: !!values.enabled,
        metadata
      }

      setLoading(true)
      await updateDevice(current.deviceId, req)
      message.success('设备更新成功')
      setModalOpen(false)
      await load()
    } catch (e: any) {
      // validateFields 的错误不弹 message
      if (e?.message && typeof e.message === 'string') {
        message.error(e.message)
      }
    } finally {
      setLoading(false)
    }
  }, [current, form, load, modalMode])

  const onDelete = useCallback(
    async (deviceId: string) => {
      setLoading(true)
      try {
        await deleteDevice(deviceId)
        message.success('删除成功')
        await load()
      } catch (e: any) {
        message.error(e?.message ?? '删除失败')
      } finally {
        setLoading(false)
      }
    },
    [load]
  )

  const onToggleEnabled = useCallback(
    async (device: Device, enabled: boolean) => {
      // 只更新 enabled 字段（其余保持不变）
      setLoading(true)
      try {
        await updateDevice(device.deviceId, { enabled })
        message.success(enabled ? '已启用' : '已禁用')
        await load()
      } catch (e: any) {
        message.error(e?.message ?? '状态更新失败')
      } finally {
        setLoading(false)
      }
    },
    [load]
  )

  const columns: ColumnsType<Device> = [
    {
      title: '设备ID',
      dataIndex: 'deviceId',
      key: 'deviceId',
      width: 160,
      render: (v: string) => <Typography.Text code>{v}</Typography.Text>
    },
    { title: '名称', dataIndex: 'name', key: 'name', width: 140, render: (v) => v ?? '-' },
    {
      title: '协议',
      dataIndex: 'protocol',
      key: 'protocol',
      width: 120,
      render: (v: string | null | undefined) => {
        const p = (v ?? 'unknown').toLowerCase()
        const color = p === 'libplctag' ? 'blue' : p === 'opcua' ? 'green' : p === 'modbustcp' ? 'gold' : 'default'
        return <Tag color={color}>{v ?? '-'}</Tag>
      }
    },
    {
      title: '连接地址',
      key: 'endpoint',
      width: 240,
      ellipsis: true,
      render: (_: unknown, record: Device) => {
        if (record.connectionString) {
          return <Tooltip title={record.connectionString}><span>{record.connectionString}</span></Tooltip>
        }
        if (record.host) {
          const protocol = (record.protocol || '').toLowerCase()
          const port = record.port || DefaultPorts[protocol] || null
          const addr = port ? `${record.host}:${port}` : record.host
          return <Tooltip title={addr}><span>{addr}</span></Tooltip>
        }
        return '-'
      }
    },
    { title: '位置', dataIndex: 'location', key: 'location', width: 120, render: (v) => v ?? '-' },
    {
      title: '状态',
      dataIndex: 'enabled',
      key: 'enabled',
      width: 100,
      render: (_: unknown, record: Device) => (
        <Switch
          checked={record.enabled}
          checkedChildren="启用"
          unCheckedChildren="禁用"
          onChange={(checked) => onToggleEnabled(record, checked)}
        />
      )
    },
    {
      title: '创建时间',
      dataIndex: 'createdUtc',
      key: 'createdUtc',
      width: 170,
      render: (v: number) => formatUtcMs(v)
    },
    {
      title: '操作',
      key: 'actions',
      width: 140,
      fixed: 'right',
      render: (_: unknown, record: Device) => (
        <Space>
          <Button size="small" onClick={() => openEdit(record)}>
            编辑
          </Button>
          <Popconfirm
            title="确认删除该设备？"
            description={`设备ID: ${record.deviceId}`}
            okText="删除"
            cancelText="取消"
            onConfirm={() => onDelete(record.deviceId)}
          >
            <Button size="small" danger>
              删除
            </Button>
          </Popconfirm>
        </Space>
      )
    }
  ]

  return (
    <>
      {/* 页面标题 */}
      <div style={{ marginBottom: 24 }}>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
          <div>
            <h1 style={{ fontSize: 24, fontWeight: 700, color: 'var(--color-text-primary)', margin: '0 0 8px 0' }}>设备管理</h1>
            <p style={{ fontSize: 14, color: 'var(--color-text-muted)', margin: 0 }}>管理设备信息、配置连接协议和参数</p>
          </div>
          <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
            新增设备
          </Button>
        </div>
      </div>

      {/* 设备表格 */}
      <Card>
        <Table
          rowKey={(r) => r.deviceId}
          loading={loading}
          columns={columns}
          dataSource={devices}
          size="middle"
          scroll={{ x: 1200 }}
          pagination={{ pageSize: 10 }}
        />
      </Card>

      <Modal
        title={modalMode === 'create' ? '新增设备' : '编辑设备'}
        open={modalOpen}
        onCancel={closeModal}
        onOk={onSubmitModal}
        okText="保存"
        cancelText="取消"
        confirmLoading={loading}
        destroyOnClose
        width={720}
      >
        <Form
          form={form}
          layout="vertical"
          initialValues={{ enabled: true, protocol: 'OpcUa', port: 4840 }}
        >
          <Form.Item
            label="DeviceId"
            name="deviceId"
            rules={[
              { required: true, message: 'DeviceId 必填' },
              { max: 128, message: 'DeviceId 过长' }
            ]}
          >
            <Input placeholder="例如：KEP-001 / PLC-CLX-01" disabled={modalMode === 'edit'} />
          </Form.Item>

          <Form.Item label="名称" name="name">
            <Input placeholder="可选" />
          </Form.Item>

          <Form.Item label="协议" name="protocol">
            <Select 
              options={ProtocolOptions} 
              placeholder="选择协议" 
              onChange={(v) => onProtocolChange(v as string | undefined)}
            />
          </Form.Item>

          <Form.Item label="主机地址" name="host">
            <Input placeholder="例如: localhost 或 192.168.1.100" />
          </Form.Item>

          <Form.Item label="端口" name="port">
            <InputNumber min={1} max={65535} style={{ width: '100%' }} placeholder="例如: 49320" />
          </Form.Item>

          {/* v55: LibPlcTag 特有字段 */}
          {isLibPlcTag(selectedProtocol) && (
            <>
              <Form.Item 
                label="PLC 类型" 
                name="plcType"
                tooltip="Allen-Bradley PLC 型号"
              >
                <Select 
                  options={PlcTypeOptions} 
                  placeholder="选择 PLC 类型"
                />
              </Form.Item>

              <Form.Item 
                label="路径 (Path)" 
                name="plcPath"
                tooltip="EtherNet/IP 路径，如 1,0"
              >
                <Input placeholder="1,0" />
              </Form.Item>

              <Form.Item 
                label="槽位 (Slot)" 
                name="plcSlot"
                tooltip="CPU 槽位号"
              >
                <InputNumber min={0} max={16} placeholder="0" style={{ width: '100%' }} />
              </Form.Item>

              <Form.Item 
                label="最大连接数" 
                name="maxConnections"
                tooltip="同时打开的连接数，ControlLogix 最大 8"
              >
                <InputNumber min={1} max={8} placeholder="4" style={{ width: '100%' }} />
              </Form.Item>

              <Form.Item 
                label="超时 (ms)" 
                name="timeoutMs"
                tooltip="读取超时时间"
              >
                <InputNumber min={100} max={30000} placeholder="5000" style={{ width: '100%' }} />
              </Form.Item>
            </>
          )}

          <Form.Item
            label="连接字符串（可选）"
            name="connectionString"
            tooltip="完整连接字符串，填写后将优先使用"
          >
            <Input placeholder="例如: opc.tcp://localhost:49320" />
          </Form.Item>

          <Form.Item label="位置" name="location">
            <Input placeholder="可选" />
          </Form.Item>

          <Form.Item label="型号" name="model">
            <Input placeholder="可选" />
          </Form.Item>

          <Form.Item label="启用" name="enabled" valuePropName="checked">
            <Switch checkedChildren="启用" unCheckedChildren="禁用" />
          </Form.Item>

          <Form.Item
            label="Metadata（JSON，可选）"
            name="metadataJson"
            tooltip={'示例：{"line":"A","owner":"team1"}。注意：value 必须是字符串'}
          >
            <Input.TextArea rows={3} placeholder='{"key":"value"}' />
          </Form.Item>
        </Form>
      </Modal>
    </>
  )
}
