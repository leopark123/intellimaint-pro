import { useState, useMemo, useEffect } from 'react'
import { Outlet, useNavigate, useLocation } from 'react-router-dom'
import { Layout, Menu, Dropdown, Avatar, Badge, message, Tooltip, Switch } from 'antd'
import {
  LayoutDashboard,
  LineChart,
  Server,
  Tag,
  AlertTriangle,
  Zap,
  Heart,
  FileSearch,
  Settings,
  Users,
  Bell,
  User,
  LogOut,
  ChevronLeft,
  ChevronRight,
  Network,
  Wrench,
  TrendingUp,
  Brain,
  Database,
  Settings2,
  Shield,
  RotateCcw,
  ListChecks,
  Sun,
  Moon,
  Layers,
  Activity,
  Cpu,
} from 'lucide-react'
import { useAuth } from '../../store/authStore'
import useTheme from '../../hooks/useTheme'
import type { MenuProps } from 'antd'

const { Header, Sider, Content } = Layout

// 菜单项类型
interface MenuItem {
  key: string
  icon: React.ReactNode
  label: string
  roles?: string[]
}

// 菜单分组类型
interface MenuGroup {
  key: string
  label: string
  icon: React.ReactNode
  children: MenuItem[]
}

// 分组菜单配置
const menuGroups: MenuGroup[] = [
  {
    key: 'analytics',
    label: '智能分析',
    icon: <Brain size={18} />,
    children: [
      { key: '/dashboard', icon: <LayoutDashboard size={18} />, label: '实时监控' },
      { key: '/health-assessment', icon: <Heart size={18} />, label: '健康评估' },
      { key: '/motor-prediction', icon: <Cpu size={18} />, label: '电机故障预测' },
      { key: '/prediction-alerts', icon: <Activity size={18} />, label: '预测预警' },
      { key: '/anomaly-detection', icon: <AlertTriangle size={18} />, label: '异常检测' },
      { key: '/predictive-maintenance', icon: <Wrench size={18} />, label: '预测性维护' },
      { key: '/model-optimization', icon: <TrendingUp size={18} />, label: '持续优化' },
      { key: '/knowledge-graph', icon: <Network size={18} />, label: '知识图谱' }
    ]
  },
  {
    key: 'data',
    label: '数据中心',
    icon: <Database size={18} />,
    children: [
      { key: '/data-explorer', icon: <LineChart size={18} />, label: '数据查询' },
      { key: '/cycle-analysis', icon: <RotateCcw size={18} />, label: '周期分析' }
    ]
  },
  {
    key: 'assets',
    label: '资产配置',
    icon: <Settings2 size={18} />,
    children: [
      { key: '/device-management', icon: <Server size={18} />, label: '设备管理' },
      { key: '/tag-management', icon: <Tag size={18} />, label: '标签管理' },
      { key: '/collection-rules', icon: <ListChecks size={18} />, label: '采集规则' }
    ]
  },
  {
    key: 'alarms',
    label: '告警中心',
    icon: <Bell size={18} />,
    children: [
      { key: '/alarm-management', icon: <Bell size={18} />, label: '告警管理' },
      { key: '/alarm-groups', icon: <Layers size={18} />, label: '告警聚合' },
      { key: '/alarm-rules', icon: <Zap size={18} />, label: '告警规则' }
    ]
  },
  {
    key: 'system',
    label: '系统管理',
    icon: <Shield size={18} />,
    children: [
      { key: '/system-health', icon: <Heart size={18} />, label: '系统健康' },
      { key: '/edge-config', icon: <Server size={18} />, label: 'Edge配置', roles: ['Admin', 'Operator'] },
      { key: '/audit-log', icon: <FileSearch size={18} />, label: '审计日志' },
      { key: '/user-management', icon: <Users size={18} />, label: '用户管理', roles: ['Admin'] },
      { key: '/settings', icon: <Settings size={18} />, label: '系统设置' }
    ]
  }
]

// 存储展开状态的 key
const OPEN_KEYS_STORAGE_KEY = 'intellimaint_menu_open_keys'

export default function MainLayout() {
  const [collapsed, setCollapsed] = useState(false)
  const [openKeys, setOpenKeys] = useState<string[]>(() => {
    // 从 localStorage 恢复展开状态
    const saved = localStorage.getItem(OPEN_KEYS_STORAGE_KEY)
    return saved ? JSON.parse(saved) : ['analytics']
  })
  const navigate = useNavigate()
  const location = useLocation()
  const { auth, logout } = useAuth()
  const { theme, isDark, toggleTheme } = useTheme()

  // 保存展开状态
  useEffect(() => {
    localStorage.setItem(OPEN_KEYS_STORAGE_KEY, JSON.stringify(openKeys))
  }, [openKeys])

  // 根据当前路径自动展开对应分组
  useEffect(() => {
    const currentGroup = menuGroups.find(group => 
      group.children.some(item => location.pathname === item.key)
    )
    if (currentGroup && !openKeys.includes(currentGroup.key)) {
      setOpenKeys(prev => [...prev, currentGroup.key])
    }
  }, [location.pathname])

  // 根据角色过滤菜单并转换为 Ant Design 格式
  const antMenuItems: MenuProps['items'] = useMemo(() => {
    return menuGroups.map(group => {
      // 过滤子菜单项
      const filteredChildren = group.children.filter(item => {
        if (!item.roles) return true
        return item.roles.includes(auth.role || '')
      })

      // 如果没有可见的子菜单项，则不显示该分组
      if (filteredChildren.length === 0) return null

      return {
        key: group.key,
        icon: group.icon,
        label: group.label,
        children: filteredChildren.map(item => ({
          key: item.key,
          icon: item.icon,
          label: item.label
        }))
      }
    }).filter(Boolean)
  }, [auth.role])

  const handleMenuClick: MenuProps['onClick'] = ({ key }) => {
    // 只有路由路径才导航
    if (key.startsWith('/')) {
      navigate(key)
    }
  }

  const handleOpenChange = (keys: string[]) => {
    setOpenKeys(keys)
  }

  const handleLogout = async () => {
    await logout()
    message.success('已退出登录')
    navigate('/login')
  }

  const userMenuItems: MenuProps['items'] = [
    {
      key: 'user-info',
      label: (
        <div style={{ padding: '8px 0' }}>
          <div style={{ fontWeight: 600, color: 'var(--color-text-primary)' }}>{auth.username}</div>
          <div style={{ fontSize: 12, color: 'var(--color-text-muted)' }}>{auth.role}</div>
        </div>
      ),
      disabled: true
    },
    { type: 'divider' },
    {
      key: 'logout',
      icon: <LogOut size={16} />,
      label: '退出登录',
      onClick: handleLogout
    }
  ]

  return (
    <Layout style={{ minHeight: '100vh', background: 'var(--color-bg-darker)' }}>
      {/* 侧边栏 */}
      <Sider
        trigger={null}
        collapsible
        collapsed={collapsed}
        width={256}
        collapsedWidth={72}
        style={{
          background: 'var(--color-bg-dark)',
          borderRight: '1px solid var(--color-border)',
          position: 'fixed',
          height: '100vh',
          left: 0,
          top: 0,
          zIndex: 100,
          display: 'flex',
          flexDirection: 'column'
        }}
      >
        {/* Logo 区域 - 固定高度 */}
        <div style={{
          height: 64,
          padding: '12px 16px',
          borderBottom: '1px solid var(--color-border)',
          display: 'flex',
          alignItems: 'center',
          gap: 12,
          flexShrink: 0
        }}>
          <div style={{
            width: 40,
            height: 40,
            background: 'linear-gradient(135deg, #1A237E 0%, #283593 100%)',
            borderRadius: 10,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            flexShrink: 0
          }}>
            <Network size={24} color="#fff" />
          </div>
          {!collapsed && (
            <div>
              <div style={{ 
                fontSize: 18, 
                fontWeight: 700, 
                color: 'var(--color-text-primary)',
                lineHeight: 1.2
              }}>
                IntelliMaint
              </div>
              <div style={{ 
                fontSize: 11, 
                color: 'var(--color-text-muted)',
                lineHeight: 1.2
              }}>
                智能维护平台
              </div>
            </div>
          )}
        </div>

        {/* 菜单区域 - 可滚动 */}
        <div style={{
          flex: 1,
          overflowY: 'auto',
          overflowX: 'hidden'
        }}>
          <Menu
            mode="inline"
            selectedKeys={[location.pathname]}
            openKeys={collapsed ? [] : openKeys}
            onOpenChange={handleOpenChange}
            items={antMenuItems}
            onClick={handleMenuClick}
            style={{
              background: 'transparent',
              border: 'none',
              padding: '8px'
            }}
            theme="dark"
          />
        </div>

        {/* 底部状态 - 固定高度，不再用 absolute */}
        <div style={{
          flexShrink: 0,
          padding: 16,
          borderTop: '1px solid var(--color-border)'
        }}>
          {!collapsed ? (
            <div style={{
              background: 'var(--color-bg-card)',
              borderRadius: 8,
              padding: 12
            }}>
              <div style={{ fontSize: 12, color: 'var(--color-text-muted)', marginBottom: 8 }}>
                系统状态
              </div>
              <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <div className="status-dot online" />
                <span style={{ fontSize: 14, color: 'var(--color-text-primary)' }}>正常运行</span>
              </div>
            </div>
          ) : (
            <Tooltip title="系统正常运行" placement="right">
              <div style={{
                width: 40,
                height: 40,
                borderRadius: 8,
                background: 'var(--color-bg-card)',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                margin: '0 auto'
              }}>
                <div className="status-dot online" />
              </div>
            </Tooltip>
          )}
        </div>
      </Sider>

      {/* 主内容区 */}
      <Layout style={{ 
        marginLeft: collapsed ? 72 : 256,
        transition: 'margin-left 0.2s',
        background: 'var(--color-bg-darker)'
      }}>
        {/* 顶部导航 */}
        <Header style={{
          height: 64,
          padding: '0 24px',
          background: 'var(--color-bg-dark)',
          borderBottom: '1px solid var(--color-border)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          position: 'sticky',
          top: 0,
          zIndex: 99
        }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 16 }}>
            {/* 折叠按钮 */}
            <div
              onClick={() => setCollapsed(!collapsed)}
              style={{
                width: 36,
                height: 36,
                borderRadius: 8,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                cursor: 'pointer',
                transition: 'background 0.2s'
              }}
              onMouseEnter={(e) => e.currentTarget.style.background = 'var(--hover-overlay)'}
              onMouseLeave={(e) => e.currentTarget.style.background = 'transparent'}
            >
              {collapsed ? <ChevronRight size={20} color="var(--color-text-muted)" /> : <ChevronLeft size={20} color="var(--color-text-muted)" />}
            </div>
            
            {/* 标题 */}
            <div>
              <h1 style={{ 
                fontSize: 18, 
                fontWeight: 600, 
                color: 'var(--color-text-primary)',
                margin: 0
              }}>
                工业AI预测性维护系统
              </h1>
            </div>
            
            {/* 版本标签 */}
            <span style={{
              padding: '4px 12px',
              background: 'rgba(26, 35, 126, 0.2)',
              color: '#00BCD4',
              fontSize: 12,
              borderRadius: 20,
              fontWeight: 500
            }}>
              V2.0
            </span>
          </div>

          {/* 右侧操作区 */}
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            {/* 主题切换按钮 */}
            <Tooltip title={isDark ? '切换到浅色模式' : '切换到深色模式'}>
              <div
                className="theme-toggle"
                onClick={toggleTheme}
                style={{
                  width: 40,
                  height: 40,
                  borderRadius: 8,
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  cursor: 'pointer',
                  transition: 'all 0.3s',
                  background: 'var(--hover-overlay)'
                }}
                onMouseEnter={(e) => e.currentTarget.style.background = 'var(--active-overlay)'}
                onMouseLeave={(e) => e.currentTarget.style.background = 'var(--hover-overlay)'}
              >
                {isDark ? (
                  <Sun size={20} color="var(--color-warning)" />
                ) : (
                  <Moon size={20} color="var(--color-primary)" />
                )}
              </div>
            </Tooltip>

            {/* 通知按钮 */}
            <Badge count={3} size="small">
              <div
                style={{
                  width: 40,
                  height: 40,
                  borderRadius: 8,
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  cursor: 'pointer',
                  transition: 'background 0.2s'
                }}
                onMouseEnter={(e) => e.currentTarget.style.background = 'var(--hover-overlay)'}
                onMouseLeave={(e) => e.currentTarget.style.background = 'transparent'}
                onClick={() => navigate('/alarm-management')}
              >
                <Bell size={20} color="var(--color-text-muted)" />
              </div>
            </Badge>

            {/* 设置按钮 */}
            <div
              style={{
                width: 40,
                height: 40,
                borderRadius: 8,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                cursor: 'pointer',
                transition: 'background 0.2s'
              }}
              onMouseEnter={(e) => e.currentTarget.style.background = 'var(--hover-overlay)'}
              onMouseLeave={(e) => e.currentTarget.style.background = 'transparent'}
              onClick={() => navigate('/settings')}
            >
              <Settings size={20} color="var(--color-text-muted)" />
            </div>

            {/* 用户下拉菜单 */}
            <Dropdown menu={{ items: userMenuItems }} placement="bottomRight" trigger={['click']}>
              <div
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 8,
                  padding: '6px 12px',
                  borderRadius: 8,
                  cursor: 'pointer',
                  transition: 'background 0.2s'
                }}
                onMouseEnter={(e) => e.currentTarget.style.background = 'var(--hover-overlay)'}
                onMouseLeave={(e) => e.currentTarget.style.background = 'transparent'}
              >
                <Avatar 
                  size={32} 
                  icon={<User size={18} />}
                  style={{ background: '#1A237E' }}
                />
                <span style={{ color: 'var(--color-text-secondary)', fontSize: 14 }}>{auth.username}</span>
              </div>
            </Dropdown>
          </div>
        </Header>

        {/* 内容区 */}
        <Content style={{
          padding: 24,
          minHeight: 'calc(100vh - 64px)',
          background: 'var(--color-bg-darker)'
        }}>
          <Outlet />
        </Content>
      </Layout>
    </Layout>
  )
}
