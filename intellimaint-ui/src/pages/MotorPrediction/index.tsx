// v64: 电机故障预测页面

import { useEffect, useState, useCallback } from 'react'
import {
  Card,
  Row,
  Col,
  Table,
  Tag,
  Button,
  Space,
  Select,
  Progress,
  Statistic,
  message,
  Spin,
  Empty,
  Modal,
  Descriptions,
  List,
  Tooltip,
  Badge,
  Divider,
  Alert,
} from 'antd'
import {
  ReloadOutlined,
  ThunderboltOutlined,
  SettingOutlined,
  PlayCircleOutlined,
  CheckCircleOutlined,
  WarningOutlined,
  CloseCircleOutlined,
  ExclamationCircleOutlined,
  InfoCircleOutlined,
  RobotOutlined,
  PlusOutlined,
  ApiOutlined,
  DatabaseOutlined,
} from '@ant-design/icons'
import {
  RadarChart,
  PolarGrid,
  PolarAngleAxis,
  PolarRadiusAxis,
  Radar,
  ResponsiveContainer,
  AreaChart,
  Area,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip as RechartsTooltip,
} from 'recharts'
import {
  getMotorInstances,
  getAllDiagnoses,
  diagnoseMotor,
  getBaselineLearningStatus,
  learnAllModes,
} from '../../api/motor'
import type {
  MotorInstance,
  MotorDiagnosisResult,
  MotorLearningTaskState,
  DetectedFault,
  ParameterDeviation,
} from '../../types/motor'
import {
  FaultSeverity,
  SeverityNames,
  SeverityColors,
  FaultTypeNames,
  MotorFaultType,
  MotorLearningStatus,
} from '../../types/motor'

// 健康评分颜色
const getHealthColor = (score: number): string => {
  if (score >= 90) return '#52c41a'
  if (score >= 70) return '#faad14'
  if (score >= 50) return '#fa8c16'
  return '#f5222d'
}

// 健康评分等级
const getHealthLevel = (score: number): string => {
  if (score >= 90) return '优秀'
  if (score >= 70) return '良好'
  if (score >= 50) return '注意'
  return '警告'
}

// 严重程度图标
const getSeverityIcon = (severity: FaultSeverity) => {
  switch (severity) {
    case FaultSeverity.Normal:
      return <CheckCircleOutlined style={{ color: SeverityColors[severity] }} />
    case FaultSeverity.Minor:
      return <InfoCircleOutlined style={{ color: SeverityColors[severity] }} />
    case FaultSeverity.Moderate:
      return <WarningOutlined style={{ color: SeverityColors[severity] }} />
    case FaultSeverity.Severe:
      return <ExclamationCircleOutlined style={{ color: SeverityColors[severity] }} />
    case FaultSeverity.Critical:
      return <CloseCircleOutlined style={{ color: SeverityColors[severity] }} />
    default:
      return <InfoCircleOutlined />
  }
}

// 学习状态名称
const LearningStatusNames: Record<MotorLearningStatus, string> = {
  [MotorLearningStatus.Pending]: '等待中',
  [MotorLearningStatus.Running]: '学习中',
  [MotorLearningStatus.Completed]: '已完成',
  [MotorLearningStatus.Failed]: '失败',
}

// 学习状态颜色
const LearningStatusColors: Record<MotorLearningStatus, string> = {
  [MotorLearningStatus.Pending]: 'default',
  [MotorLearningStatus.Running]: 'processing',
  [MotorLearningStatus.Completed]: 'success',
  [MotorLearningStatus.Failed]: 'error',
}

const MotorPrediction = () => {
  const [loading, setLoading] = useState(false)
  const [instances, setInstances] = useState<MotorInstance[]>([])
  const [diagnoses, setDiagnoses] = useState<MotorDiagnosisResult[]>([])
  const [selectedInstance, setSelectedInstance] = useState<string | null>(null)
  const [selectedDiagnosis, setSelectedDiagnosis] = useState<MotorDiagnosisResult | null>(null)
  const [learningTasks, setLearningTasks] = useState<MotorLearningTaskState[]>([])
  const [detailModalVisible, setDetailModalVisible] = useState(false)
  const [diagnosing, setDiagnosing] = useState(false)

  // 加载数据
  const loadData = useCallback(async () => {
    setLoading(true)
    try {
      const [instancesRes, diagnosesRes] = await Promise.all([
        getMotorInstances(),
        getAllDiagnoses(),
      ])

      if (instancesRes.success) {
        setInstances(instancesRes.data)
        if (instancesRes.data.length > 0 && !selectedInstance) {
          setSelectedInstance(instancesRes.data[0].instanceId)
        }
      }

      if (diagnosesRes.success) {
        setDiagnoses(diagnosesRes.data)
      }
    } catch (err) {
      console.error('Failed to load data', err)
      message.error('加载数据失败')
    } finally {
      setLoading(false)
    }
  }, [selectedInstance])

  // 加载学习任务状态
  const loadLearningTasks = useCallback(async (instanceId: string) => {
    try {
      const res = await getBaselineLearningStatus(instanceId)
      if (res.success) {
        setLearningTasks(res.data)
      }
    } catch (err) {
      console.error('Failed to load learning tasks', err)
    }
  }, [])

  // 执行诊断
  const handleDiagnose = async (instanceId: string) => {
    setDiagnosing(true)
    try {
      const res = await diagnoseMotor(instanceId)
      if (res.success) {
        message.success('诊断完成')
        await loadData()
        // 更新选中的诊断结果
        const updatedDiagnosis = diagnoses.find((d) => d.instanceId === instanceId)
        if (updatedDiagnosis) {
          setSelectedDiagnosis(res.data)
        }
      } else {
        message.error(res.error || '诊断失败')
      }
    } catch (err) {
      message.error('诊断失败')
    } finally {
      setDiagnosing(false)
    }
  }

  // 开始基线学习（所有模式）
  const handleStartLearning = async (instanceId: string) => {
    try {
      const res = await learnAllModes(instanceId)
      if (res.success) {
        message.success(res.data?.message || '基线学习已启动')
        await loadLearningTasks(instanceId)
      } else {
        message.error(res.error || '启动失败，请检查电机配置')
      }
    } catch (err: any) {
      const errorMsg = err?.response?.data?.error || err?.message || '网络错误'
      message.error(`启动基线学习失败: ${errorMsg}`)
      console.error('Learn baseline error:', err)
    }
  }

  // 查看详情
  const handleViewDetail = (diagnosis: MotorDiagnosisResult) => {
    setSelectedDiagnosis(diagnosis)
    setDetailModalVisible(true)
  }

  // 初始加载
  useEffect(() => {
    loadData()
  }, [])

  // 选中实例变化时加载学习任务状态
  useEffect(() => {
    if (selectedInstance) {
      loadLearningTasks(selectedInstance)
    }
  }, [selectedInstance, loadLearningTasks])

  // 定时刷新
  useEffect(() => {
    const timer = setInterval(loadData, 60000)
    return () => clearInterval(timer)
  }, [loadData])

  // 获取实例的诊断结果
  const getDiagnosisForInstance = (instanceId: string) => {
    return diagnoses.find((d) => d.instanceId === instanceId)
  }

  // 统计数据
  const stats = {
    total: instances.length,
    healthy: diagnoses.filter((d) => d.healthScore >= 90).length,
    attention: diagnoses.filter((d) => d.healthScore >= 70 && d.healthScore < 90).length,
    warning: diagnoses.filter((d) => d.healthScore < 70).length,
    avgHealth: diagnoses.length > 0
      ? Math.round(diagnoses.reduce((sum, d) => sum + d.healthScore, 0) / diagnoses.length)
      : 0,
  }

  // 雷达图数据（偏差分布）
  const getRadarData = (diagnosis: MotorDiagnosisResult) => {
    if (!diagnosis.deviations || diagnosis.deviations.length === 0) return []

    // 按参数类型分组
    const groups = {
      '电流': diagnosis.deviations.filter((d) => d.parameterName.includes('电流')),
      '电压': diagnosis.deviations.filter((d) => d.parameterName.includes('电压')),
      '温度': diagnosis.deviations.filter((d) => d.parameterName.includes('温度')),
      '振动': diagnosis.deviations.filter((d) => d.parameterName.includes('振动')),
      '功率': diagnosis.deviations.filter((d) => d.parameterName.includes('功率') || d.parameterName.includes('因数')),
    }

    return Object.entries(groups)
      .filter(([, items]) => items.length > 0)
      .map(([name, items]) => ({
        dimension: name,
        deviation: Math.max(...items.map((i) => Math.abs(i.deviationSigma))),
        fullMark: 5,
      }))
  }

  // 表格列定义
  const columns = [
    {
      title: '电机名称',
      dataIndex: 'name',
      key: 'name',
      render: (name: string, record: MotorInstance) => (
        <Space>
          <ThunderboltOutlined style={{ color: '#1890ff' }} />
          <span style={{ fontWeight: 500 }}>{name}</span>
        </Space>
      ),
    },
    {
      title: '位置',
      dataIndex: 'location',
      key: 'location',
      render: (location: string | null) => location || '-',
    },
    {
      title: '健康评分',
      key: 'healthScore',
      width: 180,
      render: (_: any, record: MotorInstance) => {
        const diagnosis = getDiagnosisForInstance(record.instanceId)
        if (!diagnosis) {
          return <Tag>暂无数据</Tag>
        }
        return (
          <Space>
            <Progress
              type="circle"
              percent={diagnosis.healthScore}
              width={40}
              strokeColor={getHealthColor(diagnosis.healthScore)}
              format={(percent) => percent}
            />
            <Tag color={getHealthColor(diagnosis.healthScore)}>
              {getHealthLevel(diagnosis.healthScore)}
            </Tag>
          </Space>
        )
      },
    },
    {
      title: '故障数',
      key: 'faults',
      width: 100,
      render: (_: any, record: MotorInstance) => {
        const diagnosis = getDiagnosisForInstance(record.instanceId)
        if (!diagnosis) return '-'
        const count = diagnosis.faults.length
        return (
          <Badge
            count={count}
            showZero
            style={{ backgroundColor: count > 0 ? '#f5222d' : '#52c41a' }}
          />
        )
      },
    },
    {
      title: '状态',
      key: 'status',
      width: 100,
      render: (_: any, record: MotorInstance) => {
        const diagnosis = getDiagnosisForInstance(record.instanceId)
        if (!diagnosis) {
          return <Tag>未诊断</Tag>
        }
        return (
          <Space>
            {getSeverityIcon(diagnosis.overallSeverity)}
            <span>{SeverityNames[diagnosis.overallSeverity]}</span>
          </Space>
        )
      },
    },
    {
      title: '诊断时间',
      key: 'timestamp',
      width: 160,
      render: (_: any, record: MotorInstance) => {
        const diagnosis = getDiagnosisForInstance(record.instanceId)
        if (!diagnosis) return '-'
        return new Date(diagnosis.timestamp).toLocaleString('zh-CN')
      },
    },
    {
      title: '操作',
      key: 'action',
      width: 180,
      render: (_: any, record: MotorInstance) => {
        const diagnosis = getDiagnosisForInstance(record.instanceId)
        return (
          <Space>
            <Tooltip title="执行诊断">
              <Button
                type="primary"
                size="small"
                icon={<PlayCircleOutlined />}
                loading={diagnosing}
                onClick={() => handleDiagnose(record.instanceId)}
              >
                诊断
              </Button>
            </Tooltip>
            {diagnosis && (
              <Button size="small" onClick={() => handleViewDetail(diagnosis)}>
                详情
              </Button>
            )}
          </Space>
        )
      },
    },
  ]

  // 故障列表列
  const faultColumns = [
    {
      title: '故障类型',
      dataIndex: 'faultTypeName',
      key: 'faultTypeName',
      render: (name: string, record: DetectedFault) => (
        <Space>
          {getSeverityIcon(record.severity)}
          <span>{name || FaultTypeNames[record.faultType]}</span>
        </Space>
      ),
    },
    {
      title: '严重程度',
      dataIndex: 'severity',
      key: 'severity',
      render: (severity: FaultSeverity) => (
        <Tag color={SeverityColors[severity]}>{SeverityNames[severity]}</Tag>
      ),
    },
    {
      title: '置信度',
      dataIndex: 'confidence',
      key: 'confidence',
      render: (confidence: number) => (
        <Progress percent={Math.round(confidence * 100)} size="small" />
      ),
    },
    {
      title: '描述',
      dataIndex: 'description',
      key: 'description',
      ellipsis: true,
    },
  ]

  return (
    <div style={{ padding: 24 }}>
      {/* 页面标题 */}
      <div
        style={{
          marginBottom: 24,
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
        }}
      >
        <div>
          <h2 style={{ margin: 0, display: 'flex', alignItems: 'center', gap: 8 }}>
            <RobotOutlined style={{ color: '#1890ff' }} />
            电机故障预测
          </h2>
          <p style={{ margin: '8px 0 0', color: 'var(--color-text-muted)' }}>
            基于机器学习的电机健康状态监测与故障预测
          </p>
        </div>
        <Space>
          <Button icon={<ReloadOutlined />} onClick={loadData} loading={loading}>
            刷新
          </Button>
          <Button
            icon={<SettingOutlined />}
            onClick={() => window.open('/motor-config', '_blank')}
          >
            电机配置
          </Button>
        </Space>
      </div>

      {/* 统计卡片 */}
      <Row gutter={16} style={{ marginBottom: 24 }}>
        <Col span={6}>
          <Card>
            <Statistic
              title="电机总数"
              value={stats.total}
              prefix={<ThunderboltOutlined style={{ color: '#1890ff' }} />}
            />
          </Card>
        </Col>
        <Col span={6}>
          <Card>
            <Statistic
              title="健康电机"
              value={stats.healthy}
              valueStyle={{ color: '#52c41a' }}
              prefix={<CheckCircleOutlined />}
            />
          </Card>
        </Col>
        <Col span={6}>
          <Card>
            <Statistic
              title="需要关注"
              value={stats.attention + stats.warning}
              valueStyle={{ color: stats.warning > 0 ? '#f5222d' : '#faad14' }}
              prefix={<WarningOutlined />}
            />
          </Card>
        </Col>
        <Col span={6}>
          <Card>
            <Statistic
              title="平均健康评分"
              value={stats.avgHealth}
              suffix="/100"
              valueStyle={{ color: getHealthColor(stats.avgHealth) }}
            />
          </Card>
        </Col>
      </Row>

      {/* 电机状态表格 - 全宽 */}
      <Card title="电机状态概览" style={{ marginBottom: 24 }}>
        <Spin spinning={loading}>
          <Table
            dataSource={instances}
            columns={columns}
            rowKey="instanceId"
            pagination={{ pageSize: 10 }}
            scroll={{ x: 900 }}
            locale={{
              emptyText: (
                <Empty
                  image={Empty.PRESENTED_IMAGE_SIMPLE}
                  description={
                    <Space direction="vertical">
                      <span>暂无电机实例</span>
                      <span style={{ fontSize: 12, color: 'var(--color-text-muted)' }}>
                        请先在电机配置页面添加电机模型和实例
                      </span>
                    </Space>
                  }
                >
                  <Button
                    type="primary"
                    icon={<PlusOutlined />}
                    onClick={() => window.open('/motor-config', '_blank')}
                  >
                    配置电机
                  </Button>
                </Empty>
              ),
            }}
            onRow={(record) => ({
              onClick: () => setSelectedInstance(record.instanceId),
              style: {
                cursor: 'pointer',
                backgroundColor:
                  selectedInstance === record.instanceId
                    ? 'rgba(24, 144, 255, 0.1)'
                    : undefined,
              },
            })}
          />
        </Spin>
      </Card>

      {/* 选中电机的详情区域 - 三列布局 */}
      {selectedInstance && (
        <Row gutter={16}>
          {/* 左列 - 基线学习状态 */}
          <Col xs={24} md={8}>
            <Card
              title="基线学习状态"
              style={{ marginBottom: 16 }}
              extra={
                <Button
                  type="link"
                  size="small"
                  icon={<SettingOutlined />}
                  onClick={() => handleStartLearning(selectedInstance)}
                >
                  学习基线
                </Button>
              }
            >
              {learningTasks.length > 0 ? (
                <List
                  size="small"
                  dataSource={learningTasks}
                  renderItem={(task) => (
                    <List.Item>
                      <List.Item.Meta
                        title={
                          <Space>
                            <span>{task.progress || `任务 ${task.taskId.slice(0, 8)}`}</span>
                            <Tag color={LearningStatusColors[task.status]}>
                              {LearningStatusNames[task.status]}
                            </Tag>
                          </Space>
                        }
                        description={
                          <Space direction="vertical" size={4} style={{ width: '100%' }}>
                            {task.status === MotorLearningStatus.Running && (
                              <Progress percent={50} size="small" status="active" />
                            )}
                            {task.message && (
                              <span style={{ fontSize: 12, color: 'var(--color-text-muted)' }}>
                                {task.message}
                              </span>
                            )}
                            <span style={{ fontSize: 11, color: 'var(--color-text-muted)' }}>
                              开始时间: {new Date(task.startTime).toLocaleString()}
                            </span>
                          </Space>
                        }
                      />
                    </List.Item>
                  )}
                />
              ) : (
                <Empty description="暂无学习任务" image={Empty.PRESENTED_IMAGE_SIMPLE} />
              )}
            </Card>
          </Col>

          {/* 中列 - 健康评分与雷达图 */}
          <Col xs={24} md={8}>
            {(() => {
              const diagnosis = getDiagnosisForInstance(selectedInstance)
              if (!diagnosis) {
                return (
                  <Card title="诊断概要" style={{ marginBottom: 16 }}>
                    <Empty description="暂无诊断数据" />
                  </Card>
                )
              }

              const radarData = getRadarData(diagnosis)

              return (
                <Card title="诊断概要" style={{ marginBottom: 16 }}>
                  {/* 健康评分 */}
                  <div style={{ textAlign: 'center', marginBottom: 16 }}>
                    <Progress
                      type="dashboard"
                      percent={diagnosis.healthScore}
                      width={100}
                      strokeColor={getHealthColor(diagnosis.healthScore)}
                      format={(percent) => (
                        <div>
                          <div style={{ fontSize: 20, fontWeight: 'bold' }}>{percent}</div>
                          <div style={{ fontSize: 11, color: 'var(--color-text-muted)' }}>
                            健康评分
                          </div>
                        </div>
                      )}
                    />
                  </div>

                  {/* 摘要 */}
                  {diagnosis.summary && (
                    <Alert
                      message={diagnosis.summary}
                      type={
                        diagnosis.healthScore >= 90
                          ? 'success'
                          : diagnosis.healthScore >= 70
                          ? 'warning'
                          : 'error'
                      }
                      showIcon
                      style={{ marginBottom: 16 }}
                    />
                  )}

                  {/* 偏差雷达图 */}
                  {radarData.length > 0 && (
                    <ResponsiveContainer width="100%" height={180}>
                      <RadarChart data={radarData}>
                        <PolarGrid />
                        <PolarAngleAxis dataKey="dimension" tick={{ fontSize: 11 }} />
                        <PolarRadiusAxis angle={30} domain={[0, 5]} tick={{ fontSize: 10 }} />
                        <Radar
                          name="偏差(σ)"
                          dataKey="deviation"
                          stroke="#1890ff"
                          fill="#1890ff"
                          fillOpacity={0.3}
                        />
                      </RadarChart>
                    </ResponsiveContainer>
                  )}
                </Card>
              )
            })()}
          </Col>

          {/* 右列 - 故障列表与建议 */}
          <Col xs={24} md={8}>
            {(() => {
              const diagnosis = getDiagnosisForInstance(selectedInstance)
              if (!diagnosis) {
                return (
                  <Card title="故障与建议" style={{ marginBottom: 16 }}>
                    <Empty description="暂无数据" />
                  </Card>
                )
              }

              return (
                <Card
                  title="故障与建议"
                  style={{ marginBottom: 16 }}
                  extra={
                    <Button size="small" onClick={() => handleViewDetail(diagnosis)}>
                      详情
                    </Button>
                  }
                >
                  {/* 故障列表 */}
                  {diagnosis.faults.length > 0 ? (
                    <>
                      <div style={{ marginBottom: 8, fontWeight: 500, fontSize: 13 }}>
                        检测到的故障 ({diagnosis.faults.length})
                      </div>
                      <List
                        size="small"
                        dataSource={diagnosis.faults.slice(0, 3)}
                        renderItem={(fault) => (
                          <List.Item style={{ padding: '6px 0' }}>
                            <Space size={4}>
                              {getSeverityIcon(fault.severity)}
                              <span style={{ fontSize: 13 }}>
                                {fault.faultTypeName || FaultTypeNames[fault.faultType]}
                              </span>
                            </Space>
                            <Tag color={SeverityColors[fault.severity]} style={{ marginLeft: 'auto' }}>
                              {SeverityNames[fault.severity]}
                            </Tag>
                          </List.Item>
                        )}
                      />
                      {diagnosis.faults.length > 3 && (
                        <div style={{ textAlign: 'center', marginTop: 4 }}>
                          <Button type="link" size="small" onClick={() => handleViewDetail(diagnosis)}>
                            查看全部 {diagnosis.faults.length} 个故障
                          </Button>
                        </div>
                      )}
                    </>
                  ) : (
                    <div style={{ color: '#52c41a', marginBottom: 12 }}>
                      <CheckCircleOutlined /> 未检测到故障
                    </div>
                  )}

                  {/* 建议 */}
                  {diagnosis.recommendations.length > 0 && (
                    <>
                      <Divider style={{ margin: '12px 0' }} />
                      <div style={{ marginBottom: 8, fontWeight: 500, fontSize: 13 }}>
                        维护建议
                      </div>
                      <List
                        size="small"
                        dataSource={diagnosis.recommendations.slice(0, 2)}
                        renderItem={(rec, index) => (
                          <List.Item style={{ padding: '4px 0' }}>
                            <Space size={4}>
                              <Tag color="blue" style={{ marginRight: 4 }}>{index + 1}</Tag>
                              <span style={{ fontSize: 12 }}>{rec}</span>
                            </Space>
                          </List.Item>
                        )}
                      />
                    </>
                  )}
                </Card>
              )
            })()}
          </Col>
        </Row>
      )}

      {/* 详情模态框 */}
      <Modal
        title={
          <Space>
            <ThunderboltOutlined />
            诊断详情
          </Space>
        }
        open={detailModalVisible}
        onCancel={() => setDetailModalVisible(false)}
        width={800}
        footer={null}
      >
        {selectedDiagnosis && (
          <>
            {/* 基本信息 */}
            <Descriptions bordered size="small" column={2} style={{ marginBottom: 24 }}>
              <Descriptions.Item label="诊断ID">{selectedDiagnosis.diagnosisId}</Descriptions.Item>
              <Descriptions.Item label="运行模式">{selectedDiagnosis.modeName}</Descriptions.Item>
              <Descriptions.Item label="健康评分">
                <span
                  style={{
                    color: getHealthColor(selectedDiagnosis.healthScore),
                    fontWeight: 'bold',
                  }}
                >
                  {selectedDiagnosis.healthScore}
                </span>
              </Descriptions.Item>
              <Descriptions.Item label="整体状态">
                <Space>
                  {getSeverityIcon(selectedDiagnosis.overallSeverity)}
                  <span>{SeverityNames[selectedDiagnosis.overallSeverity]}</span>
                </Space>
              </Descriptions.Item>
              <Descriptions.Item label="诊断时间" span={2}>
                {new Date(selectedDiagnosis.timestamp).toLocaleString('zh-CN')}
              </Descriptions.Item>
              {selectedDiagnosis.summary && (
                <Descriptions.Item label="诊断摘要" span={2}>
                  {selectedDiagnosis.summary}
                </Descriptions.Item>
              )}
            </Descriptions>

            {/* 故障列表 */}
            {selectedDiagnosis.faults.length > 0 && (
              <>
                <h4>检测到的故障 ({selectedDiagnosis.faults.length})</h4>
                <Table
                  dataSource={selectedDiagnosis.faults}
                  columns={faultColumns}
                  rowKey={(_, index) => `fault-${index}`}
                  pagination={false}
                  size="small"
                  style={{ marginBottom: 24 }}
                />
              </>
            )}

            {/* 参数偏差 */}
            {selectedDiagnosis.deviations.length > 0 && (
              <>
                <h4>参数偏差 ({selectedDiagnosis.deviations.length})</h4>
                <Table
                  dataSource={selectedDiagnosis.deviations}
                  columns={[
                    {
                      title: '参数',
                      dataIndex: 'parameterName',
                      key: 'parameterName',
                    },
                    {
                      title: '当前值',
                      dataIndex: 'currentValue',
                      key: 'currentValue',
                      render: (v: number) => v.toFixed(2),
                    },
                    {
                      title: '基线均值',
                      dataIndex: 'baselineMean',
                      key: 'baselineMean',
                      render: (v: number) => v.toFixed(2),
                    },
                    {
                      title: '偏差(σ)',
                      dataIndex: 'deviationSigma',
                      key: 'deviationSigma',
                      render: (v: number, record: ParameterDeviation) => (
                        <span style={{ color: SeverityColors[record.severity] }}>
                          {v.toFixed(2)}σ
                        </span>
                      ),
                    },
                    {
                      title: '状态',
                      dataIndex: 'severity',
                      key: 'severity',
                      render: (severity: FaultSeverity) => (
                        <Tag color={SeverityColors[severity]}>{SeverityNames[severity]}</Tag>
                      ),
                    },
                  ]}
                  rowKey="parameterName"
                  pagination={false}
                  size="small"
                  style={{ marginBottom: 24 }}
                />
              </>
            )}

            {/* 维护建议 */}
            {selectedDiagnosis.recommendations.length > 0 && (
              <>
                <h4>维护建议</h4>
                <List
                  bordered
                  size="small"
                  dataSource={selectedDiagnosis.recommendations}
                  renderItem={(rec, index) => (
                    <List.Item>
                      <Space>
                        <Tag color="blue">{index + 1}</Tag>
                        <span>{rec}</span>
                      </Space>
                    </List.Item>
                  )}
                />
              </>
            )}
          </>
        )}
      </Modal>
    </div>
  )
}

export default MotorPrediction
