import { useCallback, useEffect, useMemo, useState } from 'react'
import { Alert, Button, Card, Col, Descriptions, Divider, Form, InputNumber, message, Popconfirm, Row, Space, Spin, Typography } from 'antd'

const { Text } = Typography
import { ReloadOutlined, SaveOutlined, DeleteOutlined } from '@ant-design/icons'
import { cleanupData, getAllSettings, getSystemInfo, setSetting, type CleanupResult, type SystemInfo, type SystemSetting } from '../../api/settings'
import PageHeader from '../../components/common/PageHeader'

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(2)} KB`
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(2)} MB`
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`
}

function formatUptime(seconds: number): string {
  const days = Math.floor(seconds / 86400)
  const hours = Math.floor((seconds % 86400) / 3600)
  const minutes = Math.floor((seconds % 3600) / 60)
  if (days > 0) return `${days}天 ${hours}小时 ${minutes}分钟`
  if (hours > 0) return `${hours}小时 ${minutes}分钟`
  return `${minutes}分钟`
}

const KEY_TELEMETRY = 'retention.telemetry.days'
const KEY_ALARM = 'retention.alarm.days'
const KEY_HEALTH = 'retention.health.days'

export default function Settings() {
  const [loading, setLoading] = useState<boolean>(true)
  const [saving, setSaving] = useState<boolean>(false)
  const [cleaning, setCleaning] = useState<boolean>(false)

  const [info, setInfo] = useState<SystemInfo | null>(null)
  const [settings, setSettings] = useState<SystemSetting[]>([])
  const [cleanupResult, setCleanupResult] = useState<CleanupResult | null>(null)

  const [form] = Form.useForm()

  const retentionValues = useMemo(() => {
    const map = new Map(settings.map(s => [s.key, s.value]))
    const telemetryDays = Number(map.get(KEY_TELEMETRY) ?? '30')
    const alarmDays = Number(map.get(KEY_ALARM) ?? '90')
    const healthDays = Number(map.get(KEY_HEALTH) ?? '7')

    return {
      telemetryDays: Number.isFinite(telemetryDays) ? telemetryDays : 30,
      alarmDays: Number.isFinite(alarmDays) ? alarmDays : 90,
      healthDays: Number.isFinite(healthDays) ? healthDays : 7
    }
  }, [settings])

  const loadAll = useCallback(async () => {
    setLoading(true)
    setCleanupResult(null)
    try {
      const [infoResp, settingResp] = await Promise.all([getSystemInfo(), getAllSettings()])

      if (!infoResp.success || !infoResp.data) {
        message.error(infoResp.error || '获取系统信息失败')
      } else {
        setInfo(infoResp.data)
      }

      if (!settingResp.success || !settingResp.data) {
        message.error(settingResp.error || '获取系统设置失败')
      } else {
        setSettings(settingResp.data)
      }
    } catch (err) {
      message.error('请求失败，请检查后端服务是否运行')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void loadAll()
  }, [loadAll])

  useEffect(() => {
    form.setFieldsValue({
      telemetryDays: retentionValues.telemetryDays,
      alarmDays: retentionValues.alarmDays,
      healthDays: retentionValues.healthDays
    })
  }, [form, retentionValues])

  const onSaveRetention = useCallback(async () => {
    try {
      const values = await form.validateFields()
      setSaving(true)

      const telemetryDays = String(values.telemetryDays)
      const alarmDays = String(values.alarmDays)
      const healthDays = String(values.healthDays)

      const calls = [
        setSetting(KEY_TELEMETRY, telemetryDays),
        setSetting(KEY_ALARM, alarmDays),
        setSetting(KEY_HEALTH, healthDays)
      ]

      const results = await Promise.all(calls)
      const failed = results.find(r => !r.success)

      if (failed) {
        message.error(failed.error || '保存失败')
        return
      }

      message.success('保存成功')
      await loadAll()
    } catch {
      // validateFields 已提示
    } finally {
      setSaving(false)
    }
  }, [form, loadAll])

  const onCleanup = useCallback(async () => {
    setCleaning(true)
    setCleanupResult(null)
    try {
      const resp = await cleanupData()
      if (!resp.success || !resp.data) {
        message.error(resp.error || '清理失败')
        return
      }
      setCleanupResult(resp.data)
      message.success('清理完成')
      await loadAll()
    } catch {
      message.error('清理请求失败')
    } finally {
      setCleaning(false)
    }
  }, [loadAll])

  return (
    <Spin spinning={loading}>
      <Space direction="vertical" style={{ width: '100%' }} size="large">
        <PageHeader
          title="系统设置"
          description="查看系统信息、配置保留策略并执行数据清理"
          extra={
            <Button icon={<ReloadOutlined />} onClick={() => void loadAll()}>
              刷新
            </Button>
          }
        />

        <Card title="系统信息">
          {!info ? (
            <Alert type="warning" message="暂无系统信息" showIcon />
          ) : (
            <Descriptions column={2} bordered size="small">
              <Descriptions.Item label="版本">{info.version}</Descriptions.Item>
              <Descriptions.Item label="Edge ID">{info.edgeId}</Descriptions.Item>
              <Descriptions.Item label="数据库路径" span={2}>
                <Text code>{info.databasePath}</Text>
              </Descriptions.Item>
              <Descriptions.Item label="数据库大小">{formatBytes(info.databaseSizeBytes)}</Descriptions.Item>
              <Descriptions.Item label="运行时间">{formatUptime(info.uptimeSeconds)}</Descriptions.Item>
              <Descriptions.Item label="遥测点数（总）">{info.totalTelemetryPoints}</Descriptions.Item>
              <Descriptions.Item label="告警数量（总）">{info.totalAlarms}</Descriptions.Item>
              <Descriptions.Item label="设备数量（总）">{info.totalDevices}</Descriptions.Item>
              <Descriptions.Item label="标签数量（总）">{info.totalTags}</Descriptions.Item>
              <Descriptions.Item label="启动时间（UTC）" span={2}>
                {new Date(info.startTime).toISOString()}
              </Descriptions.Item>
            </Descriptions>
          )}
        </Card>

        <Card
          title="数据保留策略"
          extra={
            <Button type="primary" icon={<SaveOutlined />} loading={saving} onClick={() => void onSaveRetention()}>
              保存
            </Button>
          }
        >
          <Form
            form={form}
            layout="vertical"
            initialValues={{
              telemetryDays: 30,
              alarmDays: 90,
              healthDays: 7
            }}
          >
            <Row gutter={16}>
              <Col xs={24} md={8}>
                <Form.Item
                  label="遥测数据保留天数"
                  name="telemetryDays"
                  rules={[{ required: true, message: '请输入保留天数' }]}
                >
                  <InputNumber min={1} max={365} style={{ width: '100%' }} />
                </Form.Item>
              </Col>
              <Col xs={24} md={8}>
                <Form.Item
                  label="告警数据保留天数"
                  name="alarmDays"
                  rules={[{ required: true, message: '请输入保留天数' }]}
                >
                  <InputNumber min={1} max={365} style={{ width: '100%' }} />
                </Form.Item>
              </Col>
              <Col xs={24} md={8}>
                <Form.Item
                  label="健康快照保留天数"
                  name="healthDays"
                  rules={[{ required: true, message: '请输入保留天数' }]}
                >
                  <InputNumber min={1} max={30} style={{ width: '100%' }} />
                </Form.Item>
              </Col>
            </Row>

            <Alert
              type="info"
              showIcon
              message="说明"
              description={'修改保留策略只影响后续清理行为。点击"立即清理"后将按策略删除旧数据，并执行 VACUUM 释放空间。'}
            />
          </Form>
        </Card>

        <Card
          title="数据清理"
          extra={
            <Popconfirm
              title="确认立即清理？"
              description="将删除超过保留天数的数据，并执行 VACUUM。建议在低峰期操作。"
              okText="确认"
              cancelText="取消"
              onConfirm={() => void onCleanup()}
            >
              <Button danger icon={<DeleteOutlined />} loading={cleaning}>
                立即清理
              </Button>
            </Popconfirm>
          }
        >
          <Descriptions bordered size="small" column={3}>
            <Descriptions.Item label="遥测保留">{retentionValues.telemetryDays} 天</Descriptions.Item>
            <Descriptions.Item label="告警保留">{retentionValues.alarmDays} 天</Descriptions.Item>
            <Descriptions.Item label="健康保留">{retentionValues.healthDays} 天</Descriptions.Item>
          </Descriptions>

          <Divider />

          {!cleanupResult ? (
            <Alert type="warning" showIcon message="尚未执行清理" />
          ) : (
            <Descriptions bordered size="small" column={2} title="清理结果">
              <Descriptions.Item label="删除遥测点">{cleanupResult.deletedTelemetryPoints}</Descriptions.Item>
              <Descriptions.Item label="删除告警">{cleanupResult.deletedAlarms}</Descriptions.Item>
              <Descriptions.Item label="删除健康快照">{cleanupResult.deletedHealthSnapshots}</Descriptions.Item>
              <Descriptions.Item label="释放空间">{formatBytes(cleanupResult.freedBytes)}</Descriptions.Item>
            </Descriptions>
          )}
        </Card>
      </Space>
    </Spin>
  )
}
