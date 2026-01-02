import React, { useState, useEffect } from 'react';
import {
  Card,
  Table,
  Button,
  Space,
  Modal,
  Form,
  Input,
  Select,
  Switch,
  message,
  Popconfirm,
  Tag,
  Tooltip,
  InputNumber,
  Divider,
  Typography,
  Badge,
  Tabs,
  DatePicker,
} from 'antd';
import {
  PlusOutlined,
  EditOutlined,
  DeleteOutlined,
  PlayCircleOutlined,
  PauseCircleOutlined,
  ReloadOutlined,
  MinusCircleOutlined,
  EyeOutlined,
} from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import {
  getCollectionRules,
  createCollectionRule,
  updateCollectionRule,
  deleteCollectionRule,
  enableCollectionRule,
  disableCollectionRule,
  getCollectionSegments,
  deleteCollectionSegment,
} from '../../api/collectionRule';
import { getDevices } from '../../api/device';
import { getTags } from '../../api/tag';
import { queryTelemetry } from '../../api/telemetry';
import type {
  CollectionRule,
  CollectionRuleParsed,
  ConditionConfig,
  ConditionItem,
  CollectionConfig,
  CreateCollectionRuleRequest,
  UpdateCollectionRuleRequest,
  CollectionSegment,
} from '../../types/collectionRule';
import { parseCollectionRule, getOperatorText, getSegmentStatusText, getSegmentStatusColor } from '../../types/collectionRule';
import type { Device } from '../../types/device';
import type { Tag as TagType } from '../../types/tag';
import type { TelemetryPoint } from '../../types/telemetry';
import dayjs from 'dayjs';

const { Text } = Typography;

const CollectionRulesPage: React.FC = () => {
  const [rules, setRules] = useState<CollectionRuleParsed[]>([]);
  const [devices, setDevices] = useState<Device[]>([]);
  const [tags, setTags] = useState<TagType[]>([]);
  const [loading, setLoading] = useState(false);
  const [modalVisible, setModalVisible] = useState(false);
  const [editingRule, setEditingRule] = useState<CollectionRuleParsed | null>(null);
  const [form] = Form.useForm();
  
  // 采集片段相关状态
  const [activeTab, setActiveTab] = useState('rules');
  const [segments, setSegments] = useState<CollectionSegment[]>([]);
  const [segmentsLoading, setSegmentsLoading] = useState(false);
  const [selectedSegment, setSelectedSegment] = useState<CollectionSegment | null>(null);
  const [segmentDataVisible, setSegmentDataVisible] = useState(false);
  const [segmentData, setSegmentData] = useState<TelemetryPoint[]>([]);
  const [segmentDataLoading, setSegmentDataLoading] = useState(false);

  const loadRules = async () => {
    setLoading(true);
    try {
      const data = await getCollectionRules();
      setRules(data.map(parseCollectionRule));
    } catch (error: any) {
      message.error(error.message || '加载失败');
    } finally {
      setLoading(false);
    }
  };

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

  useEffect(() => {
    loadRules();
    loadDevices();
    loadTags();
  }, []);

  // 加载采集片段
  const loadSegments = async () => {
    setSegmentsLoading(true);
    try {
      const data = await getCollectionSegments({ limit: 100 });
      setSegments(data);
    } catch (error: any) {
      message.error(error.message || '加载采集片段失败');
    } finally {
      setSegmentsLoading(false);
    }
  };

  // 切换标签页时加载数据
  const handleTabChange = (key: string) => {
    setActiveTab(key);
    if (key === 'segments') {
      loadSegments();
    }
  };

  // 查看采集片段数据
  const handleViewSegmentData = async (segment: CollectionSegment) => {
    setSelectedSegment(segment);
    setSegmentDataVisible(true);
    setSegmentDataLoading(true);
    try {
      // 查询该时间段内的遥测数据
      const response = await queryTelemetry({
        deviceId: segment.deviceId,
        startTs: segment.startTimeUtc,
        endTs: segment.endTimeUtc || Date.now(),
        limit: 1000,
      });
      if (response.success && response.data) {
        setSegmentData(response.data);
      } else {
        message.error(response.error || '加载数据失败');
        setSegmentData([]);
      }
    } catch (error: any) {
      message.error(error.message || '加载数据失败');
      setSegmentData([]);
    } finally {
      setSegmentDataLoading(false);
    }
  };

  // 删除采集片段
  const handleDeleteSegment = async (id: number) => {
    try {
      await deleteCollectionSegment(id);
      message.success('删除成功');
      loadSegments();
    } catch (error: any) {
      message.error(error.message || '删除失败');
    }
  };

  const handleCreate = () => {
    setEditingRule(null);
    form.resetFields();
    form.setFieldsValue({
      enabled: true,
      startCondition: { logic: 'AND', conditions: [{ type: 'tag', operator: 'gt' }] },
      stopCondition: { logic: 'AND', conditions: [{ type: 'tag', operator: 'lt' }] },
      collectionConfig: { tagIds: [], preBufferSeconds: 5, postBufferSeconds: 3 },
    });
    setModalVisible(true);
  };

  const handleEdit = (rule: CollectionRuleParsed) => {
    setEditingRule(rule);
    form.setFieldsValue({
      ruleId: rule.ruleId,
      name: rule.name,
      description: rule.description,
      deviceId: rule.deviceId,
      enabled: rule.enabled,
      startCondition: rule.startCondition,
      stopCondition: rule.stopCondition,
      collectionConfig: rule.collectionConfig,
    });
    setModalVisible(true);
  };

  const handleDelete = async (ruleId: string) => {
    try {
      await deleteCollectionRule(ruleId);
      message.success('删除成功');
      loadRules();
    } catch (error: any) {
      message.error(error.message || '删除失败');
    }
  };

  const handleToggleEnabled = async (rule: CollectionRuleParsed) => {
    try {
      if (rule.enabled) {
        await disableCollectionRule(rule.ruleId);
        message.success('已禁用');
      } else {
        await enableCollectionRule(rule.ruleId);
        message.success('已启用');
      }
      loadRules();
    } catch (error: any) {
      message.error(error.message || '操作失败');
    }
  };

  const handleSubmit = async () => {
    try {
      const values = await form.validateFields();

      if (editingRule) {
        const request: UpdateCollectionRuleRequest = {
          name: values.name,
          description: values.description,
          deviceId: values.deviceId,
          enabled: values.enabled,
          startCondition: values.startCondition,
          stopCondition: values.stopCondition,
          collectionConfig: values.collectionConfig,
        };
        await updateCollectionRule(editingRule.ruleId, request);
        message.success('更新成功');
      } else {
        const request: CreateCollectionRuleRequest = {
          ruleId: values.ruleId,
          name: values.name,
          description: values.description,
          deviceId: values.deviceId,
          enabled: values.enabled,
          startCondition: values.startCondition,
          stopCondition: values.stopCondition,
          collectionConfig: values.collectionConfig,
        };
        await createCollectionRule(request);
        message.success('创建成功');
      }

      setModalVisible(false);
      loadRules();
    } catch (error: any) {
      if (error.errorFields) {
        return; // 表单验证错误
      }
      message.error(error.message || '保存失败');
    }
  };

  const renderCondition = (condition: ConditionConfig | undefined) => {
    if (!condition || !condition.conditions || condition.conditions.length === 0) {
      return <Text type="secondary" style={{ fontSize: 12 }}>未配置</Text>;
    }
    return (
      <Space direction="vertical" size={0}>
        <Text type="secondary" style={{ fontSize: 12 }}>
          {condition.logic}
        </Text>
        {condition.conditions.map((c, i) => (
          <Text key={i} style={{ fontSize: 12 }}>
            {c.type === 'tag' ? (
              <>
                {c.tagId} {getOperatorText(c.operator || '')} {c.value}
              </>
            ) : (
              <>持续 {c.seconds} 秒</>
            )}
          </Text>
        ))}
      </Space>
    );
  };

  const columns: ColumnsType<CollectionRuleParsed> = [
    {
      title: '状态',
      dataIndex: 'enabled',
      key: 'enabled',
      width: 80,
      render: (enabled: boolean) => (
        <Badge status={enabled ? 'success' : 'default'} text={enabled ? '启用' : '禁用'} />
      ),
    },
    {
      title: '规则名称',
      dataIndex: 'name',
      key: 'name',
      render: (name: string, record) => (
        <Space direction="vertical" size={0}>
          <Text strong>{name}</Text>
          <Text type="secondary" style={{ fontSize: 12 }}>
            {record.ruleId}
          </Text>
        </Space>
      ),
    },
    {
      title: '设备',
      dataIndex: 'deviceId',
      key: 'deviceId',
      width: 120,
    },
    {
      title: '开始条件',
      key: 'startCondition',
      width: 180,
      render: (_, record) => renderCondition(record.startCondition),
    },
    {
      title: '停止条件',
      key: 'stopCondition',
      width: 180,
      render: (_, record) => renderCondition(record.stopCondition),
    },
    {
      title: '触发次数',
      dataIndex: 'triggerCount',
      key: 'triggerCount',
      width: 100,
      render: (count: number) => <Tag color="blue">{count}</Tag>,
    },
    {
      title: '最后触发',
      dataIndex: 'lastTriggerUtc',
      key: 'lastTriggerUtc',
      width: 160,
      render: (ts: number | undefined) =>
        ts ? new Date(ts).toLocaleString() : '-',
    },
    {
      title: '操作',
      key: 'action',
      width: 200,
      render: (_, record) => (
        <Space>
          <Tooltip title={record.enabled ? '禁用' : '启用'}>
            <Button
              type="text"
              size="small"
              icon={record.enabled ? <PauseCircleOutlined /> : <PlayCircleOutlined />}
              onClick={() => handleToggleEnabled(record)}
            />
          </Tooltip>
          <Tooltip title="编辑">
            <Button
              type="text"
              size="small"
              icon={<EditOutlined />}
              onClick={() => handleEdit(record)}
            />
          </Tooltip>
          <Popconfirm
            title="确定删除此规则？"
            onConfirm={() => handleDelete(record.ruleId)}
            okText="删除"
            cancelText="取消"
          >
            <Tooltip title="删除">
              <Button type="text" size="small" danger icon={<DeleteOutlined />} />
            </Tooltip>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  const deviceTags = tags.filter(
    (t) => !form.getFieldValue('deviceId') || t.deviceId === form.getFieldValue('deviceId')
  );

  // 采集片段表格列定义
  const segmentColumns: ColumnsType<CollectionSegment> = [
    {
      title: '状态',
      dataIndex: 'status',
      key: 'status',
      width: 100,
      render: (status: number) => (
        <Badge status={getSegmentStatusColor(status) as any} text={getSegmentStatusText(status)} />
      ),
    },
    {
      title: '规则ID',
      dataIndex: 'ruleId',
      key: 'ruleId',
      width: 150,
    },
    {
      title: '设备',
      dataIndex: 'deviceId',
      key: 'deviceId',
      width: 120,
    },
    {
      title: '开始时间',
      dataIndex: 'startTimeUtc',
      key: 'startTimeUtc',
      width: 180,
      render: (ts: number) => dayjs(ts).format('YYYY-MM-DD HH:mm:ss'),
    },
    {
      title: '结束时间',
      dataIndex: 'endTimeUtc',
      key: 'endTimeUtc',
      width: 180,
      render: (ts: number | undefined) => ts ? dayjs(ts).format('YYYY-MM-DD HH:mm:ss') : '-',
    },
    {
      title: '数据点数',
      dataIndex: 'dataPointCount',
      key: 'dataPointCount',
      width: 100,
      render: (count: number) => <Tag color="blue">{count}</Tag>,
    },
    {
      title: '操作',
      key: 'action',
      width: 120,
      render: (_, record) => (
        <Space>
          <Tooltip title="查看数据">
            <Button
              type="text"
              size="small"
              icon={<EyeOutlined />}
              onClick={() => handleViewSegmentData(record)}
            />
          </Tooltip>
          <Popconfirm
            title="确定删除此片段？"
            onConfirm={() => handleDeleteSegment(record.id)}
            okText="删除"
            cancelText="取消"
          >
            <Tooltip title="删除">
              <Button type="text" size="small" danger icon={<DeleteOutlined />} />
            </Tooltip>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  // 采集数据表格列定义
  const dataColumns: ColumnsType<TelemetryPoint> = [
    {
      title: '时间',
      dataIndex: 'ts',
      key: 'ts',
      width: 180,
      render: (ts: number) => dayjs(ts).format('YYYY-MM-DD HH:mm:ss.SSS'),
    },
    {
      title: '标签',
      dataIndex: 'tagId',
      key: 'tagId',
      width: 150,
    },
    {
      title: '值',
      key: 'value',
      width: 120,
      render: (_, record: any) => {
        // 后端返回的是单一的 value 字段
        const val = record.value;
        if (val === null || val === undefined) return '-';
        if (typeof val === 'number') return val.toFixed(2);
        if (typeof val === 'boolean') return val ? 'true' : 'false';
        return String(val);
      },
    },
    {
      title: '质量',
      dataIndex: 'quality',
      key: 'quality',
      width: 80,
      render: (q: number) => {
        // OPC 质量码: 192 (0xC0) = Good, 0 = Bad
        // 简化判断: >= 192 为好
        const isGood = q >= 192 || q === 0;
        return <Tag color={isGood ? 'green' : 'red'}>{isGood ? '好' : '差'}</Tag>;
      },
    },
  ];

  return (
    <>
      {/* 页面标题 */}
      <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', marginBottom: 24 }}>
        <div>
          <h1 style={{ fontSize: 24, fontWeight: 700, color: 'var(--color-text-primary)', margin: '0 0 8px 0' }}>采集规则</h1>
          <p style={{ fontSize: 14, color: 'var(--color-text-muted)', margin: 0 }}>配置数据采集策略和触发条件</p>
        </div>
        <Space>
          <Button icon={<ReloadOutlined />} onClick={loadRules}>
            刷新
          </Button>
          <Button type="primary" icon={<PlusOutlined />} onClick={handleCreate}>
            新建规则
          </Button>
        </Space>
      </div>

      <Card>
        <Tabs activeKey={activeTab} onChange={handleTabChange}>
          <Tabs.TabPane tab="采集规则" key="rules">
            <Table
              columns={columns}
              dataSource={rules}
              rowKey="ruleId"
              loading={loading}
              pagination={{ pageSize: 20 }}
            />
          </Tabs.TabPane>

          <Tabs.TabPane tab="采集片段" key="segments">
            <Space style={{ marginBottom: 16 }}>
              <Button icon={<ReloadOutlined />} onClick={loadSegments}>
                刷新片段
              </Button>
            </Space>
            <Table
              columns={segmentColumns}
              dataSource={segments}
              rowKey="id"
              loading={segmentsLoading}
              pagination={{ pageSize: 20 }}
            />
          </Tabs.TabPane>
        </Tabs>
      </Card>

      {/* 规则编辑 Modal */}
      <Modal
        title={editingRule ? '编辑采集规则' : '新建采集规则'}
        open={modalVisible}
        onOk={handleSubmit}
        onCancel={() => setModalVisible(false)}
        width={800}
        okText="保存"
        cancelText="取消"
      >
        <Form form={form} layout="vertical">
          <Form.Item
            name="ruleId"
            label="规则ID"
            rules={[{ required: true, message: '请输入规则ID' }]}
          >
            <Input placeholder="唯一标识" disabled={!!editingRule} />
          </Form.Item>

          <Form.Item
            name="name"
            label="规则名称"
            rules={[{ required: true, message: '请输入规则名称' }]}
          >
            <Input placeholder="如：翻车机工作采集" />
          </Form.Item>

          <Form.Item name="description" label="描述">
            <Input.TextArea rows={2} placeholder="可选描述" />
          </Form.Item>

          <Form.Item
            name="deviceId"
            label="设备"
            rules={[{ required: true, message: '请选择设备' }]}
          >
            <Select placeholder="选择设备">
              {devices.map((d) => (
                <Select.Option key={d.deviceId} value={d.deviceId}>
                  {d.name || d.deviceId}
                </Select.Option>
              ))}
            </Select>
          </Form.Item>

          <Form.Item name="enabled" label="启用" valuePropName="checked">
            <Switch />
          </Form.Item>

          <Divider>开始条件</Divider>
          <ConditionEditor name="startCondition" tags={tags} />

          <Divider>停止条件</Divider>
          <ConditionEditor name="stopCondition" tags={tags} />

          <Divider>采集配置</Divider>
          <Form.Item
            name={['collectionConfig', 'tagIds']}
            label="采集标签"
            rules={[{ required: true, message: '请选择采集标签' }]}
          >
            <Select mode="multiple" placeholder="选择要采集的标签">
              {tags.map((t) => (
                <Select.Option key={t.tagId} value={t.tagId}>
                  {t.name || t.tagId}
                </Select.Option>
              ))}
            </Select>
          </Form.Item>

          <Space>
            <Form.Item name={['collectionConfig', 'preBufferSeconds']} label="前置缓冲(秒)">
              <InputNumber min={0} max={60} />
            </Form.Item>
            <Form.Item name={['collectionConfig', 'postBufferSeconds']} label="后置缓冲(秒)">
              <InputNumber min={0} max={60} />
            </Form.Item>
          </Space>
        </Form>
      </Modal>

      {/* 查看采集数据 Modal */}
      <Modal
        title={`采集数据 - ${selectedSegment?.ruleId || ''}`}
        open={segmentDataVisible}
        onCancel={() => setSegmentDataVisible(false)}
        footer={null}
        width={900}
      >
        {selectedSegment && (
          <div style={{ marginBottom: 16 }}>
            <Space split={<Divider type="vertical" />}>
              <Text>设备: {selectedSegment.deviceId}</Text>
              <Text>开始: {dayjs(selectedSegment.startTimeUtc).format('YYYY-MM-DD HH:mm:ss')}</Text>
              <Text>结束: {selectedSegment.endTimeUtc ? dayjs(selectedSegment.endTimeUtc).format('YYYY-MM-DD HH:mm:ss') : '-'}</Text>
              <Text>数据点: {selectedSegment.dataPointCount}</Text>
            </Space>
          </div>
        )}
        <Table
          columns={dataColumns}
          dataSource={segmentData}
          rowKey={(r) => `${r.ts}-${r.tagId}`}
          loading={segmentDataLoading}
          pagination={{ pageSize: 50 }}
          size="small"
          scroll={{ y: 400 }}
        />
      </Modal>
    </>
  );
};

// 条件编辑器组件
interface ConditionEditorProps {
  name: string;
  tags: TagType[];
}

const ConditionEditor: React.FC<ConditionEditorProps> = ({ name, tags }) => {
  return (
    <>
      <Form.Item name={[name, 'logic']} label="逻辑">
        <Select style={{ width: 100 }}>
          <Select.Option value="AND">AND</Select.Option>
          <Select.Option value="OR">OR</Select.Option>
        </Select>
      </Form.Item>

      <Form.List name={[name, 'conditions']}>
        {(fields, { add, remove }) => (
          <>
            {fields.map(({ key, name: fieldName, ...restField }) => (
              <Space key={key} style={{ display: 'flex', marginBottom: 8 }} align="baseline">
                <Form.Item {...restField} name={[fieldName, 'type']} rules={[{ required: true }]}>
                  <Select style={{ width: 100 }} placeholder="类型">
                    <Select.Option value="tag">标签</Select.Option>
                    <Select.Option value="duration">持续时间</Select.Option>
                  </Select>
                </Form.Item>

                <Form.Item
                  noStyle
                  shouldUpdate={(prev, cur) =>
                    prev?.[name]?.conditions?.[fieldName]?.type !==
                    cur?.[name]?.conditions?.[fieldName]?.type
                  }
                >
                  {({ getFieldValue }) => {
                    const type = getFieldValue([name, 'conditions', fieldName, 'type']);
                    if (type === 'tag') {
                      return (
                        <>
                          <Form.Item
                            {...restField}
                            name={[fieldName, 'tagId']}
                            rules={[{ required: true, message: '选择标签' }]}
                          >
                            <Select style={{ width: 150 }} placeholder="标签">
                              {tags.map((t) => (
                                <Select.Option key={t.tagId} value={t.tagId}>
                                  {t.name || t.tagId}
                                </Select.Option>
                              ))}
                            </Select>
                          </Form.Item>
                          <Form.Item
                            {...restField}
                            name={[fieldName, 'operator']}
                            rules={[{ required: true }]}
                          >
                            <Select style={{ width: 80 }} placeholder="操作">
                              <Select.Option value="gt">&gt;</Select.Option>
                              <Select.Option value="gte">≥</Select.Option>
                              <Select.Option value="lt">&lt;</Select.Option>
                              <Select.Option value="lte">≤</Select.Option>
                              <Select.Option value="eq">=</Select.Option>
                              <Select.Option value="ne">≠</Select.Option>
                            </Select>
                          </Form.Item>
                          <Form.Item
                            {...restField}
                            name={[fieldName, 'value']}
                            rules={[{ required: true, message: '输入值' }]}
                          >
                            <InputNumber placeholder="值" />
                          </Form.Item>
                        </>
                      );
                    } else if (type === 'duration') {
                      return (
                        <Form.Item
                          {...restField}
                          name={[fieldName, 'seconds']}
                          rules={[{ required: true, message: '输入秒数' }]}
                        >
                          <InputNumber placeholder="秒" min={1} addonAfter="秒" />
                        </Form.Item>
                      );
                    }
                    return null;
                  }}
                </Form.Item>

                <Button
                  type="text"
                  danger
                  icon={<MinusCircleOutlined />}
                  onClick={() => remove(fieldName)}
                />
              </Space>
            ))}
            <Button type="dashed" onClick={() => add({ type: 'tag', operator: 'gt' })} block>
              + 添加条件
            </Button>
          </>
        )}
      </Form.List>
    </>
  );
};

export default CollectionRulesPage;
