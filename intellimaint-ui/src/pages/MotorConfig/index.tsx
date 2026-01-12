// v64: Motor Configuration Page - Wizard for setting up motor fault prediction
import { useEffect, useState, useCallback } from 'react'
import {
  Card,
  Row,
  Col,
  Table,
  Tag,
  Button,
  Space,
  Modal,
  Form,
  Input,
  Select,
  InputNumber,
  message,
  Spin,
  Empty,
  Tabs,
  Popconfirm,
  Descriptions,
  List,
  Switch,
  Steps,
  Divider,
  Alert,
} from 'antd'
import {
  PlusOutlined,
  EditOutlined,
  DeleteOutlined,
  SettingOutlined,
  ThunderboltOutlined,
  ApiOutlined,
  CheckCircleOutlined,
  DatabaseOutlined,
  PlayCircleOutlined,
} from '@ant-design/icons'
import {
  getMotorModels,
  getMotorInstances,
  createMotorModel,
  createMotorInstance,
  deleteMotorModel,
  deleteMotorInstance,
  getMotorInstanceDetail,
  createParameterMapping,
  deleteParameterMapping,
  createOperationMode,
  deleteOperationMode,
  learnAllModes,
} from '../../api/motor'
import { getDevices } from '../../api/device'
import { getTags } from '../../api/tag'
import type {
  MotorModel,
  MotorInstance,
  MotorInstanceDetail,
  MotorParameterMapping,
  OperationMode,
  CreateMotorModelRequest,
  CreateMotorInstanceRequest,
  CreateParameterMappingRequest,
  CreateOperationModeRequest,
} from '../../types/motor'
import type { Device } from '../../types/device'
import type { Tag as TagEntity } from '../../types/tag'

// Motor type options
const motorTypes = [
  { value: 0, label: 'Induction Motor' },
  { value: 1, label: 'Synchronous Motor' },
  { value: 2, label: 'DC Motor' },
  { value: 3, label: 'PM Sync Motor' },
  { value: 4, label: 'Brushless DC' },
]

// Motor parameter types for mapping
const motorParameters = [
  { value: 0, label: 'U相电流' },
  { value: 1, label: 'V相电流' },
  { value: 2, label: 'W相电流' },
  { value: 3, label: '电流有效值' },
  { value: 10, label: 'UV线电压' },
  { value: 11, label: 'VW线电压' },
  { value: 12, label: 'WU线电压' },
  { value: 20, label: '转矩' },
  { value: 21, label: '转速' },
  { value: 22, label: '有功功率' },
  { value: 23, label: '无功功率' },
  { value: 24, label: '功率因数' },
  { value: 30, label: '轴向振动' },
  { value: 31, label: '径向振动' },
  { value: 32, label: '振动速度' },
  { value: 33, label: '振动加速度' },
  { value: 34, label: '运行状态' },
  { value: 40, label: '绕组温度' },
  { value: 41, label: '轴承温度' },
  { value: 42, label: '环境温度' },
]

const MotorConfig = () => {
  const [loading, setLoading] = useState(false)
  const [models, setModels] = useState<MotorModel[]>([])
  const [instances, setInstances] = useState<MotorInstance[]>([])
  const [devices, setDevices] = useState<Device[]>([])
  const [tags, setTags] = useState<TagEntity[]>([])
  const [activeTab, setActiveTab] = useState('models')

  // Modal states
  const [modelModalVisible, setModelModalVisible] = useState(false)
  const [instanceModalVisible, setInstanceModalVisible] = useState(false)
  const [mappingModalVisible, setMappingModalVisible] = useState(false)
  const [modeModalVisible, setModeModalVisible] = useState(false)
  const [detailModalVisible, setDetailModalVisible] = useState(false)
  const [selectedInstance, setSelectedInstance] = useState<MotorInstanceDetail | null>(null)
  const [selectedInstanceId, setSelectedInstanceId] = useState<string | null>(null)

  // Forms
  const [modelForm] = Form.useForm()
  const [instanceForm] = Form.useForm()
  const [mappingForm] = Form.useForm()
  const [modeForm] = Form.useForm()

  // Load all data
  const loadData = useCallback(async () => {
    setLoading(true)
    try {
      const [modelsRes, instancesRes, devicesData, tagsData] = await Promise.all([
        getMotorModels(),
        getMotorInstances(),
        getDevices(),
        getTags(),
      ])

      if (modelsRes.success) setModels(modelsRes.data)
      if (instancesRes.success) setInstances(instancesRes.data)
      // getDevices and getTags return arrays directly
      setDevices(devicesData || [])
      setTags(tagsData || [])
    } catch (err) {
      console.error('Failed to load data', err)
      message.error('Failed to load data')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    loadData()
  }, [loadData])

  // Load instance detail
  const loadInstanceDetail = async (instanceId: string) => {
    try {
      const res = await getMotorInstanceDetail(instanceId)
      if (res.success) {
        setSelectedInstance(res.data)
        setSelectedInstanceId(instanceId)
        setDetailModalVisible(true)
      }
    } catch (err) {
      message.error('Failed to load instance detail')
    }
  }

  // Create motor model
  const handleCreateModel = async (values: CreateMotorModelRequest) => {
    try {
      const res = await createMotorModel(values)
      if (res.success) {
        message.success('Motor model created')
        setModelModalVisible(false)
        modelForm.resetFields()
        loadData()
      } else {
        message.error(res.error || 'Failed to create model')
      }
    } catch (err) {
      message.error('Failed to create model')
    }
  }

  // Delete motor model
  const handleDeleteModel = async (modelId: string) => {
    try {
      const res = await deleteMotorModel(modelId)
      if (res.success) {
        message.success('Model deleted')
        loadData()
      } else {
        message.error(res.error || 'Failed to delete')
      }
    } catch (err) {
      message.error('Failed to delete model')
    }
  }

  // Create motor instance
  const handleCreateInstance = async (values: CreateMotorInstanceRequest) => {
    try {
      const res = await createMotorInstance(values)
      if (res.success) {
        message.success('Motor instance created')
        setInstanceModalVisible(false)
        instanceForm.resetFields()
        loadData()
      } else {
        message.error(res.error || 'Failed to create instance')
      }
    } catch (err) {
      message.error('Failed to create instance')
    }
  }

  // Delete motor instance
  const handleDeleteInstance = async (instanceId: string) => {
    try {
      const res = await deleteMotorInstance(instanceId)
      if (res.success) {
        message.success('Instance deleted')
        loadData()
      } else {
        message.error(res.error || 'Failed to delete')
      }
    } catch (err) {
      message.error('Failed to delete instance')
    }
  }

  // Create parameter mapping
  const handleCreateMapping = async (values: CreateParameterMappingRequest) => {
    if (!selectedInstanceId) return
    try {
      const res = await createParameterMapping(selectedInstanceId, values)
      if (res.success) {
        message.success('Mapping created')
        setMappingModalVisible(false)
        mappingForm.resetFields()
        loadInstanceDetail(selectedInstanceId)
      } else {
        message.error(res.error || 'Failed to create mapping')
      }
    } catch (err) {
      message.error('Failed to create mapping')
    }
  }

  // Delete parameter mapping
  const handleDeleteMapping = async (mappingId: string) => {
    if (!selectedInstanceId) return
    try {
      const res = await deleteParameterMapping(selectedInstanceId, mappingId)
      if (res.success) {
        message.success('Mapping deleted')
        loadInstanceDetail(selectedInstanceId)
      } else {
        message.error(res.error || 'Failed to delete')
      }
    } catch (err) {
      message.error('Failed to delete mapping')
    }
  }

  // Create operation mode
  const handleCreateMode = async (values: CreateOperationModeRequest) => {
    if (!selectedInstanceId) return
    try {
      const res = await createOperationMode(selectedInstanceId, values)
      if (res.success) {
        message.success('Operation mode created')
        setModeModalVisible(false)
        modeForm.resetFields()
        loadInstanceDetail(selectedInstanceId)
      } else {
        message.error(res.error || 'Failed to create mode')
      }
    } catch (err) {
      message.error('Failed to create mode')
    }
  }

  // Delete operation mode
  const handleDeleteMode = async (modeId: string) => {
    if (!selectedInstanceId) return
    try {
      const res = await deleteOperationMode(selectedInstanceId, modeId)
      if (res.success) {
        message.success('Mode deleted')
        loadInstanceDetail(selectedInstanceId)
      } else {
        message.error(res.error || 'Failed to delete')
      }
    } catch (err) {
      message.error('Failed to delete mode')
    }
  }

  // Start baseline learning for all modes
  const handleStartLearning = async (instanceId: string) => {
    try {
      const res = await learnAllModes(instanceId)
      if (res.success) {
        message.success(res.data?.message || '基线学习已启动')
        // 刷新实例详情以更新基线数量
        if (selectedInstance?.instance.instanceId === instanceId) {
          const detailRes = await getMotorInstanceDetail(instanceId)
          if (detailRes.success) {
            setSelectedInstance(detailRes.data)
          }
        }
      } else {
        message.error(res.error || '启动学习失败，请检查配置')
      }
    } catch (err: any) {
      const errorMsg = err?.response?.data?.error || err?.message || '网络错误'
      message.error(`启动基线学习失败: ${errorMsg}`)
    }
  }

  // Model columns
  const modelColumns = [
    {
      title: 'Name',
      dataIndex: 'name',
      key: 'name',
      render: (name: string) => (
        <Space>
          <DatabaseOutlined style={{ color: '#1890ff' }} />
          <span style={{ fontWeight: 500 }}>{name}</span>
        </Space>
      ),
    },
    {
      title: 'Type',
      dataIndex: 'type',
      key: 'type',
      render: (type: number) => {
        const t = motorTypes.find((m) => m.value === type)
        return <Tag color="blue">{t?.label || type}</Tag>
      },
    },
    {
      title: 'Power (kW)',
      dataIndex: 'ratedPower',
      key: 'ratedPower',
      render: (v: number) => v?.toFixed(1) || '-',
    },
    {
      title: 'Voltage (V)',
      dataIndex: 'ratedVoltage',
      key: 'ratedVoltage',
    },
    {
      title: 'Speed (RPM)',
      dataIndex: 'ratedSpeed',
      key: 'ratedSpeed',
    },
    {
      title: 'VFD Model',
      dataIndex: 'vfdModel',
      key: 'vfdModel',
      render: (v: string) => v || '-',
    },
    {
      title: 'Actions',
      key: 'actions',
      width: 100,
      render: (_: any, record: MotorModel) => (
        <Popconfirm
          title="Delete this model?"
          onConfirm={() => handleDeleteModel(record.modelId)}
        >
          <Button type="link" danger size="small" icon={<DeleteOutlined />}>
            Delete
          </Button>
        </Popconfirm>
      ),
    },
  ]

  // Instance columns
  const instanceColumns = [
    {
      title: 'Name',
      dataIndex: 'name',
      key: 'name',
      render: (name: string) => (
        <Space>
          <ThunderboltOutlined style={{ color: '#52c41a' }} />
          <span style={{ fontWeight: 500 }}>{name}</span>
        </Space>
      ),
    },
    {
      title: 'Device',
      dataIndex: 'deviceId',
      key: 'deviceId',
      render: (deviceId: string) => <Tag color="cyan">{deviceId}</Tag>,
    },
    {
      title: 'Model',
      key: 'modelId',
      render: (_: any, record: MotorInstance) => {
        const model = models.find((m) => m.modelId === record.modelId)
        return model?.name || record.modelId
      },
    },
    {
      title: 'Location',
      dataIndex: 'location',
      key: 'location',
      render: (v: string) => v || '-',
    },
    {
      title: 'Status',
      dataIndex: 'diagnosisEnabled',
      key: 'diagnosisEnabled',
      render: (enabled: boolean) => (
        <Tag color={enabled ? 'green' : 'default'}>
          {enabled ? 'Enabled' : 'Disabled'}
        </Tag>
      ),
    },
    {
      title: 'Actions',
      key: 'actions',
      width: 200,
      render: (_: any, record: MotorInstance) => (
        <Space>
          <Button
            type="link"
            size="small"
            icon={<SettingOutlined />}
            onClick={() => loadInstanceDetail(record.instanceId)}
          >
            Configure
          </Button>
          <Popconfirm
            title="Delete this instance?"
            onConfirm={() => handleDeleteInstance(record.instanceId)}
          >
            <Button type="link" danger size="small" icon={<DeleteOutlined />}>
              Delete
            </Button>
          </Popconfirm>
        </Space>
      ),
    },
  ]

  return (
    <div style={{ padding: 24 }}>
      {/* Header */}
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
            <SettingOutlined style={{ color: '#1890ff' }} />
            Motor Configuration
          </h2>
          <p style={{ margin: '8px 0 0', color: 'var(--color-text-muted)' }}>
            Configure motor models, instances, and parameter mappings for fault prediction
          </p>
        </div>
      </div>

      {/* Quick Stats */}
      <Row gutter={16} style={{ marginBottom: 24 }}>
        <Col span={6}>
          <Card size="small">
            <Space>
              <DatabaseOutlined style={{ fontSize: 24, color: '#1890ff' }} />
              <div>
                <div style={{ fontSize: 24, fontWeight: 'bold' }}>{models.length}</div>
                <div style={{ color: 'var(--color-text-muted)' }}>Motor Models</div>
              </div>
            </Space>
          </Card>
        </Col>
        <Col span={6}>
          <Card size="small">
            <Space>
              <ThunderboltOutlined style={{ fontSize: 24, color: '#52c41a' }} />
              <div>
                <div style={{ fontSize: 24, fontWeight: 'bold' }}>{instances.length}</div>
                <div style={{ color: 'var(--color-text-muted)' }}>Motor Instances</div>
              </div>
            </Space>
          </Card>
        </Col>
        <Col span={6}>
          <Card size="small">
            <Space>
              <ApiOutlined style={{ fontSize: 24, color: '#722ed1' }} />
              <div>
                <div style={{ fontSize: 24, fontWeight: 'bold' }}>{devices.length}</div>
                <div style={{ color: 'var(--color-text-muted)' }}>Available Devices</div>
              </div>
            </Space>
          </Card>
        </Col>
        <Col span={6}>
          <Card size="small">
            <Space>
              <CheckCircleOutlined style={{ fontSize: 24, color: '#13c2c2' }} />
              <div>
                <div style={{ fontSize: 24, fontWeight: 'bold' }}>{tags.length}</div>
                <div style={{ color: 'var(--color-text-muted)' }}>Available Tags</div>
              </div>
            </Space>
          </Card>
        </Col>
      </Row>

      {/* Main Content */}
      <Card>
        <Tabs
          activeKey={activeTab}
          onChange={setActiveTab}
          tabBarExtraContent={
            activeTab === 'models' ? (
              <Button
                type="primary"
                icon={<PlusOutlined />}
                onClick={() => setModelModalVisible(true)}
              >
                Add Motor Model
              </Button>
            ) : (
              <Button
                type="primary"
                icon={<PlusOutlined />}
                onClick={() => setInstanceModalVisible(true)}
                disabled={models.length === 0}
              >
                Add Motor Instance
              </Button>
            )
          }
          items={[
            {
              key: 'models',
              label: (
                <Space>
                  <DatabaseOutlined />
                  Motor Models ({models.length})
                </Space>
              ),
              children: (
                <Spin spinning={loading}>
                  <Table
                    dataSource={models}
                    columns={modelColumns}
                    rowKey="modelId"
                    pagination={{ pageSize: 10 }}
                    locale={{
                      emptyText: (
                        <Empty description="No motor models. Create one to get started." />
                      ),
                    }}
                  />
                </Spin>
              ),
            },
            {
              key: 'instances',
              label: (
                <Space>
                  <ThunderboltOutlined />
                  Motor Instances ({instances.length})
                </Space>
              ),
              children: (
                <Spin spinning={loading}>
                  {models.length === 0 ? (
                    <Alert
                      type="warning"
                      message="Please create a motor model first before adding instances."
                      showIcon
                      style={{ marginBottom: 16 }}
                    />
                  ) : null}
                  <Table
                    dataSource={instances}
                    columns={instanceColumns}
                    rowKey="instanceId"
                    pagination={{ pageSize: 10 }}
                    locale={{
                      emptyText: (
                        <Empty description="No motor instances. Add one to start monitoring." />
                      ),
                    }}
                  />
                </Spin>
              ),
            },
          ]}
        />
      </Card>

      {/* Create Motor Model Modal */}
      <Modal
        title="Create Motor Model"
        open={modelModalVisible}
        onCancel={() => setModelModalVisible(false)}
        footer={null}
        width={600}
      >
        <Form form={modelForm} layout="vertical" onFinish={handleCreateModel}>
          <Row gutter={16}>
            <Col span={12}>
              <Form.Item name="name" label="Model Name" rules={[{ required: true }]}>
                <Input placeholder="e.g., 15kW Induction Motor" />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="type" label="Motor Type" rules={[{ required: true }]}>
                <Select options={motorTypes} placeholder="Select type" />
              </Form.Item>
            </Col>
          </Row>
          <Form.Item name="description" label="Description">
            <Input.TextArea rows={2} placeholder="Optional description" />
          </Form.Item>
          <Divider>Rated Parameters</Divider>
          <Row gutter={16}>
            <Col span={8}>
              <Form.Item name="ratedPower" label="Power (kW)" rules={[{ required: true }]}>
                <InputNumber style={{ width: '100%' }} min={0} step={0.1} />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item name="ratedVoltage" label="Voltage (V)" rules={[{ required: true }]}>
                <InputNumber style={{ width: '100%' }} min={0} />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item name="ratedCurrent" label="Current (A)" rules={[{ required: true }]}>
                <InputNumber style={{ width: '100%' }} min={0} step={0.1} />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={16}>
            <Col span={8}>
              <Form.Item name="ratedSpeed" label="Speed (RPM)">
                <InputNumber style={{ width: '100%' }} min={0} />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item name="ratedFrequency" label="Frequency (Hz)">
                <InputNumber style={{ width: '100%' }} min={0} />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item name="polePairs" label="Pole Pairs">
                <InputNumber style={{ width: '100%' }} min={1} />
              </Form.Item>
            </Col>
          </Row>
          <Divider>VFD & Bearing (Optional)</Divider>
          <Row gutter={16}>
            <Col span={12}>
              <Form.Item name="vfdModel" label="VFD Model">
                <Input placeholder="e.g., ABB ACS580" />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="bearingModel" label="Bearing Model">
                <Input placeholder="e.g., SKF 6308" />
              </Form.Item>
            </Col>
          </Row>
          <Form.Item>
            <Space style={{ width: '100%', justifyContent: 'flex-end' }}>
              <Button onClick={() => setModelModalVisible(false)}>Cancel</Button>
              <Button type="primary" htmlType="submit">
                Create Model
              </Button>
            </Space>
          </Form.Item>
        </Form>
      </Modal>

      {/* Create Motor Instance Modal */}
      <Modal
        title="Create Motor Instance"
        open={instanceModalVisible}
        onCancel={() => setInstanceModalVisible(false)}
        footer={null}
        width={500}
      >
        <Form form={instanceForm} layout="vertical" onFinish={handleCreateInstance}>
          <Form.Item name="name" label="Instance Name" rules={[{ required: true }]}>
            <Input placeholder="e.g., Main Drive Motor #1" />
          </Form.Item>
          <Form.Item name="modelId" label="Motor Model" rules={[{ required: true }]}>
            <Select
              placeholder="Select a motor model"
              options={models.map((m) => ({ value: m.modelId, label: m.name }))}
            />
          </Form.Item>
          <Form.Item name="deviceId" label="Bound Device" rules={[{ required: true }]}>
            <Select
              placeholder="Select a device"
              showSearch
              optionFilterProp="label"
              options={devices.map((d) => ({ value: d.deviceId, label: `${d.deviceId} - ${d.name}` }))}
            />
          </Form.Item>
          <Form.Item name="location" label="Location">
            <Input placeholder="e.g., Workshop A - Line 1" />
          </Form.Item>
          <Form.Item name="assetNumber" label="Asset Number">
            <Input placeholder="e.g., MTR-2024-001" />
          </Form.Item>
          <Form.Item>
            <Space style={{ width: '100%', justifyContent: 'flex-end' }}>
              <Button onClick={() => setInstanceModalVisible(false)}>Cancel</Button>
              <Button type="primary" htmlType="submit">
                Create Instance
              </Button>
            </Space>
          </Form.Item>
        </Form>
      </Modal>

      {/* Instance Detail Modal */}
      <Modal
        title={
          <Space>
            <ThunderboltOutlined />
            {selectedInstance?.instance?.name || 'Motor Instance'}
          </Space>
        }
        open={detailModalVisible}
        onCancel={() => setDetailModalVisible(false)}
        footer={null}
        width={800}
      >
        {selectedInstance && (
          <Tabs
            items={[
              {
                key: 'info',
                label: 'Basic Info',
                children: (
                  <Descriptions bordered column={2} size="small">
                    <Descriptions.Item label="Instance ID">
                      {selectedInstance.instance.instanceId}
                    </Descriptions.Item>
                    <Descriptions.Item label="Device ID">
                      {selectedInstance.instance.deviceId}
                    </Descriptions.Item>
                    <Descriptions.Item label="Model">
                      {selectedInstance.model?.name || 'N/A'}
                    </Descriptions.Item>
                    <Descriptions.Item label="Location">
                      {selectedInstance.instance.location || 'N/A'}
                    </Descriptions.Item>
                    <Descriptions.Item label="Install Date">
                      {selectedInstance.instance.installDate || 'N/A'}
                    </Descriptions.Item>
                    <Descriptions.Item label="Asset Number">
                      {selectedInstance.instance.assetNumber || 'N/A'}
                    </Descriptions.Item>
                  </Descriptions>
                ),
              },
              {
                key: 'mappings',
                label: `Parameter Mappings (${selectedInstance.mappings?.length || 0})`,
                children: (
                  <>
                    <Button
                      type="primary"
                      icon={<PlusOutlined />}
                      style={{ marginBottom: 16 }}
                      onClick={() => setMappingModalVisible(true)}
                    >
                      Add Mapping
                    </Button>
                    <Table
                      dataSource={selectedInstance.mappings}
                      rowKey="mappingId"
                      size="small"
                      pagination={false}
                      columns={[
                        {
                          title: 'Parameter',
                          dataIndex: 'parameter',
                          key: 'parameter',
                          render: (p: number) => {
                            const param = motorParameters.find((m) => m.value === p)
                            return param?.label || p
                          },
                        },
                        {
                          title: 'Tag ID',
                          dataIndex: 'tagId',
                          key: 'tagId',
                          render: (t: string) => <Tag color="blue">{t}</Tag>,
                        },
                        {
                          title: 'Scale',
                          dataIndex: 'scaleFactor',
                          key: 'scaleFactor',
                        },
                        {
                          title: 'Diagnosis',
                          dataIndex: 'usedForDiagnosis',
                          key: 'usedForDiagnosis',
                          render: (v: boolean) => (
                            <Tag color={v ? 'green' : 'default'}>{v ? 'Yes' : 'No'}</Tag>
                          ),
                        },
                        {
                          title: 'Action',
                          key: 'action',
                          render: (_: any, record: MotorParameterMapping) => (
                            <Popconfirm
                              title="Delete this mapping?"
                              onConfirm={() => handleDeleteMapping(record.mappingId)}
                            >
                              <Button type="link" danger size="small">
                                Delete
                              </Button>
                            </Popconfirm>
                          ),
                        },
                      ]}
                    />
                  </>
                ),
              },
              {
                key: 'modes',
                label: `Operation Modes (${selectedInstance.modes?.length || 0})`,
                children: (
                  <>
                    <Button
                      type="primary"
                      icon={<PlusOutlined />}
                      style={{ marginBottom: 16 }}
                      onClick={() => setModeModalVisible(true)}
                    >
                      Add Mode
                    </Button>
                    <Table
                      dataSource={selectedInstance.modes}
                      rowKey="modeId"
                      size="small"
                      pagination={false}
                      columns={[
                        {
                          title: 'Name',
                          dataIndex: 'name',
                          key: 'name',
                        },
                        {
                          title: 'Trigger Tag',
                          dataIndex: 'triggerTagId',
                          key: 'triggerTagId',
                          render: (t: string) => <Tag>{t}</Tag>,
                        },
                        {
                          title: 'Range',
                          key: 'range',
                          render: (_: any, record: OperationMode) =>
                            `${record.triggerMinValue} - ${record.triggerMaxValue}`,
                        },
                        {
                          title: 'Enabled',
                          dataIndex: 'enabled',
                          key: 'enabled',
                          render: (v: boolean) => (
                            <Tag color={v ? 'green' : 'default'}>{v ? 'Yes' : 'No'}</Tag>
                          ),
                        },
                        {
                          title: 'Action',
                          key: 'action',
                          render: (_: any, record: OperationMode) => (
                            <Popconfirm
                              title="Delete this mode?"
                              onConfirm={() => handleDeleteMode(record.modeId)}
                            >
                              <Button type="link" danger size="small">
                                Delete
                              </Button>
                            </Popconfirm>
                          ),
                        },
                      ]}
                    />
                  </>
                ),
              },
              {
                key: 'learning',
                label: 'Baseline Learning',
                children: (
                  <div style={{ textAlign: 'center', padding: 24 }}>
                    <p>Start baseline learning to enable fault detection.</p>
                    <p style={{ color: 'var(--color-text-muted)', marginBottom: 24 }}>
                      This will analyze historical data and establish normal operating baselines.
                    </p>
                    <Button
                      type="primary"
                      icon={<PlayCircleOutlined />}
                      size="large"
                      onClick={() =>
                        handleStartLearning(selectedInstance.instance.instanceId)
                      }
                    >
                      Start Baseline Learning
                    </Button>
                    <div style={{ marginTop: 24 }}>
                      <Tag color="blue">Baselines: {selectedInstance.baselineCount || 0}</Tag>
                    </div>
                  </div>
                ),
              },
            ]}
          />
        )}
      </Modal>

      {/* Create Parameter Mapping Modal */}
      <Modal
        title="Add Parameter Mapping"
        open={mappingModalVisible}
        onCancel={() => setMappingModalVisible(false)}
        footer={null}
      >
        <Form form={mappingForm} layout="vertical" onFinish={handleCreateMapping}>
          <Form.Item name="parameter" label="Motor Parameter" rules={[{ required: true }]}>
            <Select
              placeholder="Select parameter"
              showSearch
              optionFilterProp="label"
              options={motorParameters}
            />
          </Form.Item>
          <Form.Item name="tagId" label="Tag ID" rules={[{ required: true }]}>
            <Select
              placeholder="Select tag"
              showSearch
              optionFilterProp="label"
              options={tags.map((t) => ({ value: t.tagId, label: `${t.tagId} - ${t.name || t.description || ''}` }))}
            />
          </Form.Item>
          <Row gutter={16}>
            <Col span={12}>
              <Form.Item name="scaleFactor" label="Scale Factor" initialValue={1.0}>
                <InputNumber style={{ width: '100%' }} step={0.1} />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="offset" label="Offset" initialValue={0}>
                <InputNumber style={{ width: '100%' }} />
              </Form.Item>
            </Col>
          </Row>
          <Form.Item
            name="usedForDiagnosis"
            label="Use for Diagnosis"
            valuePropName="checked"
            initialValue={true}
          >
            <Switch />
          </Form.Item>
          <Form.Item>
            <Space style={{ width: '100%', justifyContent: 'flex-end' }}>
              <Button onClick={() => setMappingModalVisible(false)}>Cancel</Button>
              <Button type="primary" htmlType="submit">
                Add Mapping
              </Button>
            </Space>
          </Form.Item>
        </Form>
      </Modal>

      {/* Create Operation Mode Modal */}
      <Modal
        title="Add Operation Mode"
        open={modeModalVisible}
        onCancel={() => setModeModalVisible(false)}
        footer={null}
      >
        <Form form={modeForm} layout="vertical" onFinish={handleCreateMode}>
          <Form.Item name="name" label="Mode Name" rules={[{ required: true }]}>
            <Input placeholder="e.g., Normal Operation" />
          </Form.Item>
          <Form.Item name="description" label="Description">
            <Input.TextArea rows={2} placeholder="Optional description" />
          </Form.Item>
          <Form.Item name="triggerTagId" label="Trigger Tag" rules={[{ required: true }]}>
            <Select
              placeholder="Select tag"
              showSearch
              optionFilterProp="label"
              options={tags.map((t) => ({ value: t.tagId, label: t.tagId }))}
            />
          </Form.Item>
          <Row gutter={16}>
            <Col span={12}>
              <Form.Item name="triggerMinValue" label="Min Value" rules={[{ required: true }]}>
                <InputNumber style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="triggerMaxValue" label="Max Value" rules={[{ required: true }]}>
                <InputNumber style={{ width: '100%' }} />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={16}>
            <Col span={12}>
              <Form.Item name="minDurationMs" label="Min Duration (ms)" initialValue={5000}>
                <InputNumber style={{ width: '100%' }} min={0} />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="priority" label="Priority" initialValue={1}>
                <InputNumber style={{ width: '100%' }} min={1} />
              </Form.Item>
            </Col>
          </Row>
          <Form.Item>
            <Space style={{ width: '100%', justifyContent: 'flex-end' }}>
              <Button onClick={() => setModeModalVisible(false)}>Cancel</Button>
              <Button type="primary" htmlType="submit">
                Add Mode
              </Button>
            </Space>
          </Form.Item>
        </Form>
      </Modal>
    </div>
  )
}

export default MotorConfig
