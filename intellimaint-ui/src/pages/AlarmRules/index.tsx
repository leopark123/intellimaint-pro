import { useCallback, useEffect, useMemo, useState } from 'react'
import { Button, Card, Form, Input, InputNumber, Modal, Popconfirm, Select, Space, Switch, Table, Tag, message } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import dayjs from 'dayjs'

import { getAlarmRules, createAlarmRule, updateAlarmRule, deleteAlarmRule, enableAlarmRule, disableAlarmRule } from '../../api/alarmRule'
import { getDevices } from '../../api/device'
import { getTagsByDevice } from '../../api/tag'

import type { AlarmRule, CreateAlarmRuleRequest, UpdateAlarmRuleRequest } from '../../types/alarmRule'
import { ConditionTypeOptions, SeverityOptions } from '../../types/alarmRule'
import type { Device } from '../../types/device'
import type { Tag as TagEntity } from '../../types/tag'

function formatTime(ts: number) {
  return dayjs(ts).format('YYYY-MM-DD HH:mm:ss')
}

export default function AlarmRules() {
  const [loading, setLoading] = useState(false)
  const [rules, setRules] = useState<AlarmRule[]>([])
  const [devices, setDevices] = useState<Device[]>([])
  const [tags, setTags] = useState<TagEntity[]>([])

  const [modalOpen, setModalOpen] = useState(false)
  const [editing, setEditing] = useState<AlarmRule | null>(null)
  const [modalSubmitting, setModalSubmitting] = useState(false)

  const [form] = Form.useForm()

  const refreshRules = useCallback(async () => {
    setLoading(true)
    try {
      const res = await getAlarmRules()
      if (!res.success || !res.data) {
        message.error(res.error || '加载告警规则失败')
        return
      }
      setRules(res.data)
    } catch (err) {
      console.error(err)
      message.error('加载告警规则失败')
    } finally {
      setLoading(false)
    }
  }, [])

  const loadDevices = useCallback(async () => {
    try {
      const deviceList = await getDevices()
      setDevices(deviceList)
    } catch (err) {
      console.error(err)
    }
  }, [])

  const loadTagsByDevice = useCallback(async (deviceId?: string) => {
    if (!deviceId) {
      setTags([])
      return
    }
    try {
      const tagList = await getTagsByDevice(deviceId)
      setTags(tagList)
    } catch (err) {
      console.error(err)
      setTags([])
    }
  }, [])

  useEffect(() => {
    refreshRules()
    loadDevices()
  }, [refreshRules, loadDevices])

  const openCreate = useCallback(() => {
    setEditing(null)
    form.resetFields()
    form.setFieldsValue({
      enabled: true,
      severity: 3,
      durationMs: 0,
      conditionType: 'gt'
    })
    setTags([])
    setModalOpen(true)
  }, [form])

  const openEdit = useCallback(
    async (rule: AlarmRule) => {
      setEditing(rule)
      form.resetFields()
      form.setFieldsValue({
        ruleId: rule.ruleId,
        name: rule.name,
        description: rule.description ?? undefined,
        deviceId: rule.deviceId ?? undefined,
        tagId: rule.tagId,
        conditionType: rule.conditionType,
        threshold: rule.threshold,
        durationMs: rule.durationMs,
        severity: rule.severity,
        messageTemplate: rule.messageTemplate ?? undefined,
        enabled: rule.enabled
      })
      await loadTagsByDevice(rule.deviceId ?? undefined)
      setModalOpen(true)
    },
    [form, loadTagsByDevice]
  )

  const closeModal = useCallback(() => {
    setModalOpen(false)
    setEditing(null)
    setModalSubmitting(false)
  }, [])

  const onDeviceChange = useCallback(
    async (deviceId: string | undefined) => {
      form.setFieldsValue({ tagId: undefined })
      await loadTagsByDevice(deviceId)
    },
    [form, loadTagsByDevice]
  )

  const onSubmit = useCallback(async () => {
    try {
      const values = await form.validateFields()
      setModalSubmitting(true)

      if (!editing) {
        const req: CreateAlarmRuleRequest = {
          ruleId: values.ruleId,
          name: values.name,
          description: values.description,
          tagId: values.tagId,
          deviceId: values.deviceId,
          conditionType: values.conditionType,
          threshold: values.threshold,
          durationMs: values.durationMs ?? 0,
          severity: values.severity ?? 3,
          messageTemplate: values.messageTemplate,
          enabled: values.enabled ?? true
        }

        const res = await createAlarmRule(req)
        if (!res.success) {
          message.error(res.error || '创建失败')
          return
        }
        message.success('创建成功')
      } else {
        const req: UpdateAlarmRuleRequest = {
          name: values.name,
          description: values.description,
          tagId: values.tagId,
          deviceId: values.deviceId,
          conditionType: values.conditionType,
          threshold: values.threshold,
          durationMs: values.durationMs ?? 0,
          severity: values.severity ?? 3,
          messageTemplate: values.messageTemplate,
          enabled: values.enabled
        }

        const res = await updateAlarmRule(editing.ruleId, req)
        if (!res.success) {
          message.error(res.error || '更新失败')
          return
        }
        message.success('更新成功')
      }

      closeModal()
      await refreshRules()
    } catch (err) {
      console.error(err)
      // validateFields 会抛出错误，这里不提示
    } finally {
      setModalSubmitting(false)
    }
  }, [closeModal, editing, form, refreshRules])

  const onDelete = useCallback(
    async (ruleId: string) => {
      try {
        const res = await deleteAlarmRule(ruleId)
        if (!res.success) {
          message.error(res.error || '删除失败')
          return
        }
        message.success('删除成功')
        await refreshRules()
      } catch (err) {
        console.error(err)
        message.error('删除失败')
      }
    },
    [refreshRules]
  )

  const onToggleEnabled = useCallback(
    async (rule: AlarmRule, enabled: boolean) => {
      try {
        const res = enabled ? await enableAlarmRule(rule.ruleId) : await disableAlarmRule(rule.ruleId)
        if (!res.success) {
          message.error(res.error || '更新状态失败')
          return
        }
        message.success(enabled ? '已启用' : '已禁用')
        await refreshRules()
      } catch (err) {
        console.error(err)
        message.error('更新状态失败')
      }
    },
    [refreshRules]
  )

  const deviceOptions = useMemo(
    () => devices.map((d) => ({ label: d.name ? `${d.name} (${d.deviceId})` : d.deviceId, value: d.deviceId })),
    [devices]
  )

  const tagOptions = useMemo(
    () =>
      tags.map((t) => ({
        label: t.name ? `${t.name} (${t.tagId})` : t.tagId,
        value: t.tagId
      })),
    [tags]
  )

  const columns: ColumnsType<AlarmRule> = useMemo(
    () => [
      { title: '规则ID', dataIndex: 'ruleId', width: 220 },
      { title: '名称', dataIndex: 'name', width: 200 },
      { title: '设备', dataIndex: 'deviceId', width: 160, render: (v: string | null | undefined) => v || '-' },
      { title: '标签', dataIndex: 'tagId', width: 220 },
      { title: '条件', dataIndex: 'conditionType', width: 110 },
      { title: '阈值', dataIndex: 'threshold', width: 110 },
      {
        title: '持续时间',
        dataIndex: 'durationMs',
        width: 120,
        render: (v: number) => (v && v > 0 ? `${v} ms` : '立即')
      },
      {
        title: '级别',
        dataIndex: 'severity',
        width: 110,
        render: (v: number) => <Tag>{v}</Tag>
      },
      {
        title: '状态',
        dataIndex: 'enabled',
        width: 100,
        render: (v: boolean, r) => <Switch checked={v} onChange={(checked) => onToggleEnabled(r, checked)} />
      },
      {
        title: '更新时间',
        dataIndex: 'updatedUtc',
        width: 180,
        render: (v: number) => formatTime(v)
      },
      {
        title: '操作',
        key: 'actions',
        width: 160,
        render: (_: unknown, r) => (
          <Space>
            <Button size="small" onClick={() => openEdit(r)}>
              编辑
            </Button>
            <Popconfirm title="确定删除该规则吗？" onConfirm={() => onDelete(r.ruleId)}>
              <Button size="small" danger>
                删除
              </Button>
            </Popconfirm>
          </Space>
        )
      }
    ],
    [onDelete, onToggleEnabled, openEdit]
  )

  return (
    <>
      {/* 页面标题 */}
      <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', marginBottom: 24 }}>
        <div>
          <h1 style={{ fontSize: 24, fontWeight: 700, color: 'var(--color-text-primary)', margin: '0 0 8px 0' }}>告警规则</h1>
          <p style={{ fontSize: 14, color: 'var(--color-text-muted)', margin: 0 }}>配置告警触发条件和阈值</p>
        </div>
        <Button type="primary" onClick={openCreate}>
          新增规则
        </Button>
      </div>

      <Card>
        <Table<AlarmRule>
          rowKey={(r) => r.ruleId}
          loading={loading}
          columns={columns}
          dataSource={rules}
          pagination={{ pageSize: 20, showSizeChanger: true, pageSizeOptions: [10, 20, 50, 100] }}
        />
      </Card>

      <Modal
        title={editing ? '编辑规则' : '新增规则'}
        open={modalOpen}
        onCancel={closeModal}
        onOk={onSubmit}
        confirmLoading={modalSubmitting}
        destroyOnClose
      >
        <Form form={form} layout="vertical">
          <Form.Item
            label="规则ID"
            name="ruleId"
            rules={[{ required: true, message: '请输入规则ID' }]}
          >
            <Input disabled={!!editing} placeholder="例如: rule-temp-high" />
          </Form.Item>

          <Form.Item
            label="名称"
            name="name"
            rules={[{ required: true, message: '请输入名称' }]}
          >
            <Input placeholder="例如: 温度过高" />
          </Form.Item>

          <Form.Item label="描述" name="description">
            <Input.TextArea rows={2} placeholder="可选" />
          </Form.Item>

          <Form.Item label="设备（可选）" name="deviceId">
            <Select
              allowClear
              placeholder="选择设备（不选表示仅按 TagId 匹配）"
              options={deviceOptions}
              onChange={(v) => onDeviceChange(v as string | undefined)}
            />
          </Form.Item>

          <Form.Item
            label="标签"
            name="tagId"
            rules={[{ required: true, message: '请选择或输入标签ID' }]}
          >
            <Select
              showSearch
              allowClear
              placeholder="选择标签（或直接输入 TagId）"
              options={tagOptions}
              optionFilterProp="label"
            />
          </Form.Item>

          <Form.Item
            label="条件"
            name="conditionType"
            rules={[{ required: true, message: '请选择条件' }]}
          >
            <Select options={ConditionTypeOptions} />
          </Form.Item>

          <Form.Item
            label="阈值"
            name="threshold"
            rules={[{ required: true, message: '请输入阈值' }]}
          >
            <InputNumber style={{ width: '100%' }} />
          </Form.Item>

          <Form.Item label="持续时间 (ms)" name="durationMs">
            <InputNumber min={0} style={{ width: '100%' }} placeholder="0 表示立即触发" />
          </Form.Item>

          <Form.Item label="严重级别" name="severity">
            <Select options={SeverityOptions} />
          </Form.Item>

          <Form.Item label="消息模板（可选）" name="messageTemplate">
            <Input.TextArea
              rows={3}
              placeholder="支持变量：{ruleId} {ruleName} {deviceId} {tagId} {cond} {threshold} {value}"
            />
          </Form.Item>

          <Form.Item label="启用" name="enabled" valuePropName="checked">
            <Switch />
          </Form.Item>
        </Form>
      </Modal>
    </>
  )
}
