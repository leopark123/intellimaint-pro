import { createContext, useContext, useState, useEffect, ReactNode, useCallback } from 'react'
import type { AuthState, LoginResponse } from '../types/auth'
import { refreshToken as refreshTokenApi, logout as logoutApi } from '../api/auth'
import { logError } from '../utils/logger'

const TOKEN_KEY = 'intellimaint_token'
const REFRESH_TOKEN_KEY = 'intellimaint_refresh_token'
const AUTH_KEY = 'intellimaint_auth'

// 从 localStorage 读取认证状态
function loadAuthState(): AuthState {
  try {
    const saved = localStorage.getItem(AUTH_KEY)
    if (saved) {
      const parsed = JSON.parse(saved) as AuthState
      // 检查 refresh token 是否过期
      if (parsed.refreshExpiresAt && Date.now() < parsed.refreshExpiresAt) {
        return { ...parsed, isAuthenticated: true }
      }
    }
  } catch (e) {
    logError('Failed to load auth state', e, 'AuthStore')
  }
  return {
    token: null,
    refreshToken: null,
    username: null,
    role: null,
    expiresAt: null,
    refreshExpiresAt: null,
    isAuthenticated: false
  }
}

// 保存认证状态到 localStorage
function saveAuthState(state: AuthState): void {
  try {
    localStorage.setItem(AUTH_KEY, JSON.stringify(state))
    if (state.token) {
      localStorage.setItem(TOKEN_KEY, state.token)
    } else {
      localStorage.removeItem(TOKEN_KEY)
    }
    if (state.refreshToken) {
      localStorage.setItem(REFRESH_TOKEN_KEY, state.refreshToken)
    } else {
      localStorage.removeItem(REFRESH_TOKEN_KEY)
    }
  } catch (e) {
    logError('Failed to save auth state', e, 'AuthStore')
  }
}

// 获取 token（供 axios 拦截器使用）
export function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY)
}

// 获取 refresh token
export function getRefreshToken(): string | null {
  return localStorage.getItem(REFRESH_TOKEN_KEY)
}

// 清除认证状态
export function clearAuth(): void {
  localStorage.removeItem(AUTH_KEY)
  localStorage.removeItem(TOKEN_KEY)
  localStorage.removeItem(REFRESH_TOKEN_KEY)
}

// 检查 access token 是否即将过期（5分钟内）
export function isTokenExpiringSoon(): boolean {
  try {
    const saved = localStorage.getItem(AUTH_KEY)
    if (!saved) return true
    const parsed = JSON.parse(saved) as AuthState
    if (!parsed.expiresAt) return true
    const fiveMinutes = 5 * 60 * 1000
    return parsed.expiresAt - Date.now() < fiveMinutes
  } catch {
    return true
  }
}

// 检查 refresh token 是否过期
export function isRefreshTokenExpired(): boolean {
  try {
    const saved = localStorage.getItem(AUTH_KEY)
    if (!saved) return true
    const parsed = JSON.parse(saved) as AuthState
    if (!parsed.refreshExpiresAt) return true
    return Date.now() > parsed.refreshExpiresAt
  } catch {
    return true
  }
}

// 刷新 token - 全局锁防止并发
let refreshPromise: Promise<AuthState | null> | null = null

export async function refreshTokenIfNeeded(): Promise<AuthState | null> {
  if (!isTokenExpiringSoon()) {
    // Token 还有效
    const saved = localStorage.getItem(AUTH_KEY)
    return saved ? JSON.parse(saved) : null
  }

  if (isRefreshTokenExpired()) {
    clearAuth()
    return null
  }

  // 防止并发刷新
  if (refreshPromise) {
    return refreshPromise
  }

  const currentRefreshToken = getRefreshToken()
  if (!currentRefreshToken) {
    return null
  }

  refreshPromise = (async () => {
    try {
      const response = await refreshTokenApi(currentRefreshToken)
      const newState: AuthState = {
        token: response.token,
        refreshToken: response.refreshToken,
        username: response.username,
        role: response.role,
        expiresAt: response.expiresAt,
        refreshExpiresAt: response.refreshExpiresAt,
        isAuthenticated: true
      }
      saveAuthState(newState)
      return newState
    } catch {
      clearAuth()
      return null
    } finally {
      refreshPromise = null
    }
  })()

  return refreshPromise
}

// Context
interface AuthContextType {
  auth: AuthState
  login: (response: LoginResponse) => void
  logout: () => void
  refreshAuth: () => Promise<boolean>
}

const AuthContext = createContext<AuthContextType | null>(null)

// Provider 组件
export function AuthProvider({ children }: { children: ReactNode }) {
  const [auth, setAuth] = useState<AuthState>(loadAuthState)

  // 定期检查 token 过期并自动刷新
  useEffect(() => {
    const checkAndRefresh = async () => {
      if (!auth.isAuthenticated) return
      
      if (isRefreshTokenExpired()) {
        handleLogout()
        return
      }
      
      if (isTokenExpiringSoon()) {
        const newState = await refreshTokenIfNeeded()
        if (newState) {
          setAuth(newState)
        } else {
          handleLogout()
        }
      }
    }
    
    const interval = setInterval(checkAndRefresh, 60000) // 每分钟检查
    return () => clearInterval(interval)
  }, [auth.isAuthenticated])

  const handleLogin = (response: LoginResponse) => {
    const newState: AuthState = {
      token: response.token,
      refreshToken: response.refreshToken,
      username: response.username,
      role: response.role,
      expiresAt: response.expiresAt,
      refreshExpiresAt: response.refreshExpiresAt,
      isAuthenticated: true
    }
    setAuth(newState)
    saveAuthState(newState)
  }

  const handleLogout = useCallback(async () => {
    const token = auth.token
    const newState: AuthState = {
      token: null,
      refreshToken: null,
      username: null,
      role: null,
      expiresAt: null,
      refreshExpiresAt: null,
      isAuthenticated: false
    }
    setAuth(newState)
    clearAuth()
    
    // 调用后端登出 API 清除 refresh token
    if (token) {
      await logoutApi(token)
    }
  }, [auth.token])

  const refreshAuth = useCallback(async (): Promise<boolean> => {
    const newState = await refreshTokenIfNeeded()
    if (newState) {
      setAuth(newState)
      return true
    }
    handleLogout()
    return false
  }, [handleLogout])

  return (
    <AuthContext.Provider value={{ auth, login: handleLogin, logout: handleLogout, refreshAuth }}>
      {children}
    </AuthContext.Provider>
  )
}

// Hook
export function useAuth() {
  const context = useContext(AuthContext)
  if (!context) {
    throw new Error('useAuth must be used within AuthProvider')
  }
  return context
}
