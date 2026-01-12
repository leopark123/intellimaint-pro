import { useEffect, useState, useCallback } from 'react'
import {
  Card,
  Row,
  Col,
  Statistic,
  Table,
  Tag,
  Space,
  message,
  Spin,
  Progress,
  Tabs,
  Alert,
  Button,
  Tooltip
} from 'antd'
import {
  ReloadOutlined,
  WarningOutlined,
  ClockCircleOutlined,
  ExclamationCircleOutlined,
  CheckCircleOutlined,
  RiseOutlined,
  FallOutlined,
  DashboardOutlined
} from '@ant-design/icons'
import type { ColumnsType } from 'antd/es/table'
import {
  getAllRulPredictions,
  getAllDegradations,
  getPredictionAlerts
} from '../../api/prediction'
import type {
  RulPrediction,
  DegradationResult,
  DeviceDegradationSummary,
  TrendAlert,
  DegradationAlert,
  RulRiskLevel,
  RulStatus
} from '../../types/prediction'
import {
  RiskLevelColors,
  RulStatusColors,
  RulStatusLabels,
  DegradationTypeLabels
} from '../../types/prediction'

const { TabPane } = Tabs

const PredictionAlerts = () => {
  const [loading, setLoading] = useState(false)
  const [rulData, setRulData] = useState<RulPrediction[]>([])
  const [rulSummary, setRulSummary] = useState<any>(null)
  const [degradationData, setDegradationData] = useState<DeviceDegradationSummary[]>([])
  const [degradationSummary, setDegradationSummary] = useState<any>(null)
  const [trendAlerts, setTrendAlerts] = useState<TrendAlert[]>([])
  const [degradationAlerts, setDegradationAlerts] = useState<DegradationAlert[]>([])
  const [alertsSummary, setAlertsSummary] = useState<any>(null)

  // 加载所有数据
  const loadData = useCallback(async () => {
    setLoading(true)
    try {
      const [rulRes, degradationRes, alertsRes] = await Promise.all([
        getAllRulPredictions(),
        getAllDegradations(),
        getPredictionAlerts()
      ])

      if (rulRes.success) {
        setRulData(rulRes.data)
        setRulSummary(rulRes.summary)
      }

      if (degradationRes.success) {
        setDegradationData(degradationRes.data)
        setDegradationSummary(degradationRes.summary)
      }

      if (alertsRes.success) {
        setTrendAlerts(alertsRes.data.trendAlerts)
        setDegradationAlerts(alertsRes.data.degradationAlerts)
        setAlertsSummary(alertsRes.data.summary)
      }
    } catch (err) {
      console.error('Failed to load prediction data', err)
      message.error('加载预测数据失败')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    loadData()
    // 60秒自动刷新
    const timer = setInterval(loadData, 60000)
    return () => clearInterval(timer)
  }, [loadData])

  // RUL 表格列
  const rulColumns: ColumnsType<RulPrediction> = [
    {
      title: '设备ID',
      dataIndex: 'deviceId',
      key: 'deviceId',
      width: 150
    },
    {
      title: '健康指数',
      dataIndex: 'currentHealthIndex',
      key: 'currentHealthIndex',
      width: 100,
      sorter: (a, b) => a.currentHealthIndex - b.currentHealthIndex,
      render: (value: number) => (
        <Progress
          percent={value}
          size="small"
          status={value >= 80 ? 'success' : value >= 60 ? 'normal' : value >= 40 ? 'exception' : 'exception'}
          format={() => value}
        />
      )
    },
    {
      title: '剩余寿命',
      dataIndex: 'remainingUsefulLifeDays',
      key: 'remainingUsefulLifeDays',
      width: 120,
      sorter: (a, b) => (a.remainingUsefulLifeDays || 9999) - (b.remainingUsefulLifeDays || 9999),
      render: (days: number | null) =>
        days !== null ? (
          <span style={{ color: days < 30 ? '#f5222d' : days < 90 ? '#faad14' : '#52c41a' }}>
            {days.toFixed(0)} 天
          </span>
        ) : (
          <span style={{ color: '#8c8c8c' }}>-</span>
        )
    },
    {
      title: '状态',
      dataIndex: 'status',
      key: 'status',
      width: 120,
      filters: [
        { text: '健康', value: 'Healthy' },
        { text: '正常老化', value: 'NormalDegradation' },
        { text: '加速劣化', value: 'AcceleratedDegradation' },
        { text: '临近失效', value: 'NearFailure' },
        { text: '数据不足', value: 'InsufficientData' }
      ],
      onFilter: (value, record) => record.status === value,
      render: (status: RulStatus) => (
        <Tag color={RulStatusColors[status]}>{RulStatusLabels[status]}</Tag>
      )
    },
    {
      title: '风险等级',
      dataIndex: 'riskLevel',
      key: 'riskLevel',
      width: 100,
      filters: [
        { text: '低', value: 'low' },
        { text: '中', value: 'medium' },
        { text: '高', value: 'high' },
        { text: '严重', value: 'critical' }
      ],
      onFilter: (value, record) => record.riskLevel === value,
      render: (level: RulRiskLevel) => {
        const labels: Record<RulRiskLevel, string> = {
          low: '低',
          medium: '中',
          high: '高',
          critical: '严重'
        }
        return <Tag color={RiskLevelColors[level]}>{labels[level]}</Tag>
      }
    },
    {
      title: '劣化率',
      dataIndex: 'degradationRate',
      key: 'degradationRate',
      width: 100,
      render: (rate: number) => (
        <span style={{ color: rate > 1 ? '#f5222d' : rate > 0.5 ? '#faad14' : '#52c41a' }}>
          {rate.toFixed(2)} 点/天
        </span>
      )
    },
    {
      title: '诊断信息',
      dataIndex: 'diagnosticMessage',
      key: 'diagnosticMessage',
      ellipsis: true
    },
    {
      title: '建议维护时间',
      dataIndex: 'recommendedMaintenanceTime',
      key: 'recommendedMaintenanceTime',
      width: 160,
      render: (ts: number | null) =>
        ts ? new Date(ts).toLocaleDateString('zh-CN') : '-'
    }
  ]

  // 劣化检测表格列
  const degradationColumns: ColumnsType<DegradationResult & { deviceId: string }> = [
    {
      title: '设备ID',
      dataIndex: 'deviceId',
      key: 'deviceId',
      width: 150
    },
    {
      title: '标签',
      dataIndex: 'tagId',
      key: 'tagId',
      width: 150
    },
    {
      title: '劣化类型',
      dataIndex: 'degradationType',
      key: 'degradationType',
      width: 120,
      render: (type: string) => {
        const colors: Record<string, string> = {
          GradualIncrease: 'orange',
          GradualDecrease: 'blue',
          IncreasingVariance: 'purple'
        }
        return (
          <Tag color={colors[type] || 'default'}>
            {DegradationTypeLabels[type as keyof typeof DegradationTypeLabels] || type}
          </Tag>
        )
      }
    },
    {
      title: '变化率',
      dataIndex: 'degradationRate',
      key: 'degradationRate',
      width: 100,
      render: (rate: number) => (
        <span style={{ color: rate > 0 ? '#f5222d' : '#52c41a' }}>
          {rate > 0 ? <RiseOutlined /> : <FallOutlined />} {Math.abs(rate).toFixed(2)}%/天
        </span>
      )
    },
    {
      title: '累计变化',
      dataIndex: 'changePercent',
      key: 'changePercent',
      width: 100,
      render: (pct: number) => (
        <span style={{ color: Math.abs(pct) > 10 ? '#f5222d' : '#faad14' }}>
          {pct > 0 ? '+' : ''}{pct.toFixed(1)}%
        </span>
      )
    },
    {
      title: '描述',
      dataIndex: 'description',
      key: 'description',
      ellipsis: true
    }
  ]

  // 展开劣化数据
  const flatDegradationData = degradationData.flatMap((device) =>
    device.results.map((result) => ({
      ...result,
      deviceId: device.deviceId,
      key: `${device.deviceId}-${result.tagId}`
    }))
  )

  // 总预警数量
  const totalAlerts =
    (alertsSummary?.totalTrendAlerts || 0) + (alertsSummary?.totalDegradationAlerts || 0)
  const criticalDevices =
    (rulSummary?.riskDistribution?.critical || 0) + (rulSummary?.riskDistribution?.high || 0)

  return (
    <div style={{ padding: 24 }}>
      {/* 页面标题 */}
      <div
        style={{
          marginBottom: 24,
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center'
        }}
      >
        <h2 style={{ margin: 0 }}>预测预警中心</h2>
        <Button icon={<ReloadOutlined />} onClick={loadData} loading={loading}>
          刷新
        </Button>
      </div>

      {/* 关键预警提示 */}
      {criticalDevices > 0 && (
        <Alert
          message={`有 ${criticalDevices} 台设备需要立即关注`}
          description="检测到高风险或严重风险设备，建议尽快安排维护。"
          type="error"
          showIcon
          icon={<ExclamationCircleOutlined />}
          style={{ marginBottom: 24 }}
        />
      )}

      {/* 汇总统计卡片 */}
      <Spin spinning={loading}>
        <Row gutter={16} style={{ marginBottom: 24 }}>
          <Col span={6}>
            <Card>
              <Statistic
                title="监控设备"
                value={rulSummary?.totalDevices || 0}
                prefix={<DashboardOutlined />}
              />
            </Card>
          </Col>
          <Col span={6}>
            <Card>
              <Statistic
                title="平均剩余寿命"
                value={rulSummary?.averageRulDays?.toFixed(0) || '-'}
                suffix="天"
                prefix={<ClockCircleOutlined />}
                valueStyle={{
                  color:
                    (rulSummary?.averageRulDays || 365) < 30
                      ? '#f5222d'
                      : (rulSummary?.averageRulDays || 365) < 90
                      ? '#faad14'
                      : '#52c41a'
                }}
              />
            </Card>
          </Col>
          <Col span={6}>
            <Card>
              <Statistic
                title="劣化标签"
                value={degradationSummary?.totalDegradingTags || 0}
                prefix={<WarningOutlined style={{ color: '#faad14' }} />}
                valueStyle={{ color: '#faad14' }}
              />
            </Card>
          </Col>
          <Col span={6}>
            <Card>
              <Statistic
                title="高风险设备"
                value={criticalDevices}
                prefix={<ExclamationCircleOutlined style={{ color: '#f5222d' }} />}
                valueStyle={{ color: criticalDevices > 0 ? '#f5222d' : '#52c41a' }}
              />
            </Card>
          </Col>
        </Row>

        {/* 风险分布 */}
        <Row gutter={16} style={{ marginBottom: 24 }}>
          <Col span={12}>
            <Card title="设备风险分布">
              <Row gutter={16}>
                <Col span={6}>
                  <Statistic
                    title="严重"
                    value={rulSummary?.riskDistribution?.critical || 0}
                    valueStyle={{ color: '#f5222d' }}
                  />
                </Col>
                <Col span={6}>
                  <Statistic
                    title="高"
                    value={rulSummary?.riskDistribution?.high || 0}
                    valueStyle={{ color: '#ff7a45' }}
                  />
                </Col>
                <Col span={6}>
                  <Statistic
                    title="中"
                    value={rulSummary?.riskDistribution?.medium || 0}
                    valueStyle={{ color: '#faad14' }}
                  />
                </Col>
                <Col span={6}>
                  <Statistic
                    title="低"
                    value={rulSummary?.riskDistribution?.low || 0}
                    valueStyle={{ color: '#52c41a' }}
                  />
                </Col>
              </Row>
            </Card>
          </Col>
          <Col span={12}>
            <Card title="设备状态分布">
              <Row gutter={16}>
                <Col span={5}>
                  <Tooltip title="健康">
                    <Statistic
                      title={<CheckCircleOutlined style={{ color: '#52c41a' }} />}
                      value={rulSummary?.statusDistribution?.healthy || 0}
                    />
                  </Tooltip>
                </Col>
                <Col span={5}>
                  <Tooltip title="正常老化">
                    <Statistic
                      title={<ClockCircleOutlined style={{ color: '#faad14' }} />}
                      value={rulSummary?.statusDistribution?.normalDegradation || 0}
                    />
                  </Tooltip>
                </Col>
                <Col span={5}>
                  <Tooltip title="加速劣化">
                    <Statistic
                      title={<WarningOutlined style={{ color: '#ff7a45' }} />}
                      value={rulSummary?.statusDistribution?.acceleratedDegradation || 0}
                    />
                  </Tooltip>
                </Col>
                <Col span={5}>
                  <Tooltip title="临近失效">
                    <Statistic
                      title={<ExclamationCircleOutlined style={{ color: '#f5222d' }} />}
                      value={rulSummary?.statusDistribution?.nearFailure || 0}
                    />
                  </Tooltip>
                </Col>
                <Col span={4}>
                  <Tooltip title="数据不足">
                    <Statistic
                      title={<span style={{ color: '#8c8c8c' }}>?</span>}
                      value={rulSummary?.statusDistribution?.insufficientData || 0}
                    />
                  </Tooltip>
                </Col>
              </Row>
            </Card>
          </Col>
        </Row>

        {/* 详细数据标签页 */}
        <Card>
          <Tabs defaultActiveKey="rul">
            <TabPane tab={`RUL 预测 (${rulData.length})`} key="rul">
              <Table
                dataSource={rulData}
                columns={rulColumns}
                rowKey="deviceId"
                pagination={{ pageSize: 10 }}
                scroll={{ x: 1200 }}
              />
            </TabPane>
            <TabPane tab={`劣化检测 (${flatDegradationData.length})`} key="degradation">
              <Table
                dataSource={flatDegradationData}
                columns={degradationColumns}
                rowKey="key"
                pagination={{ pageSize: 10 }}
                scroll={{ x: 1000 }}
              />
            </TabPane>
            <TabPane tab={`预警汇总 (${totalAlerts})`} key="alerts">
              {trendAlerts.length === 0 && degradationAlerts.length === 0 ? (
                <Alert message="暂无预警" type="success" showIcon />
              ) : (
                <Space direction="vertical" style={{ width: '100%' }}>
                  {trendAlerts.map((alert, idx) => (
                    <Alert
                      key={`trend-${idx}`}
                      message={`[趋势预警] ${alert.deviceId} - ${alert.tagId}`}
                      description={alert.message}
                      type={
                        alert.levelCode >= 4
                          ? 'error'
                          : alert.levelCode >= 3
                          ? 'warning'
                          : 'info'
                      }
                      showIcon
                    />
                  ))}
                  {degradationAlerts.map((alert, idx) => (
                    <Alert
                      key={`deg-${idx}`}
                      message={`[劣化预警] ${alert.deviceId} - ${alert.tagId}`}
                      description={alert.description}
                      type="warning"
                      showIcon
                    />
                  ))}
                </Space>
              )}
            </TabPane>
          </Tabs>
        </Card>
      </Spin>
    </div>
  )
}

export default PredictionAlerts
