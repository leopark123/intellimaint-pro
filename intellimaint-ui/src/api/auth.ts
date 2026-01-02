import axios from 'axios'
import type { LoginRequest, LoginResponse } from '../types/auth'

// 使用独立的 axios 实例，避免循环依赖
const authClient = axios.create({
  baseURL: '/api',
  timeout: 30000,
  headers: {
    'Content-Type': 'application/json'
  }
})

export async function login(data: LoginRequest): Promise<LoginResponse> {
  const response = await authClient.post<{ success: boolean; data: LoginResponse }>('/auth/login', data)
  return response.data.data
}

export async function refreshToken(refreshToken: string): Promise<LoginResponse> {
  const response = await authClient.post<{ success: boolean; data: LoginResponse }>('/auth/refresh', { refreshToken })
  return response.data.data
}

export async function logout(token: string): Promise<void> {
  try {
    await authClient.post('/auth/logout', undefined, {
      headers: { Authorization: `Bearer ${token}` }
    })
  } catch {
    // 忽略错误，前端也要清理
  }
}
