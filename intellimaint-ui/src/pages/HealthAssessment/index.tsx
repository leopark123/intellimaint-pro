import { useEffect, useState, useCallback } from 'react'
import {
  Card,
  Row,
  Col,
  Select,
  Button,
  Statistic,
  Table,
  Tag,
  Space,
  message,
  Spin,
  Empty,
  Tooltip
} from 'antd'
import {
  ReloadOutlined,
  HeartOutlined,
  WarningOutlined,
  ExclamationCircleOutlined,
  CloseCircleOutlined,
  BookOutlined,
  DeleteOutlined
} from '@ant-design/icons'
import {
  LineChart,
  Line,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip as RechartsTooltip,
  ResponsiveContainer,
  RadarChart,
  PolarGrid,
  PolarAngleAxis,
  PolarRadiusAxis,
  Radar
} from 'recharts'
import HealthGauge, { getColor, getLevel } from '../../components/HealthGauge'
import {
  HealthScore,
  HealthSnapshot,
  HealthSummary,
  getAllDevicesHealth,
  getDeviceHealth,
  getDeviceHealthHistory,
  getHealthSummary,
  learnBaseline,
  deleteBaseline,
  getDeviceBaseline
} from '../../api/healthAssessment'
import { getDevices } from '../../api/device'

const HealthAssessment = () => {
  const [loading, setLoading] = useState(false)
  const [devices, setDevices] = useState<{ id: string; name: string }[]>([])
  const [selectedDevice, setSelectedDevice] = useState<string | null>(null)
  const [summary, setSummary] = useState<HealthSummary | null>(null)
  const [currentScore, setCurrentScore] = useState<HealthScore | null>(null)
  const [historyData, setHistoryData] = useState<HealthSnapshot[]>([])
  const [hasBaseline, setHasBaseline] = useState(false)
  const [allScores, setAllScores] = useState<HealthScore[]>([])

  // 加载设备列表
  const loadDevices = useCallback(async () => {
    try {
      const deviceData = await getDevices()
      const deviceList = deviceData.map((d) => ({
        id: d.deviceId,
        name: d.name || d.deviceId
      }))
      setDevices(deviceList)
      if (deviceList.length > 0 && !selectedDevice) {
        setSelectedDevice(deviceList[0].id)
      }
    } catch (err) {
      console.error('Failed to load devices', err)
    }
  }, [selectedDevice])

  // 加载汇总数据
  const loadSummary = useCallback(async () => {
    try {
      const res = await getHealthSummary()
      if (res.success) {
        setSummary(res.data)
      }
    } catch (err) {
      console.error('Failed to load summary', err)
    }
  }, [])

  // 加载所有设备评分
  const loadAllScores = useCallback(async () => {
    try {
      const res = await getAllDevicesHealth()
      if (res.success) {
        setAllScores(res.data)
      }
    } catch (err) {
      console.error('Failed to load all scores', err)
    }
  }, [])

  // 加载选中设备的详细数据
  const loadDeviceDetails = useCallback(async (deviceId: string) => {
    if (!deviceId) return

    setLoading(true)
    try {
      // 并行加载健康评分、历史数据和基线状态
      const [scoreRes, historyRes, baselineRes] = await Promise.all([
        getDeviceHealth(deviceId),
        getDeviceHealthHistory(deviceId),
        getDeviceBaseline(deviceId).catch(() => ({ success: false }))
      ])

      if (scoreRes.success) {
        setCurrentScore(scoreRes.data)
      }

      if (historyRes.success) {
        setHistoryData(historyRes.data.snapshots || [])
      }

      setHasBaseline(baselineRes.success)
    } catch (err) {
      console.error('Failed to load device details', err)
    } finally {
      setLoading(false)
    }
  }, [])

  // 刷新数据
  const handleRefresh = useCallback(async () => {
    setLoading(true)
    try {
      await Promise.all([loadSummary(), loadAllScores()])
      if (selectedDevice) {
        await loadDeviceDetails(selectedDevice)
      }
      message.success('数据已刷新')
    } finally {
      setLoading(false)
    }
  }, [loadSummary, loadAllScores, loadDeviceDetails, selectedDevice])

  // 学习基线
  const handleLearnBaseline = async () => {
    if (!selectedDevice) return

    try {
      const res = await learnBaseline(selectedDevice)
      if (res.success) {
        message.success(res.message || '基线学习成功')
        setHasBaseline(true)
        await loadDeviceDetails(selectedDevice)
      } else {
        message.error(res.error || '基线学习失败')
      }
    } catch (err) {
      message.error('基线学习失败')
    }
  }

  // 删除基线
  const handleDeleteBaseline = async () => {
    if (!selectedDevice) return

    try {
      const res = await deleteBaseline(selectedDevice)
      if (res.success) {
        message.success('基线已删除')
        setHasBaseline(false)
        await loadDeviceDetails(selectedDevice)
      }
    } catch (err) {
      message.error('删除基线失败')
    }
  }

  // 初始加载
  useEffect(() => {
    loadDevices()
    loadSummary()
    loadAllScores()
  }, [])

  // 设备切换时加载详情
  useEffect(() => {
    if (selectedDevice) {
      loadDeviceDetails(selectedDevice)
    }
  }, [selectedDevice, loadDeviceDetails])

  // 60秒自动刷新
  useEffect(() => {
    const timer = setInterval(() => {
      loadSummary()
      loadAllScores()
      if (selectedDevice) {
        loadDeviceDetails(selectedDevice)
      }
    }, 60000)
    return () => clearInterval(timer)
  }, [selectedDevice, loadSummary, loadAllScores, loadDeviceDetails])

  // 雷达图数据
  const radarData = currentScore
    ? [
        { dimension: '偏差', score: currentScore.deviationScore, fullMark: 100 },
        { dimension: '趋势', score: currentScore.trendScore, fullMark: 100 },
        { dimension: '稳定性', score: currentScore.stabilityScore, fullMark: 100 },
        { dimension: '告警', score: currentScore.alarmScore, fullMark: 100 }
      ]
    : []

  // 历史趋势图数据
  const chartData = historyData.map((s) => ({
    time: new Date(s.timestamp).toLocaleTimeString('zh-CN', {
      hour: '2-digit',
      minute: '2-digit'
    }),
    index: s.index
  }))

  // 设备列表表格列
  const columns = [
    {
      title: '设备',
      dataIndex: 'deviceId',
      key: 'deviceId',
      render: (id: string) => {
        const device = devices.find((d) => d.id === id)
        return device?.name || id
      }
    },
    {
      title: '健康指数',
      dataIndex: 'index',
      key: 'index',
      sorter: (a: HealthScore, b: HealthScore) => a.index - b.index,
      render: (index: number) => (
        <span style={{ color: getColor(index), fontWeight: 'bold' }}>{index}</span>
      )
    },
    {
      title: '状态',
      dataIndex: 'level',
      key: 'level',
      render: (level: string) => {
        const levelMap: Record<string, { color: string; text: string }> = {
          healthy: { color: 'success', text: '健康' },
          attention: { color: 'warning', text: '注意' },
          warning: { color: 'orange', text: '警告' },
          critical: { color: 'error', text: '危险' }
        }
        const info = levelMap[level] || { color: 'default', text: level }
        return <Tag color={info.color}>{info.text}</Tag>
      }
    },
    {
      title: '操作',
      key: 'action',
      render: (_: any, record: HealthScore) => (
        <Button type="link" size="small" onClick={() => setSelectedDevice(record.deviceId)}>
          查看详情
        </Button>
      )
    }
  ]

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
        <h2 style={{ margin: 0 }}>健康评估中心</h2>
        <Button icon={<ReloadOutlined />} onClick={handleRefresh} loading={loading}>
          刷新
        </Button>
      </div>

      {/* 汇总统计卡片 */}
      <Row gutter={16} style={{ marginBottom: 24 }}>
        <Col span={6}>
          <Card>
            <Statistic
              title="平均健康指数"
              value={summary?.avgHealthIndex || 0}
              suffix="/100"
              valueStyle={{ color: getColor(summary?.avgHealthIndex || 0) }}
            />
          </Card>
        </Col>
        <Col span={6}>
          <Card>
            <Statistic
              title="健康设备"
              value={summary?.distribution.healthy || 0}
              prefix={<HeartOutlined style={{ color: '#52c41a' }} />}
              valueStyle={{ color: '#52c41a' }}
            />
          </Card>
        </Col>
        <Col span={6}>
          <Card>
            <Statistic
              title="需要关注"
              value={(summary?.distribution.attention || 0) + (summary?.distribution.warning || 0)}
              prefix={<WarningOutlined style={{ color: '#faad14' }} />}
              valueStyle={{ color: '#faad14' }}
            />
          </Card>
        </Col>
        <Col span={6}>
          <Card>
            <Statistic
              title="危险设备"
              value={summary?.distribution.critical || 0}
              prefix={<CloseCircleOutlined style={{ color: '#f5222d' }} />}
              valueStyle={{ color: '#f5222d' }}
            />
          </Card>
        </Col>
      </Row>

      {/* 设备选择器 */}
      <Card style={{ marginBottom: 24 }}>
        <Space>
          <span>选择设备：</span>
          <Select
            style={{ width: 300 }}
            placeholder="选择要查看的设备"
            value={selectedDevice}
            onChange={setSelectedDevice}
            options={devices.map((d) => ({ value: d.id, label: d.name }))}
          />
          {selectedDevice && (
            <>
              <Tooltip title={hasBaseline ? '已有基线' : '学习设备正常运行基线'}>
                <Button
                  icon={<BookOutlined />}
                  onClick={handleLearnBaseline}
                  disabled={hasBaseline}
                >
                  学习基线
                </Button>
              </Tooltip>
              {hasBaseline && (
                <Button icon={<DeleteOutlined />} danger onClick={handleDeleteBaseline}>
                  删除基线
                </Button>
              )}
            </>
          )}
        </Space>
      </Card>

      {/* 设备详情 */}
      {selectedDevice && (
        <Spin spinning={loading}>
          {currentScore ? (
            <Row gutter={24} style={{ marginBottom: 24 }}>
              {/* 健康仪表盘 */}
              <Col span={8}>
                <Card title="健康仪表盘" style={{ textAlign: 'center' }}>
                  <HealthGauge value={currentScore.index} size={200} />
                  {currentScore.diagnosticMessage && (
                    <div style={{ marginTop: 16, color: '#8c8c8c' }}>
                      {currentScore.diagnosticMessage}
                    </div>
                  )}
                </Card>
              </Col>

              {/* 四维评分雷达图 */}
              <Col span={8}>
                <Card title="四维评分">
                  <ResponsiveContainer width="100%" height={240}>
                    <RadarChart data={radarData}>
                      <PolarGrid />
                      <PolarAngleAxis dataKey="dimension" />
                      <PolarRadiusAxis angle={30} domain={[0, 100]} />
                      <Radar
                        name="评分"
                        dataKey="score"
                        stroke={getColor(currentScore.index)}
                        fill={getColor(currentScore.index)}
                        fillOpacity={0.3}
                      />
                    </RadarChart>
                  </ResponsiveContainer>
                </Card>
              </Col>

              {/* 详细评分 */}
              <Col span={8}>
                <Card title="评分详情">
                  <div style={{ marginBottom: 12 }}>
                    <span>偏差评分：</span>
                    <span style={{ float: 'right', fontWeight: 'bold' }}>
                      {currentScore.deviationScore}
                    </span>
                    <div
                      style={{
                        background: '#f0f0f0',
                        height: 8,
                        borderRadius: 4,
                        marginTop: 4
                      }}
                    >
                      <div
                        style={{
                          background: getColor(currentScore.deviationScore),
                          height: '100%',
                          width: `${currentScore.deviationScore}%`,
                          borderRadius: 4
                        }}
                      />
                    </div>
                  </div>
                  <div style={{ marginBottom: 12 }}>
                    <span>趋势评分：</span>
                    <span style={{ float: 'right', fontWeight: 'bold' }}>
                      {currentScore.trendScore}
                    </span>
                    <div
                      style={{
                        background: '#f0f0f0',
                        height: 8,
                        borderRadius: 4,
                        marginTop: 4
                      }}
                    >
                      <div
                        style={{
                          background: getColor(currentScore.trendScore),
                          height: '100%',
                          width: `${currentScore.trendScore}%`,
                          borderRadius: 4
                        }}
                      />
                    </div>
                  </div>
                  <div style={{ marginBottom: 12 }}>
                    <span>稳定性评分：</span>
                    <span style={{ float: 'right', fontWeight: 'bold' }}>
                      {currentScore.stabilityScore}
                    </span>
                    <div
                      style={{
                        background: '#f0f0f0',
                        height: 8,
                        borderRadius: 4,
                        marginTop: 4
                      }}
                    >
                      <div
                        style={{
                          background: getColor(currentScore.stabilityScore),
                          height: '100%',
                          width: `${currentScore.stabilityScore}%`,
                          borderRadius: 4
                        }}
                      />
                    </div>
                  </div>
                  <div>
                    <span>告警评分：</span>
                    <span style={{ float: 'right', fontWeight: 'bold' }}>
                      {currentScore.alarmScore}
                    </span>
                    <div
                      style={{
                        background: '#f0f0f0',
                        height: 8,
                        borderRadius: 4,
                        marginTop: 4
                      }}
                    >
                      <div
                        style={{
                          background: getColor(currentScore.alarmScore),
                          height: '100%',
                          width: `${currentScore.alarmScore}%`,
                          borderRadius: 4
                        }}
                      />
                    </div>
                  </div>
                  {currentScore.problemTags?.length > 0 && (
                    <div style={{ marginTop: 16 }}>
                      <span style={{ color: '#f5222d' }}>问题标签：</span>
                      <div style={{ marginTop: 8 }}>
                        {currentScore.problemTags.map((tag) => (
                          <Tag key={tag} color="error">
                            {tag}
                          </Tag>
                        ))}
                      </div>
                    </div>
                  )}
                </Card>
              </Col>
            </Row>
          ) : (
            <Card style={{ marginBottom: 24 }}>
              <Empty description="暂无健康数据" />
            </Card>
          )}

          {/* 历史趋势图 */}
          {chartData.length > 0 && (
            <Card title="健康指数趋势 (24小时)" style={{ marginBottom: 24 }}>
              <ResponsiveContainer width="100%" height={300}>
                <LineChart data={chartData}>
                  <CartesianGrid strokeDasharray="3 3" />
                  <XAxis dataKey="time" />
                  <YAxis domain={[0, 100]} />
                  <RechartsTooltip />
                  <Line
                    type="monotone"
                    dataKey="index"
                    stroke="#1890ff"
                    strokeWidth={2}
                    dot={false}
                  />
                </LineChart>
              </ResponsiveContainer>
            </Card>
          )}
        </Spin>
      )}

      {/* 设备健康列表 */}
      <Card title="设备健康概览">
        <Table
          dataSource={allScores}
          columns={columns}
          rowKey="deviceId"
          pagination={{ pageSize: 10 }}
          loading={loading}
          locale={{ emptyText: '暂无设备健康数据' }}
        />
      </Card>
    </div>
  )
}

export default HealthAssessment
