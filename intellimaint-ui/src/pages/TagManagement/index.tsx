import React, { useCallback, useEffect, useMemo, useState } from 'react'
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
  Tag as AntTag,
  Typography
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { PlusOutlined } from '@ant-design/icons'
import { getDevices } from '../../api/device'
import type { Device } from '../../types/device'
import { isLibPlcTag } from '../../types/device'
import type { Tag, CreateTagRequest, UpdateTagRequest } from '../../types/tag'
import { DataTypeOptions, TagGroupOptions, CipTypeOptions, cipTypeToDataType, dataTypeToCipType } from '../../types/tag'
import { createTag, deleteTag, getTagsByDevice, updateTag } from '../../api/tag'

type Mode = 'create' | 'edit'

function formatUtcMs(ms: number) {
  try {
    return new Date(ms).toLocaleString('zh-CN')
  } catch {
    return String(ms)
  }
}

function getDataTypeLabel(dataType: number): string {
  return DataTypeOptions.find((x) => x.value === dataType)?.label ?? String(dataType)
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

export default function TagManagement() {
  const [loading, setLoading] = useState(false)

  const [devices, setDevices] = useState<Device[]>([])
  const [selectedDeviceId, setSelectedDeviceId] = useState<string>('')
  // v55: 跟踪选中设备的协议
  const [selectedDeviceProtocol, setSelectedDeviceProtocol] = useState<string>('')

  const [tags, setTags] = useState<Tag[]>([])

  const [modalOpen, setModalOpen] = useState(false)
  const [modalMode, setModalMode] = useState<Mode>('create')
  const [current, setCurrent] = useState<Tag | null>(null)

  const [form] = Form.useForm()

  const loadDevices = useCallback(async () => {
    setLoading(true)
    try {
      const list = await getDevices()
      setDevices(list)

      // 自动选择第一个设备
      if (!selectedDeviceId && list.length > 0) {
        setSelectedDeviceId(list[0].deviceId)
        setSelectedDeviceProtocol(list[0].protocol || '')
      }
    } catch (e: unknown) {
      message.error((e as Error)?.message ?? '获取设备列表失败')
      setDevices([])
    } finally {
      setLoading(false)
    }
  }, [selectedDeviceId])

  const loadTags = useCallback(async (deviceId: string) => {
    if (!deviceId) {
      setTags([])
      return
    }

    setLoading(true)
    try {
      const data = await getTagsByDevice(deviceId)
      setTags(data)
    } catch (e: unknown) {
      message.error((e as Error)?.message ?? '获取标签列表失败')
      setTags([])
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    loadDevices()
  }, [loadDevices])

  useEffect(() => {
    if (selectedDeviceId) {
      loadTags(selectedDeviceId)
    }
  }, [selectedDeviceId, loadTags])

  const openCreate = useCallback(() => {
    setModalMode('create')
    setCurrent(null)
    form.resetFields()
    form.setFieldsValue({
      deviceId: selectedDeviceId,
      enabled: true,
      dataType: 10,  // Float32
      tagGroup: 'Normal',
      cipType: 'REAL'
    })
    setModalOpen(true)
  }, [form, selectedDeviceId])

  const openEdit = useCallback(
    (tag: Tag) => {
      setModalMode('edit')
      setCurrent(tag)
      form.resetFields()
      
      // v55: 从 metadata 获取 CipType，或从 DataType 推断
      const cipType = tag.metadata?.CipType || dataTypeToCipType(tag.dataType)
      
      form.setFieldsValue({
        deviceId: tag.deviceId,
        tagId: tag.tagId,
        name: tag.name ?? '',
        description: tag.description ?? '',
        unit: tag.unit ?? '',
        dataType: tag.dataType,
        enabled: tag.enabled,
        address: tag.address ?? '',
        scanIntervalMs: tag.scanIntervalMs ?? undefined,
        tagGroup: tag.tagGroup ?? '',
        metadataJson: tag.metadata ? JSON.stringify(tag.metadata, null, 2) : '',
        cipType: cipType
      })
      setModalOpen(true)
    },
    [form]
  )

  const closeModal = useCallback(() => {
    setModalOpen(false)
  }, [])

  const onSubmitModal = useCallback(async () => {
    try {
      const values = await form.validateFields()
      
      let metadata = parseMetadataJson(values.metadataJson)
      
      // v55: 保存 CipType 到 metadata
      if (isLibPlcTag(selectedDeviceProtocol) && values.cipType) {
        metadata = metadata || {}
        metadata.CipType = values.cipType
      }

      if (modalMode === 'create') {
        const req: CreateTagRequest = {
          tagId: values.tagId.trim(),
          deviceId: selectedDeviceId,
          name: values.name?.trim() || undefined,
          description: values.description?.trim() || undefined,
          unit: values.unit?.trim() || undefined,
          dataType: values.dataType,
          enabled: !!values.enabled,
          address: values.address?.trim() || undefined,
          scanIntervalMs: values.scanIntervalMs || undefined,
          tagGroup: values.tagGroup || undefined,
          metadata
        }

        setLoading(true)
        await createTag(req)
        message.success('标签创建成功')
        setModalOpen(false)
        await loadTags(selectedDeviceId)
        return
      }

      // edit
      if (!current) {
        message.error('未选择要编辑的标签')
        return
      }

      const req: UpdateTagRequest = {
        name: values.name?.trim() || undefined,
        description: values.description?.trim() || undefined,
        unit: values.unit?.trim() || undefined,
        dataType: values.dataType,
        enabled: !!values.enabled,
        address: values.address?.trim() || undefined,
        scanIntervalMs: values.scanIntervalMs || undefined,
        tagGroup: values.tagGroup || undefined,
        metadata
      }

      setLoading(true)
      await updateTag(current.tagId, req)
      message.success('标签更新成功')
      setModalOpen(false)
      await loadTags(selectedDeviceId)
    } catch (e: unknown) {
      const errMsg = (e as Error)?.message
      if (errMsg && typeof errMsg === 'string') {
        message.error(errMsg)
      }
    } finally {
      setLoading(false)
    }
  }, [current, form, loadTags, modalMode, selectedDeviceId, selectedDeviceProtocol])

  const onDelete = useCallback(
    async (tagId: string) => {
      setLoading(true)
      try {
        await deleteTag(tagId)
        message.success('删除成功')
        await loadTags(selectedDeviceId)
      } catch (e: unknown) {
        message.error((e as Error)?.message ?? '删除失败')
      } finally {
        setLoading(false)
      }
    },
    [loadTags, selectedDeviceId]
  )

  const onToggleEnabled = useCallback(
    async (tag: Tag, enabled: boolean) => {
      setLoading(true)
      try {
        await updateTag(tag.tagId, { enabled })
        message.success(enabled ? '已启用' : '已禁用')
        await loadTags(selectedDeviceId)
      } catch (e: unknown) {
        message.error((e as Error)?.message ?? '状态更新失败')
      } finally {
        setLoading(false)
      }
    },
    [loadTags, selectedDeviceId]
  )

  const deviceOptions = useMemo(
    () =>
      devices.map((d) => ({
        value: d.deviceId,
        label: d.name ? `${d.deviceId} (${d.name})` : d.deviceId
      })),
    [devices]
  )

  // v55: 设备选择变化时更新协议
  const onDeviceChange = useCallback((deviceId: string) => {
    setSelectedDeviceId(deviceId)
    const device = devices.find(d => d.deviceId === deviceId)
    setSelectedDeviceProtocol(device?.protocol || '')
  }, [devices])

  // v55: CipType 变化时自动更新 DataType
  const onCipTypeChange = useCallback((cipType: string) => {
    const dataType = cipTypeToDataType(cipType)
    form.setFieldsValue({ dataType })
  }, [form])

  const columns: ColumnsType<Tag> = [
    {
      title: 'TagId',
      dataIndex: 'tagId',
      key: 'tagId',
      width: 200,
      ellipsis: true,
      render: (v: string) => <Typography.Text code>{v}</Typography.Text>
    },
    { title: '名称', dataIndex: 'name', key: 'name', width: 120, render: (v) => v ?? '-' },
    {
      title: '数据类型',
      dataIndex: 'dataType',
      key: 'dataType',
      width: 100,
      render: (v: number, record: Tag) => {
        // v55: 显示 CipType（如果有）
        const cipType = record.metadata?.CipType
        if (cipType) {
          return <AntTag color="blue">{cipType}</AntTag>
        }
        return <AntTag>{getDataTypeLabel(v)}</AntTag>
      }
    },
    { title: '单位', dataIndex: 'unit', key: 'unit', width: 80, render: (v) => v ?? '-' },
    {
      title: '地址',
      dataIndex: 'address',
      key: 'address',
      width: 280,
      ellipsis: true,
      render: (v) => v ?? '-'
    },
    {
      title: '分组',
      dataIndex: 'tagGroup',
      key: 'tagGroup',
      width: 100,
      render: (v: string | null | undefined) => {
        if (!v) return '-'
        const color = v === 'Fast' ? 'red' : v === 'Normal' ? 'blue' : 'default'
        return <AntTag color={color}>{v}</AntTag>
      }
    },
    {
      title: '状态',
      dataIndex: 'enabled',
      key: 'enabled',
      width: 100,
      render: (_: unknown, record: Tag) => (
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
      render: (_: unknown, record: Tag) => (
        <Space>
          <Button size="small" onClick={() => openEdit(record)}>
            编辑
          </Button>
          <Popconfirm
            title="确认删除该标签？"
            description={`TagId: ${record.tagId}`}
            okText="删除"
            cancelText="取消"
            onConfirm={() => onDelete(record.tagId)}
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
            <h1 style={{ fontSize: 24, fontWeight: 700, color: 'var(--color-text-primary)', margin: '0 0 8px 0' }}>标签管理</h1>
            <p style={{ fontSize: 14, color: 'var(--color-text-muted)', margin: 0 }}>配置设备标签、数据类型和采集参数</p>
          </div>
        </div>
      </div>

      {/* 设备选择器 */}
      <div style={{ marginBottom: 16 }}>
        <Space>
          <span style={{ color: 'var(--color-text-secondary)' }}>选择设备：</span>
          <Select
            style={{ width: 300 }}
            placeholder="选择设备"
            value={selectedDeviceId || undefined}
            options={deviceOptions}
            onChange={onDeviceChange}
          />
          <Button type="primary" icon={<PlusOutlined />} onClick={openCreate} disabled={!selectedDeviceId}>
            新增标签
          </Button>
        </Space>
      </div>

      <Card>
        <Table
          rowKey={(r) => r.tagId}
          loading={loading}
          columns={columns}
          dataSource={tags}
          size="middle"
          scroll={{ x: 1400 }}
          pagination={{ pageSize: 10 }}
        />
      </Card>

      <Modal
        title={modalMode === 'create' ? '新增标签' : '编辑标签'}
        open={modalOpen}
        onCancel={closeModal}
        onOk={onSubmitModal}
        okText="保存"
        cancelText="取消"
        confirmLoading={loading}
        destroyOnClose
        width={720}
      >
        <Form form={form} layout="vertical" initialValues={{ enabled: true, dataType: 10, tagGroup: 'Normal' }}>
          <Form.Item
            label="DeviceId"
            name="deviceId"
            rules={[{ required: true, message: 'DeviceId 必填' }]}
          >
            <Select options={deviceOptions} disabled={modalMode === 'edit'} />
          </Form.Item>

          <Form.Item
            label="TagId"
            name="tagId"
            rules={[
              { required: true, message: 'TagId 必填' },
              { max: 256, message: 'TagId 过长' }
            ]}
          >
            <Input placeholder='例如：Motor1_Speed / Ramp_Value' disabled={modalMode === 'edit'} />
          </Form.Item>

          <Form.Item label="名称" name="name">
            <Input placeholder="可选" />
          </Form.Item>

          <Form.Item label="描述" name="description">
            <Input placeholder="可选" />
          </Form.Item>

          <Form.Item label="单位" name="unit">
            <Input placeholder="可选，例如 rpm / °C / bar" />
          </Form.Item>

          {/* v55: LibPlcTag CipType */}
          {isLibPlcTag(selectedDeviceProtocol) && (
            <Form.Item
              label="CIP 类型"
              name="cipType"
              tooltip="Allen-Bradley CIP 数据类型，将自动映射到数据类型"
            >
              <Select
                options={CipTypeOptions.map(o => ({ value: o.value, label: o.label }))}
                placeholder="选择 CIP 类型"
                onChange={onCipTypeChange}
              />
            </Form.Item>
          )}

          <Form.Item
            label="数据类型"
            name="dataType"
            rules={[{ required: true, message: 'DataType 必填' }]}
          >
            <Select 
              options={DataTypeOptions} 
              disabled={isLibPlcTag(selectedDeviceProtocol)}
            />
          </Form.Item>

          <Form.Item label="启用" name="enabled" valuePropName="checked">
            <Switch checkedChildren="启用" unCheckedChildren="禁用" />
          </Form.Item>

          <Form.Item
            label="地址（Address）"
            name="address"
            tooltip={
              isLibPlcTag(selectedDeviceProtocol)
                ? 'PLC 内部标签名，如 Program:MainProgram.Motor1.Speed'
                : 'OPC UA: NodeId，Modbus: 寄存器地址'
            }
          >
            <Input
              placeholder={
                isLibPlcTag(selectedDeviceProtocol)
                  ? 'Program:MainProgram.Tag_Name'
                  : 'ns=2;s=Channel.Device.Tag / 40001'
              }
            />
          </Form.Item>

          <Form.Item label="采集周期（ms）" name="scanIntervalMs">
            <InputNumber 
              min={10} 
              max={60000} 
              placeholder="100 / 1000 / 5000" 
              style={{ width: '100%' }}
            />
          </Form.Item>

          <Form.Item label="分组（TagGroup）" name="tagGroup">
            <Select options={TagGroupOptions} allowClear placeholder="Fast / Normal / Slow" />
          </Form.Item>

          <Form.Item
            label="Metadata（JSON，可选）"
            name="metadataJson"
            tooltip='示例：{"line":"A","owner":"team1"}。注意：value 必须是字符串'
          >
            <Input.TextArea rows={4} placeholder='{"key":"value"}' />
          </Form.Item>
        </Form>
      </Modal>
    </>
  )
}
