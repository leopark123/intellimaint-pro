import React, { useState, useEffect } from 'react';
import {
  Card,
  Table,
  Button,
  Space,
  Form,
  Select,
  DatePicker,
  message,
  Tag,
  Statistic,
  Row,
  Col,
  Progress,
  Modal,
  Descriptions,
  Divider,
  Tabs,
  Alert,
} from 'antd';
import {
  PlayCircleOutlined,
  BookOutlined,
  HistoryOutlined,
  WarningOutlined,
  ReloadOutlined,
  ExperimentOutlined,
} from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import dayjs from 'dayjs';
import {
  analyzeCycles,
  getCycles,
  getRecentCycles,
  getAnomalyCycles,
  getCycleStats,
  getBaselines,
  learnBaselines,
} from '../../api/cycleAnalysis';
import { getDevices } from '../../api/device';
import { getTags } from '../../api/tag';
import type {
  WorkCycle,
  CycleAnalysisResult,
  CycleStatsSummary,
  DeviceBaseline,
} from '../../types/cycleAnalysis';
import {
  AnomalyTypeLabels,
  getAnomalyTypeColor,
  formatDuration,
  getAnomalyScoreColor,
} from '../../types/cycleAnalysis';
import type { Device } from '../../types/device';
import type { Tag as TagType } from '../../types/tag';

const { RangePicker } = DatePicker;
const { TabPane } = Tabs;

const CycleAnalysisPage: React.FC = () => {
  const [devices, setDevices] = useState<Device[]>([]);
  const [tags, setTags] = useState<TagType[]>([]);
  const [cycles, setCycles] = useState<WorkCycle[]>([]);
  const [stats, setStats] = useState<CycleStatsSummary | null>(null);
  const [baselines, setBaselines] = useState<DeviceBaseline[]>([]);
  const [loading, setLoading] = useState(false);
  const [analyzing, setAnalyzing] = useState(false);
  const [learning, setLearning] = useState(false);
  const [selectedCycle, setSelectedCycle] = useState<WorkCycle | null>(null);
  const [analysisResult, setAnalysisResult] = useState<CycleAnalysisResult | null>(null);
  const [form] = Form.useForm();
  const [activeTab, setActiveTab] = useState('recent');

  useEffect(() => {
    loadDevices();
    loadTags();
  }, []);

  const loadDevices = async () => {
    try {
      const data = await getDevices();
      setDevices(data);
    } catch (error) {
      console.error('加载设备失败', error);
    }
  };

  const loadTags = async () => {
    try {
      const data = await getTags();
      setTags(data);
    } catch (error) {
      console.error('加载标签失败', error);
    }
  };

  const loadRecentCycles = async (deviceId: string) => {
    setLoading(true);
    try {
      const data = await getRecentCycles(deviceId, 50);
      setCycles(data);
      const statsData = await getCycleStats(deviceId);
      setStats(statsData);
    } catch (error: any) {
      message.error(error.message || '加载失败');
    } finally {
      setLoading(false);
    }
  };

  const loadAnomalyCycles = async (deviceId: string) => {
    setLoading(true);
    try {
      const data = await getAnomalyCycles(deviceId);
      setCycles(data);
    } catch (error: any) {
      message.error(error.message || '加载失败');
    } finally {
      setLoading(false);
    }
  };

  const loadBaselines = async (deviceId: string) => {
    try {
      const data = await getBaselines(deviceId);
      setBaselines(data);
    } catch (error: any) {
      message.error(error.message || '加载基线失败');
    }
  };

  const handleDeviceChange = (deviceId: string) => {
    form.setFieldsValue({ deviceId });
    loadRecentCycles(deviceId);
    loadBaselines(deviceId);
  };

  const handleAnalyze = async () => {
    try {
      const values = await form.validateFields();
      if (!values.timeRange || values.timeRange.length !== 2) {
        message.error('请选择时间范围');
        return;
      }

      setAnalyzing(true);
      const startTime = values.timeRange[0].valueOf();
      const endTime = values.timeRange[1].valueOf();

      const result = await analyzeCycles({
        deviceId: values.deviceId,
        angleTagId: values.angleTagId,
        motor1CurrentTagId: values.motor1CurrentTagId,
        motor2CurrentTagId: values.motor2CurrentTagId,
        startTimeUtc: startTime,
        endTimeUtc: endTime,
        angleThreshold: 5,
        minCycleDuration: 20,
        maxCycleDuration: 300,
      }, true); // save=true

      setAnalysisResult(result);
      setCycles(result.cycles);
      setStats(result.summary || null);
      message.success(`分析完成: 检测到 ${result.cycleCount} 个周期, ${result.anomalyCycleCount} 个异常`);
    } catch (error: any) {
      message.error(error.message || '分析失败');
    } finally {
      setAnalyzing(false);
    }
  };

  const handleLearnBaseline = async () => {
    try {
      const values = await form.validateFields();
      if (!values.timeRange || values.timeRange.length !== 2) {
        message.error('请选择学习数据的时间范围');
        return;
      }

      setLearning(true);
      const startTime = values.timeRange[0].valueOf();
      const endTime = values.timeRange[1].valueOf();

      await learnBaselines({
        deviceId: values.deviceId,
        angleTagId: values.angleTagId,
        motor1CurrentTagId: values.motor1CurrentTagId,
        motor2CurrentTagId: values.motor2CurrentTagId,
        startTimeUtc: startTime,
        endTimeUtc: endTime,
      });

      message.success('基线学习完成');
      loadBaselines(values.deviceId);
    } catch (error: any) {
      message.error(error.message || '基线学习失败');
    } finally {
      setLearning(false);
    }
  };

  const columns: ColumnsType<WorkCycle> = [
    {
      title: '开始时间',
      dataIndex: 'startTimeUtc',
      key: 'startTime',
      width: 160,
      render: (ts: number) => dayjs(ts).format('MM-DD HH:mm:ss'),
    },
    {
      title: '时长',
      dataIndex: 'durationSeconds',
      key: 'duration',
      width: 80,
      render: (s: number) => formatDuration(s),
    },
    {
      title: '最大角度',
      dataIndex: 'maxAngle',
      key: 'maxAngle',
      width: 90,
      render: (v: number) => `${v.toFixed(1)}°`,
    },
    {
      title: '电机1峰值',
      dataIndex: 'motor1PeakCurrent',
      key: 'motor1Peak',
      width: 100,
      render: (v: number) => v.toFixed(0),
    },
    {
      title: '电机2峰值',
      dataIndex: 'motor2PeakCurrent',
      key: 'motor2Peak',
      width: 100,
      render: (v: number) => v.toFixed(0),
    },
    {
      title: '平衡比',
      dataIndex: 'motorBalanceRatio',
      key: 'balance',
      width: 80,
      render: (v: number) => {
        const color = v < 0.8 || v > 1.2 ? 'orange' : 'green';
        return <Tag color={color}>{v.toFixed(2)}</Tag>;
      },
    },
    {
      title: '异常分数',
      dataIndex: 'anomalyScore',
      key: 'anomalyScore',
      width: 120,
      render: (score: number) => (
        <Progress
          percent={score}
          size="small"
          strokeColor={getAnomalyScoreColor(score)}
          format={(p) => `${p?.toFixed(0)}`}
        />
      ),
    },
    {
      title: '状态',
      key: 'status',
      width: 100,
      render: (_, record) => (
        record.isAnomaly ? (
          <Tag color={getAnomalyTypeColor(record.anomalyType || '')}>
            {AnomalyTypeLabels[record.anomalyType || ''] || '异常'}
          </Tag>
        ) : (
          <Tag color="green">正常</Tag>
        )
      ),
    },
    {
      title: '操作',
      key: 'action',
      width: 80,
      render: (_, record) => (
        <Button type="link" size="small" onClick={() => setSelectedCycle(record)}>
          详情
        </Button>
      ),
    },
  ];

  const deviceTags = tags.filter(t => {
    const deviceId = form.getFieldValue('deviceId');
    return !deviceId || t.deviceId === deviceId;
  });

  return (
    <div>
      {/* 页面标题 */}
      <div style={{ marginBottom: 24 }}>
        <h1 style={{ fontSize: 24, fontWeight: 700, color: 'var(--color-text-primary)', margin: '0 0 8px 0' }}>周期分析</h1>
        <p style={{ fontSize: 14, color: 'var(--color-text-muted)', margin: 0 }}>分析设备工作周期、学习基线、检测异常</p>
      </div>

      <Card style={{ marginBottom: 16 }}>
        <Form form={form} layout="inline" style={{ flexWrap: 'wrap', gap: 8 }}>
          <Form.Item name="deviceId" label="设备" rules={[{ required: true }]}>
            <Select
              style={{ width: 200 }}
              placeholder="选择设备"
              onChange={handleDeviceChange}
            >
              {devices.map(d => (
                <Select.Option key={d.deviceId} value={d.deviceId}>
                  {d.name || d.deviceId}
                </Select.Option>
              ))}
            </Select>
          </Form.Item>

          <Form.Item name="angleTagId" label="角度标签" rules={[{ required: true }]}>
            <Select style={{ width: 180 }} placeholder="选择角度标签">
              {deviceTags.map(t => (
                <Select.Option key={t.tagId} value={t.tagId}>
                  {t.name || t.tagId}
                </Select.Option>
              ))}
            </Select>
          </Form.Item>

          <Form.Item name="motor1CurrentTagId" label="电机1电流" rules={[{ required: true }]}>
            <Select style={{ width: 180 }} placeholder="选择电机1电流标签">
              {deviceTags.map(t => (
                <Select.Option key={t.tagId} value={t.tagId}>
                  {t.name || t.tagId}
                </Select.Option>
              ))}
            </Select>
          </Form.Item>

          <Form.Item name="motor2CurrentTagId" label="电机2电流" rules={[{ required: true }]}>
            <Select style={{ width: 180 }} placeholder="选择电机2电流标签">
              {deviceTags.map(t => (
                <Select.Option key={t.tagId} value={t.tagId}>
                  {t.name || t.tagId}
                </Select.Option>
              ))}
            </Select>
          </Form.Item>

          <Form.Item name="timeRange" label="时间范围">
            <RangePicker showTime />
          </Form.Item>

          <Form.Item>
            <Space>
              <Button
                type="primary"
                icon={<PlayCircleOutlined />}
                onClick={handleAnalyze}
                loading={analyzing}
              >
                分析周期
              </Button>
              <Button
                icon={<BookOutlined />}
                onClick={handleLearnBaseline}
                loading={learning}
              >
                学习基线
              </Button>
            </Space>
          </Form.Item>
        </Form>
      </Card>

      {/* 统计摘要 */}
      {stats && (
        <Card style={{ marginBottom: 16 }}>
          <Row gutter={16}>
            <Col span={4}>
              <Statistic title="平均周期时长" value={stats.avgDuration.toFixed(1)} suffix="秒" />
            </Col>
            <Col span={5}>
              <Statistic title="电机1平均峰值" value={stats.avgMotor1PeakCurrent.toFixed(0)} />
            </Col>
            <Col span={5}>
              <Statistic title="电机2平均峰值" value={stats.avgMotor2PeakCurrent.toFixed(0)} />
            </Col>
            <Col span={5}>
              <Statistic title="平均平衡比" value={stats.avgMotorBalanceRatio.toFixed(3)} />
            </Col>
            <Col span={5}>
              <Statistic
                title="平均异常分数"
                value={stats.avgAnomalyScore.toFixed(1)}
                valueStyle={{ color: getAnomalyScoreColor(stats.avgAnomalyScore) }}
              />
            </Col>
          </Row>
        </Card>
      )}

      {/* 基线状态 */}
      {baselines.length > 0 && (
        <Alert
          type="success"
          message={`已学习 ${baselines.length} 个基线模型`}
          description={baselines.map(b => `${b.baselineType} (${b.sampleCount} 样本)`).join(', ')}
          style={{ marginBottom: 16 }}
          icon={<ExperimentOutlined />}
        />
      )}

      <Card
        title="周期列表"
        extra={
          <Tabs activeKey={activeTab} onChange={setActiveTab} size="small">
            <TabPane tab={<span><HistoryOutlined /> 最近周期</span>} key="recent" />
            <TabPane tab={<span><WarningOutlined /> 异常周期</span>} key="anomaly" />
          </Tabs>
        }
      >
        <Table
          columns={columns}
          dataSource={cycles}
          rowKey="id"
          loading={loading}
          pagination={{ pageSize: 20 }}
          size="small"
          rowClassName={(record) => record.isAnomaly ? 'ant-table-row-warning' : ''}
        />
      </Card>

      {/* 周期详情弹窗 */}
      <Modal
        title="周期详情"
        open={!!selectedCycle}
        onCancel={() => setSelectedCycle(null)}
        footer={null}
        width={700}
      >
        {selectedCycle && (
          <Descriptions column={2} bordered size="small">
            <Descriptions.Item label="开始时间">
              {dayjs(selectedCycle.startTimeUtc).format('YYYY-MM-DD HH:mm:ss')}
            </Descriptions.Item>
            <Descriptions.Item label="结束时间">
              {dayjs(selectedCycle.endTimeUtc).format('YYYY-MM-DD HH:mm:ss')}
            </Descriptions.Item>
            <Descriptions.Item label="时长">
              {formatDuration(selectedCycle.durationSeconds)}
            </Descriptions.Item>
            <Descriptions.Item label="最大角度">
              {selectedCycle.maxAngle.toFixed(1)}°
            </Descriptions.Item>

            <Descriptions.Item label="电机1峰值电流">
              {selectedCycle.motor1PeakCurrent.toFixed(0)}
            </Descriptions.Item>
            <Descriptions.Item label="电机2峰值电流">
              {selectedCycle.motor2PeakCurrent.toFixed(0)}
            </Descriptions.Item>
            <Descriptions.Item label="电机1平均电流">
              {selectedCycle.motor1AvgCurrent.toFixed(0)}
            </Descriptions.Item>
            <Descriptions.Item label="电机2平均电流">
              {selectedCycle.motor2AvgCurrent.toFixed(0)}
            </Descriptions.Item>
            <Descriptions.Item label="电机1能耗">
              {selectedCycle.motor1Energy.toFixed(0)}
            </Descriptions.Item>
            <Descriptions.Item label="电机2能耗">
              {selectedCycle.motor2Energy.toFixed(0)}
            </Descriptions.Item>

            <Descriptions.Item label="电机平衡比">
              <Tag color={selectedCycle.motorBalanceRatio < 0.8 || selectedCycle.motorBalanceRatio > 1.2 ? 'orange' : 'green'}>
                {selectedCycle.motorBalanceRatio.toFixed(3)}
              </Tag>
            </Descriptions.Item>
            <Descriptions.Item label="基线偏差">
              {selectedCycle.baselineDeviationPercent.toFixed(1)}%
            </Descriptions.Item>

            <Descriptions.Item label="异常分数" span={2}>
              <Progress
                percent={selectedCycle.anomalyScore}
                strokeColor={getAnomalyScoreColor(selectedCycle.anomalyScore)}
                style={{ width: 200 }}
              />
            </Descriptions.Item>

            {selectedCycle.isAnomaly && (
              <Descriptions.Item label="异常类型" span={2}>
                <Tag color={getAnomalyTypeColor(selectedCycle.anomalyType || '')}>
                  {AnomalyTypeLabels[selectedCycle.anomalyType || ''] || selectedCycle.anomalyType}
                </Tag>
              </Descriptions.Item>
            )}
          </Descriptions>
        )}
      </Modal>
    </div>
  );
};

export default CycleAnalysisPage;
