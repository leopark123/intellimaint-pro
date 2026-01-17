import { useState, useEffect } from 'react'
import { Form, Input, Button, message, Tooltip } from 'antd'
import { useNavigate, useLocation } from 'react-router-dom'
import { Network, User, Lock, LogIn, Sun, Moon } from 'lucide-react'
import { login } from '../../api/auth'
import { useAuth } from '../../store/authStore'
import useTheme from '../../hooks/useTheme'
import type { LoginRequest } from '../../types/auth'

export default function LoginPage() {
  const [loading, setLoading] = useState(false)
  const navigate = useNavigate()
  const location = useLocation()
  const { login: setAuth } = useAuth()
  const { isDark, toggleTheme } = useTheme()

  const from = (location.state as { from?: string })?.from || '/'

  const handleSubmit = async (values: LoginRequest) => {
    setLoading(true)
    try {
      const response = await login(values)
      setAuth(response)
      message.success(`欢迎回来，${response.username}！`)
      navigate(from, { replace: true })
    } catch (error: unknown) {
      const axiosErr = error as { response?: { status?: number } }
      if (axiosErr.response?.status === 401) {
        message.error('用户名或密码错误')
      } else if (axiosErr.response?.status === 429) {
        message.error('账号已锁定，请稍后重试')
      } else {
        message.error('登录失败，请稍后重试')
      }
    } finally {
      setLoading(false)
    }
  }

  return (
    <div style={{
      minHeight: '100vh',
      display: 'flex',
      justifyContent: 'center',
      alignItems: 'center',
      background: 'var(--color-bg-darker)',
      position: 'relative',
      overflow: 'hidden',
      transition: 'background 0.3s'
    }}>
      {/* 主题切换按钮 */}
      <Tooltip title={isDark ? '切换到浅色模式' : '切换到深色模式'}>
        <div
          onClick={toggleTheme}
          style={{
            position: 'absolute',
            top: 24,
            right: 24,
            width: 44,
            height: 44,
            borderRadius: 22,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            cursor: 'pointer',
            background: 'var(--color-bg-card)',
            border: '1px solid var(--color-border)',
            transition: 'all 0.3s',
            zIndex: 10
          }}
        >
          {isDark ? (
            <Sun size={20} color="var(--color-warning)" />
          ) : (
            <Moon size={20} color="var(--color-primary)" />
          )}
        </div>
      </Tooltip>

      {/* 背景装饰 */}
      <div style={{
        position: 'absolute',
        top: '20%',
        left: '10%',
        width: 400,
        height: 400,
        background: isDark 
          ? 'radial-gradient(circle, rgba(26, 35, 126, 0.3) 0%, transparent 70%)'
          : 'radial-gradient(circle, rgba(26, 35, 126, 0.15) 0%, transparent 70%)',
        borderRadius: '50%',
        filter: 'blur(60px)',
        transition: 'all 0.3s'
      }} />
      <div style={{
        position: 'absolute',
        bottom: '20%',
        right: '10%',
        width: 300,
        height: 300,
        background: isDark
          ? 'radial-gradient(circle, rgba(0, 188, 212, 0.2) 0%, transparent 70%)'
          : 'radial-gradient(circle, rgba(0, 188, 212, 0.1) 0%, transparent 70%)',
        borderRadius: '50%',
        filter: 'blur(60px)',
        transition: 'all 0.3s'
      }} />

      {/* 登录卡片 */}
      <div style={{
        width: 420,
        background: 'var(--color-bg-dark)',
        border: '1px solid var(--color-border)',
        borderRadius: 16,
        padding: 40,
        position: 'relative',
        zIndex: 1,
        boxShadow: 'var(--shadow-lg)',
        transition: 'all 0.3s'
      }}>
        {/* Logo */}
        <div style={{ textAlign: 'center', marginBottom: 32 }}>
          <div style={{
            width: 64,
            height: 64,
            background: 'linear-gradient(135deg, #1A237E 0%, #283593 100%)',
            borderRadius: 16,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            margin: '0 auto 16px'
          }}>
            <Network size={36} color="#fff" />
          </div>
          <h1 style={{ 
            fontSize: 24, 
            fontWeight: 700, 
            color: 'var(--color-text-primary)',
            margin: '0 0 8px 0',
            transition: 'color 0.3s'
          }}>
            IntelliMaint Pro
          </h1>
          <p style={{ 
            fontSize: 14, 
            color: 'var(--color-text-muted)',
            margin: 0,
            transition: 'color 0.3s'
          }}>
            工业AI预测性维护系统
          </p>
        </div>

        {/* 登录表单 */}
        <Form
          name="login"
          onFinish={handleSubmit}
          size="large"
          initialValues={{ username: '', password: '' }}
        >
          <Form.Item
            name="username"
            rules={[{ required: true, message: '请输入用户名' }]}
          >
            <Input
              prefix={<User size={18} color="var(--color-text-dim)" />}
              placeholder="用户名"
              autoComplete="username"
              style={{
                background: 'var(--color-bg-elevated)',
                border: '1px solid var(--color-border)',
                borderRadius: 8,
                color: 'var(--color-text-primary)',
                height: 48
              }}
            />
          </Form.Item>

          <Form.Item
            name="password"
            rules={[{ required: true, message: '请输入密码' }]}
          >
            <Input.Password
              prefix={<Lock size={18} color="var(--color-text-dim)" />}
              placeholder="密码"
              autoComplete="current-password"
              style={{
                background: 'var(--color-bg-elevated)',
                border: '1px solid var(--color-border)',
                borderRadius: 8,
                color: 'var(--color-text-primary)',
                height: 48
              }}
            />
          </Form.Item>

          <Form.Item style={{ marginBottom: 16 }}>
            <Button
              type="primary"
              htmlType="submit"
              loading={loading}
              block
              icon={<LogIn size={18} />}
              style={{
                height: 48,
                background: 'linear-gradient(135deg, #1A237E 0%, #283593 100%)',
                border: 'none',
                borderRadius: 8,
                fontSize: 16,
                fontWeight: 500,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                gap: 8
              }}
            >
              登录系统
            </Button>
          </Form.Item>
        </Form>

        {/* 提示信息 */}
        <div style={{ 
          textAlign: 'center',
          padding: '16px 0 0',
          borderTop: '1px solid var(--color-border)'
        }}>
          <p style={{ 
            fontSize: 13, 
            color: 'var(--color-text-dim)',
            margin: 0
          }}>
            默认账户：<span style={{ color: 'var(--color-text-muted)' }}>admin</span> / <span style={{ color: 'var(--color-text-muted)' }}>admin123</span>
          </p>
        </div>
      </div>

      {/* 版本信息 */}
      <div style={{
        position: 'absolute',
        bottom: 24,
        left: '50%',
        transform: 'translateX(-50%)',
        color: 'var(--color-text-dim)',
        fontSize: 12,
        transition: 'color 0.3s'
      }}>
        IntelliMaint Pro v2.0 © 2025
      </div>
    </div>
  )
}
