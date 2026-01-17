import React, { useCallback, useEffect, useState } from 'react'
import {
  Button,
  Card,
  Checkbox,
  Collapse,
  Form,
  Input,
  message,
  Modal,
  Popconfirm,
  Select,
  Space,
  Switch,
  Table,
  Tag,
  Typography
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { PlusOutlined, KeyOutlined } from '@ant-design/icons'
import type { User, CreateUserRequest, UpdateUserRequest, ResetPasswordRequest } from '../../types/user'
import { UserRoleOptions } from '../../types/user'
import { getUsers, createUser, updateUser, disableUser, enableUser, resetPassword } from '../../api/user'
import { CheckOutlined, StopOutlined } from '@ant-design/icons'

type Mode = 'create' | 'edit'

function formatUtcMs(ms: number | null) {
  if (!ms) return '-'
  try {
    return new Date(ms).toLocaleString('zh-CN')
  } catch {
    return String(ms)
  }
}

function getRoleTagColor(role: string): string {
  switch (role) {
    case 'Admin': return 'red'
    case 'Operator': return 'blue'
    case 'Viewer': return 'green'
    default: return 'default'
  }
}

function getRoleTextColor(role: string): string {
  switch (role) {
    case 'Admin': return '#cf1322'
    case 'Operator': return '#1677ff'
    case 'Viewer': return '#389e0d'
    default: return '#666'
  }
}

function getRoleLabel(role: string): string {
  const option = UserRoleOptions.find(o => o.value === role)
  return option?.label ?? role
}

// 角色权限配置
const rolePermissions: Record<string, { description: string; permissions: string[] }> = {
  Admin: {
    description: '管理员 - 拥有系统全部权限',
    permissions: [
      '用户管理（创建、编辑、禁用用户）',
      '设备管理（添加、编辑、删除设备）',
      '告警规则配置',
      '系统设置管理',
      '查看审计日志',
      '数据查询与导出',
      '实时监控'
    ]
  },
  Operator: {
    description: '操作员 - 日常运维操作权限',
    permissions: [
      '设备管理（添加、编辑设备）',
      '告警确认与处理',
      '告警规则配置',
      '数据查询与导出',
      '实时监控'
    ]
  },
  Viewer: {
    description: '观察者 - 只读查看权限',
    permissions: [
      '查看设备列表',
      '查看告警信息',
      '数据查询（只读）',
      '实时监控（只读）'
    ]
  }
}

export default function UserManagement() {
  const [loading, setLoading] = useState(false)
  const [users, setUsers] = useState<User[]>([])

  const [modalOpen, setModalOpen] = useState(false)
  const [modalMode, setModalMode] = useState<Mode>('create')
  const [current, setCurrent] = useState<User | null>(null)

  const [resetModalOpen, setResetModalOpen] = useState(false)
  const [resetUserId, setResetUserId] = useState<string | null>(null)
  const [resetUsername, setResetUsername] = useState<string>('')
  const [selectedRole, setSelectedRole] = useState<string>('Viewer')

  const [form] = Form.useForm()
  const [resetForm] = Form.useForm()

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const resp = await getUsers()
      if (!resp.success) {
        message.error(resp.error ?? '获取用户列表失败')
        setUsers([])
        return
      }
      setUsers(resp.data ?? [])
    } catch (e: unknown) {
      message.error((e as Error)?.message ?? '获取用户列表失败')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    load()
  }, [load])

  const openCreate = () => {
    setModalMode('create')
    setCurrent(null)
    form.resetFields()
    setSelectedRole('Viewer')
    setModalOpen(true)
  }

  const openEdit = (record: User) => {
    setModalMode('edit')
    setCurrent(record)
    form.setFieldsValue({
      username: record.username,
      displayName: record.displayName ?? '',
      role: record.role,
      enabled: record.enabled
    })
    setSelectedRole(record.role)
    setModalOpen(true)
  }

  const openResetPassword = (record: User) => {
    setResetUserId(record.userId)
    setResetUsername(record.username)
    resetForm.resetFields()
    setResetModalOpen(true)
  }

  const handleSubmit = async () => {
    try {
      const values = await form.validateFields()

      if (modalMode === 'create') {
        const req: CreateUserRequest = {
          username: values.username,
          password: values.password,
          role: values.role,
          displayName: values.displayName || undefined
        }
        const resp = await createUser(req)
        if (!resp.success) {
          message.error(resp.error ?? '创建失败')
          return
        }
        message.success('用户创建成功')
      } else {
        if (!current) return
        const req: UpdateUserRequest = {
          displayName: values.displayName || undefined,
          role: values.role,
          enabled: values.enabled
        }
        const resp = await updateUser(current.userId, req)
        if (!resp.success) {
          message.error(resp.error ?? '更新失败')
          return
        }
        message.success('用户更新成功')
      }

      setModalOpen(false)
      load()
    } catch (e: unknown) {
      const formErr = e as { errorFields?: unknown[] }
      if (formErr.errorFields) return
      message.error((e as Error)?.message ?? '操作失败')
    }
  }

  const handleDisable = async (record: User) => {
    try {
      const resp = await disableUser(record.userId)
      if (!resp.success) {
        message.error(resp.error ?? '禁用失败')
        return
      }
      message.success('用户已禁用')
      load()
    } catch (e: unknown) {
      message.error((e as Error)?.message ?? '禁用失败')
    }
  }

  const handleEnable = async (record: User) => {
    try {
      const resp = await enableUser(record.userId)
      if (!resp.success) {
        message.error(resp.error ?? '启用失败')
        return
      }
      message.success('用户已启用')
      load()
    } catch (e: unknown) {
      message.error((e as Error)?.message ?? '启用失败')
    }
  }

  const handleResetPassword = async () => {
    try {
      const values = await resetForm.validateFields()
      if (!resetUserId) return

      const req: ResetPasswordRequest = {
        newPassword: values.newPassword
      }
      const resp = await resetPassword(resetUserId, req)
      if (!resp.success) {
        message.error(resp.error ?? '重置密码失败')
        return
      }
      message.success('密码已重置')
      setResetModalOpen(false)
    } catch (e: unknown) {
      const formErr = e as { errorFields?: unknown[] }
      if (formErr.errorFields) return
      message.error((e as Error)?.message ?? '重置密码失败')
    }
  }

  const columns: ColumnsType<User> = [
    {
      title: '用户名',
      dataIndex: 'username',
      key: 'username',
      width: 150
    },
    {
      title: '显示名称',
      dataIndex: 'displayName',
      key: 'displayName',
      width: 150,
      render: (v) => v ?? '-'
    },
    {
      title: '角色',
      dataIndex: 'role',
      key: 'role',
      width: 100,
      render: (role: string) => (
        <Tag color={getRoleTagColor(role)}>{getRoleLabel(role)}</Tag>
      )
    },
    {
      title: '状态',
      dataIndex: 'enabled',
      key: 'enabled',
      width: 80,
      render: (enabled: boolean) => (
        <Tag color={enabled ? 'success' : 'error'}>
          {enabled ? '启用' : '禁用'}
        </Tag>
      )
    },
    {
      title: '创建时间',
      dataIndex: 'createdUtc',
      key: 'createdUtc',
      width: 180,
      render: formatUtcMs
    },
    {
      title: '最后登录',
      dataIndex: 'lastLoginUtc',
      key: 'lastLoginUtc',
      width: 180,
      render: formatUtcMs
    },
    {
      title: '操作',
      key: 'actions',
      width: 260,
      render: (_, record) => (
        <Space size="small">
          <Button size="small" onClick={() => openEdit(record)}>
            编辑
          </Button>
          <Button
            size="small"
            icon={<KeyOutlined />}
            onClick={() => openResetPassword(record)}
          >
            重置密码
          </Button>
          {record.enabled ? (
            <Popconfirm
              title="确定要禁用该用户吗？"
              onConfirm={() => handleDisable(record)}
              okText="确定"
              cancelText="取消"
            >
              <Button size="small" danger icon={<StopOutlined />}>
                禁用
              </Button>
            </Popconfirm>
          ) : (
            <Popconfirm
              title="确定要启用该用户吗？"
              onConfirm={() => handleEnable(record)}
              okText="确定"
              cancelText="取消"
            >
              <Button size="small" type="primary" ghost icon={<CheckOutlined />}>
                启用
              </Button>
            </Popconfirm>
          )}
        </Space>
      )
    }
  ]

  return (
    <>
      {/* 页面标题 */}
      <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', marginBottom: 24 }}>
        <div>
          <h1 style={{ fontSize: 24, fontWeight: 700, color: 'var(--color-text-primary)', margin: '0 0 8px 0' }}>用户管理</h1>
          <p style={{ fontSize: 14, color: 'var(--color-text-muted)', margin: 0 }}>管理系统用户账号和权限</p>
        </div>
        <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
          新建用户
        </Button>
      </div>

      <Card>
        <Table
          loading={loading}
          columns={columns}
          dataSource={users}
          rowKey="userId"
          pagination={{ pageSize: 20, showSizeChanger: true, showTotal: (t) => `共 ${t} 条` }}
          size="small"
        />
      </Card>

      {/* 创建/编辑对话框 */}
      <Modal
        title={modalMode === 'create' ? '新建用户' : '编辑用户'}
        open={modalOpen}
        onOk={handleSubmit}
        onCancel={() => setModalOpen(false)}
        destroyOnClose
        width={500}
      >
        <Form form={form} layout="vertical" preserve={false}>
          <Form.Item
            name="username"
            label="用户名"
            rules={[
              { required: true, message: '请输入用户名' },
              { min: 3, message: '用户名至少3个字符' }
            ]}
          >
            <Input disabled={modalMode === 'edit'} placeholder="请输入用户名" />
          </Form.Item>

          {modalMode === 'create' && (
            <Form.Item
              name="password"
              label="密码"
              rules={[
                { required: true, message: '请输入密码' },
                { min: 8, message: '密码至少8个字符' },
                { pattern: /^(?=.*[A-Za-z])(?=.*\d).+$/, message: '密码必须包含字母和数字' }
              ]}
            >
              <Input.Password placeholder="请输入密码（至少8位，含字母和数字）" />
            </Form.Item>
          )}

          <Form.Item
            name="displayName"
            label="显示名称"
          >
            <Input placeholder="请输入显示名称（可选）" />
          </Form.Item>

          <Form.Item
            name="role"
            label="角色"
            rules={[{ required: true, message: '请选择角色' }]}
          >
            <Select
              options={UserRoleOptions}
              placeholder="请选择角色"
              onChange={(value) => setSelectedRole(value)}
            />
          </Form.Item>

          {/* 权限说明 */}
          {selectedRole && rolePermissions[selectedRole] && (
            <div style={{
              marginBottom: 16,
              padding: 12,
              background: 'var(--color-bg-subtle, #f5f5f5)',
              borderRadius: 8,
              border: '1px solid var(--color-border, #e8e8e8)'
            }}>
              <div style={{ marginBottom: 8, fontWeight: 500, color: getRoleTextColor(selectedRole) }}>
                {rolePermissions[selectedRole].description}
              </div>
              <div style={{ fontSize: 12, color: 'var(--color-text-muted, #666)' }}>
                <div style={{ marginBottom: 4 }}>权限列表：</div>
                <ul style={{ margin: 0, paddingLeft: 20 }}>
                  {rolePermissions[selectedRole].permissions.map((perm, idx) => (
                    <li key={idx} style={{ marginBottom: 2 }}>{perm}</li>
                  ))}
                </ul>
              </div>
            </div>
          )}

          {modalMode === 'edit' && (
            <Form.Item
              name="enabled"
              label="启用状态"
              valuePropName="checked"
            >
              <Switch checkedChildren="启用" unCheckedChildren="禁用" />
            </Form.Item>
          )}
        </Form>
      </Modal>

      {/* 重置密码对话框 */}
      <Modal
        title={`重置密码 - ${resetUsername}`}
        open={resetModalOpen}
        onOk={handleResetPassword}
        onCancel={() => setResetModalOpen(false)}
        destroyOnClose
        width={400}
      >
        <Form form={resetForm} layout="vertical" preserve={false}>
          <Form.Item
            name="newPassword"
            label="新密码"
            rules={[
              { required: true, message: '请输入新密码' },
              { min: 8, message: '密码至少8个字符' },
              { pattern: /^(?=.*[A-Za-z])(?=.*\d).+$/, message: '密码必须包含字母和数字' }
            ]}
          >
            <Input.Password placeholder="请输入新密码（至少8位，含字母和数字）" />
          </Form.Item>
          <Form.Item
            name="confirmPassword"
            label="确认密码"
            dependencies={['newPassword']}
            rules={[
              { required: true, message: '请确认新密码' },
              ({ getFieldValue }) => ({
                validator(_, value) {
                  if (!value || getFieldValue('newPassword') === value) {
                    return Promise.resolve()
                  }
                  return Promise.reject(new Error('两次输入的密码不一致'))
                }
              })
            ]}
          >
            <Input.Password placeholder="请再次输入新密码" />
          </Form.Item>
        </Form>
      </Modal>
    </>
  )
}
