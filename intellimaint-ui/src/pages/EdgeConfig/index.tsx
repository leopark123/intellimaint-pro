import React, { useState, useEffect, useCallback } from 'react';
import {
  Card,
  Row,
  Col,
  Select,
  Button,
  Form,
  InputNumber,
  Switch,
  message,
  Tabs,
  Table,
  Tag,
  Space,
  Popconfirm,
  Input,
  Statistic,
  Alert,
} from 'antd';
import {
  SyncOutlined,
  CloudServerOutlined,
  ThunderboltOutlined,
  DatabaseOutlined,
  CheckCircleOutlined,
  CloseCircleOutlined,
} from '@ant-design/icons';
import type {
  EdgeConfig,
  EdgeSummary,
  EdgeStatus,
  TagProcessingConfig,
  PagedTagConfigResult,
} from '../../types/edge';
import {
  listEdges,
  getEdgeConfig,
  updateEdgeConfig,
  getEdgeStatus,
  getTagConfigs,
  batchUpdateTagConfigs,
  deleteTagConfig,
  notifyConfigSync,
} from '../../api/edge';

const { Option } = Select;
const { TabPane } = Tabs;

const EdgeConfigPage: React.FC = () => {
  const [edges, setEdges] = useState<EdgeSummary[]>([]);
  const [selectedEdgeId, setSelectedEdgeId] = useState<string>();
  const [config, setConfig] = useState<EdgeConfig | null>(null);
  const [status, setStatus] = useState<EdgeStatus | null>(null);
  const [tagConfigs, setTagConfigs] = useState<PagedTagConfigResult | null>(null);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [form] = Form.useForm();

  // 加载 Edge 列表
  const loadEdges = useCallback(async () => {
    try {
      const data = await listEdges();
      setEdges(data);
      if (data.length > 0 && !selectedEdgeId) {
        setSelectedEdgeId(data[0].edgeId);
      }
    } catch (err) {
      message.error('加载 Edge 列表失败');
    }
  }, [selectedEdgeId]);

  // 加载选中 Edge 的配置
  const loadEdgeData = useCallback(async (edgeId: string) => {
    setLoading(true);
    try {
      const [cfg, sts, tags] = await Promise.all([
        getEdgeConfig(edgeId),
        getEdgeStatus(edgeId),
        getTagConfigs(edgeId),
      ]);
      setConfig(cfg);
      setStatus(sts);
      setTagConfigs(tags);
      if (cfg) {
        form.setFieldsValue({
          name: cfg.name,
          description: cfg.description,
          // 预处理配置
          processingEnabled: cfg.processing.enabled,
          defaultDeadband: cfg.processing.defaultDeadband,
          defaultDeadbandPercent: cfg.processing.defaultDeadbandPercent,
          defaultMinIntervalMs: cfg.processing.defaultMinIntervalMs,
          forceUploadIntervalMs: cfg.processing.forceUploadIntervalMs,
          outlierEnabled: cfg.processing.outlierEnabled,
          outlierSigmaThreshold: cfg.processing.outlierSigmaThreshold,
          outlierAction: cfg.processing.outlierAction,
          // 断网续传配置
          storeForwardEnabled: cfg.storeForward.enabled,
          maxStoreSizeMB: cfg.storeForward.maxStoreSizeMB,
          retentionDays: cfg.storeForward.retentionDays,
          compressionEnabled: cfg.storeForward.compressionEnabled,
          compressionAlgorithm: cfg.storeForward.compressionAlgorithm,
          // 网络配置
          healthCheckIntervalMs: cfg.network.healthCheckIntervalMs,
          healthCheckTimeoutMs: cfg.network.healthCheckTimeoutMs,
          offlineThreshold: cfg.network.offlineThreshold,
          sendBatchSize: cfg.network.sendBatchSize,
          sendIntervalMs: cfg.network.sendIntervalMs,
        });
      }
    } catch (err) {
      message.error('加载配置失败');
    } finally {
      setLoading(false);
    }
  }, [form]);

  useEffect(() => {
    loadEdges();
  }, [loadEdges]);

  useEffect(() => {
    if (selectedEdgeId) {
      loadEdgeData(selectedEdgeId);
    }
  }, [selectedEdgeId, loadEdgeData]);

  // 保存配置
  const handleSave = async () => {
    if (!selectedEdgeId || !config) return;

    setSaving(true);
    try {
      const values = await form.validateFields();
      const updatedConfig: EdgeConfig = {
        ...config,
        name: values.name,
        description: values.description,
        processing: {
          enabled: values.processingEnabled,
          defaultDeadband: values.defaultDeadband,
          defaultDeadbandPercent: values.defaultDeadbandPercent,
          defaultMinIntervalMs: values.defaultMinIntervalMs,
          forceUploadIntervalMs: values.forceUploadIntervalMs,
          outlierEnabled: values.outlierEnabled,
          outlierSigmaThreshold: values.outlierSigmaThreshold,
          outlierAction: values.outlierAction,
        },
        storeForward: {
          enabled: values.storeForwardEnabled,
          maxStoreSizeMB: values.maxStoreSizeMB,
          retentionDays: values.retentionDays,
          compressionEnabled: values.compressionEnabled,
          compressionAlgorithm: values.compressionAlgorithm,
        },
        network: {
          healthCheckIntervalMs: values.healthCheckIntervalMs,
          healthCheckTimeoutMs: values.healthCheckTimeoutMs,
          offlineThreshold: values.offlineThreshold,
          sendBatchSize: values.sendBatchSize,
          sendIntervalMs: values.sendIntervalMs,
        },
      };

      await updateEdgeConfig(selectedEdgeId, updatedConfig);
      message.success('配置已保存');
      await loadEdgeData(selectedEdgeId);
    } catch (err) {
      message.error('保存失败');
    } finally {
      setSaving(false);
    }
  };

  // 手动同步配置
  const handleSync = async () => {
    if (!selectedEdgeId) return;
    try {
      await notifyConfigSync(selectedEdgeId);
      message.success('已通知 Edge 同步配置');
    } catch (err) {
      message.error('通知失败');
    }
  };

  // 标签配置表格列
  const tagColumns = [
    {
      title: '标签ID',
      dataIndex: 'tagId',
      key: 'tagId',
      width: 200,
    },
    {
      title: '死区',
      dataIndex: 'deadband',
      key: 'deadband',
      width: 100,
      render: (v: number | undefined) => v?.toFixed(4) ?? '-',
    },
    {
      title: '死区%',
      dataIndex: 'deadbandPercent',
      key: 'deadbandPercent',
      width: 100,
      render: (v: number | undefined) => v !== undefined ? `${v}%` : '-',
    },
    {
      title: '最小间隔',
      dataIndex: 'minIntervalMs',
      key: 'minIntervalMs',
      width: 100,
      render: (v: number | undefined) => v !== undefined ? `${v}ms` : '-',
    },
    {
      title: '绕过',
      dataIndex: 'bypass',
      key: 'bypass',
      width: 80,
      render: (v: boolean) => v ? <Tag color="warning">是</Tag> : <Tag>否</Tag>,
    },
    {
      title: '优先级',
      dataIndex: 'priority',
      key: 'priority',
      width: 80,
    },
    {
      title: '操作',
      key: 'action',
      width: 100,
      render: (_: unknown, record: TagProcessingConfig) => (
        <Popconfirm
          title="确定删除此配置？"
          onConfirm={() => handleDeleteTagConfig(record.tagId)}
        >
          <Button type="link" danger size="small">删除</Button>
        </Popconfirm>
      ),
    },
  ];

  const handleDeleteTagConfig = async (tagId: string) => {
    if (!selectedEdgeId) return;
    try {
      await deleteTagConfig(selectedEdgeId, tagId);
      message.success('已删除');
      await loadEdgeData(selectedEdgeId);
    } catch (err) {
      message.error('删除失败');
    }
  };

  return (
    <div style={{ padding: 24 }}>
      <Card
        title="Edge 配置管理"
        extra={
          <Space>
            <Select
              style={{ width: 200 }}
              value={selectedEdgeId}
              onChange={setSelectedEdgeId}
              placeholder="选择 Edge 节点"
            >
              {edges.map((e) => (
                <Option key={e.edgeId} value={e.edgeId}>
                  {e.name} {e.isOnline ? '(在线)' : '(离线)'}
                </Option>
              ))}
            </Select>
            <Button icon={<SyncOutlined />} onClick={handleSync}>
              同步配置
            </Button>
            <Button type="primary" onClick={handleSave} loading={saving}>
              保存
            </Button>
          </Space>
        }
      >
        {/* 状态卡片 */}
        {status && (
          <Row gutter={16} style={{ marginBottom: 24 }}>
            <Col span={4}>
              <Card size="small">
                <Statistic
                  title="状态"
                  value={status.isOnline ? '在线' : '离线'}
                  prefix={status.isOnline ?
                    <CheckCircleOutlined style={{ color: '#52c41a' }} /> :
                    <CloseCircleOutlined style={{ color: '#ff4d4f' }} />
                  }
                />
              </Card>
            </Col>
            <Col span={4}>
              <Card size="small">
                <Statistic
                  title="待发送"
                  value={status.pendingPoints}
                  suffix="点"
                />
              </Card>
            </Col>
            <Col span={4}>
              <Card size="small">
                <Statistic
                  title="过滤率"
                  value={status.filterRate}
                  precision={1}
                  suffix="%"
                />
              </Card>
            </Col>
            <Col span={4}>
              <Card size="small">
                <Statistic
                  title="已发送"
                  value={status.sentCount}
                  suffix="点"
                />
              </Card>
            </Col>
            <Col span={4}>
              <Card size="small">
                <Statistic
                  title="本地存储"
                  value={status.storedMB}
                  precision={2}
                  suffix="MB"
                />
              </Card>
            </Col>
            <Col span={4}>
              <Card size="small">
                <Statistic
                  title="版本"
                  value={status.version ?? '-'}
                />
              </Card>
            </Col>
          </Row>
        )}

        <Form form={form} layout="vertical">
          <Tabs defaultActiveKey="processing">
            <TabPane
              tab={<span><ThunderboltOutlined /> 数据预处理</span>}
              key="processing"
            >
              <Row gutter={24}>
                <Col span={6}>
                  <Form.Item name="processingEnabled" label="启用预处理" valuePropName="checked">
                    <Switch />
                  </Form.Item>
                </Col>
                <Col span={6}>
                  <Form.Item name="defaultDeadband" label="默认死区值">
                    <InputNumber min={0} step={0.001} style={{ width: '100%' }} />
                  </Form.Item>
                </Col>
                <Col span={6}>
                  <Form.Item name="defaultDeadbandPercent" label="默认死区百分比 (%)">
                    <InputNumber min={0} max={100} step={0.1} style={{ width: '100%' }} />
                  </Form.Item>
                </Col>
                <Col span={6}>
                  <Form.Item name="defaultMinIntervalMs" label="默认最小间隔 (ms)">
                    <InputNumber min={0} step={100} style={{ width: '100%' }} />
                  </Form.Item>
                </Col>
              </Row>
              <Row gutter={24}>
                <Col span={6}>
                  <Form.Item name="forceUploadIntervalMs" label="强制上传间隔 (ms)">
                    <InputNumber min={0} step={1000} style={{ width: '100%' }} />
                  </Form.Item>
                </Col>
                <Col span={6}>
                  <Form.Item name="outlierEnabled" label="启用异常检测" valuePropName="checked">
                    <Switch />
                  </Form.Item>
                </Col>
                <Col span={6}>
                  <Form.Item name="outlierSigmaThreshold" label="异常阈值 (sigma)">
                    <InputNumber min={1} max={10} step={0.5} style={{ width: '100%' }} />
                  </Form.Item>
                </Col>
                <Col span={6}>
                  <Form.Item name="outlierAction" label="异常处理方式">
                    <Select>
                      <Option value="Drop">丢弃</Option>
                      <Option value="Mark">标记</Option>
                      <Option value="Pass">通过</Option>
                    </Select>
                  </Form.Item>
                </Col>
              </Row>
            </TabPane>

            <TabPane
              tab={<span><DatabaseOutlined /> 断网续传</span>}
              key="storeforward"
            >
              <Row gutter={24}>
                <Col span={6}>
                  <Form.Item name="storeForwardEnabled" label="启用断网续传" valuePropName="checked">
                    <Switch />
                  </Form.Item>
                </Col>
                <Col span={6}>
                  <Form.Item name="maxStoreSizeMB" label="最大存储 (MB)">
                    <InputNumber min={100} max={10000} step={100} style={{ width: '100%' }} />
                  </Form.Item>
                </Col>
                <Col span={6}>
                  <Form.Item name="retentionDays" label="保留天数">
                    <InputNumber min={1} max={30} style={{ width: '100%' }} />
                  </Form.Item>
                </Col>
                <Col span={6}>
                  <Form.Item name="compressionEnabled" label="启用压缩" valuePropName="checked">
                    <Switch />
                  </Form.Item>
                </Col>
              </Row>
              <Row gutter={24}>
                <Col span={6}>
                  <Form.Item name="compressionAlgorithm" label="压缩算法">
                    <Select>
                      <Option value="Gzip">Gzip</Option>
                      <Option value="Brotli">Brotli</Option>
                    </Select>
                  </Form.Item>
                </Col>
              </Row>
            </TabPane>

            <TabPane
              tab={<span><CloudServerOutlined /> 网络配置</span>}
              key="network"
            >
              <Row gutter={24}>
                <Col span={6}>
                  <Form.Item name="healthCheckIntervalMs" label="健康检查间隔 (ms)">
                    <InputNumber min={1000} step={1000} style={{ width: '100%' }} />
                  </Form.Item>
                </Col>
                <Col span={6}>
                  <Form.Item name="healthCheckTimeoutMs" label="健康检查超时 (ms)">
                    <InputNumber min={500} step={500} style={{ width: '100%' }} />
                  </Form.Item>
                </Col>
                <Col span={6}>
                  <Form.Item name="offlineThreshold" label="离线阈值 (次)">
                    <InputNumber min={1} max={10} style={{ width: '100%' }} />
                  </Form.Item>
                </Col>
                <Col span={6}>
                  <Form.Item name="sendBatchSize" label="发送批量大小">
                    <InputNumber min={50} max={2000} step={50} style={{ width: '100%' }} />
                  </Form.Item>
                </Col>
              </Row>
              <Row gutter={24}>
                <Col span={6}>
                  <Form.Item name="sendIntervalMs" label="发送间隔 (ms)">
                    <InputNumber min={100} step={100} style={{ width: '100%' }} />
                  </Form.Item>
                </Col>
              </Row>
            </TabPane>

            <TabPane
              tab={<span>标签配置</span>}
              key="tags"
            >
              <Alert
                message="标签级配置优先于全局配置"
                description="为特定标签设置自定义的死区、采样间隔等参数"
                type="info"
                style={{ marginBottom: 16 }}
              />
              <Table
                columns={tagColumns}
                dataSource={tagConfigs?.items ?? []}
                rowKey="tagId"
                size="small"
                pagination={{
                  total: tagConfigs?.total ?? 0,
                  pageSize: tagConfigs?.pageSize ?? 50,
                  current: tagConfigs?.page ?? 1,
                }}
              />
            </TabPane>
          </Tabs>
        </Form>
      </Card>
    </div>
  );
};

export default EdgeConfigPage;
