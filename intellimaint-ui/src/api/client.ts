import axios from 'axios'
import { getToken, clearAuth, refreshTokenIfNeeded, isTokenExpiringSoon } from '../store/authStore'

const apiClient = axios.create({
  baseURL: '/api',
  timeout: 30000,
  headers: {
    'Content-Type': 'application/json'
  }
})

// 请求拦截器 - 自动添加 Token 并在需要时刷新
apiClient.interceptors.request.use(
  async (config) => {
    // 跳过登录和刷新请求
    if (config.url?.includes('/auth/login') || config.url?.includes('/auth/refresh')) {
      const token = getToken()
      if (token) {
        config.headers.Authorization = `Bearer ${token}`
      }
      return config
    }

    // 检查并刷新 Token
    if (isTokenExpiringSoon()) {
      const newState = await refreshTokenIfNeeded()
      if (!newState) {
        // Token 刷新失败，跳转登录页
        clearAuth()
        window.location.href = '/login'
        return Promise.reject(new Error('Token expired'))
      }
    }

    const token = getToken()
    if (token) {
      config.headers.Authorization = `Bearer ${token}`
    }
    return config
  },
  (error) => {
    return Promise.reject(error)
  }
)

// 响应拦截器 - 处理错误
apiClient.interceptors.response.use(
  (response) => {
    return response.data
  },
  (error) => {
    // v48: 增强错误处理
    if (error.response) {
      const status = error.response.status
      const data = error.response.data

      if (status === 401) {
        // Token 过期或无效，清除认证状态并跳转登录页
        clearAuth()
        window.location.href = '/login'
      }

      // 429 Too Many Requests - 账号锁定或限流
      if (status === 429) {
        console.warn('Rate limited or account locked:', data?.error)
      }

      // v56.1: 提取 API 返回的错误信息，覆盖 axios 默认的 "Request failed with status code XXX"
      if (data?.error) {
        error.message = data.error
      }
    }

    // 网络错误
    if (error.code === 'ECONNABORTED') {
      console.error('Request timeout')
    }

    if (error.message === 'Network Error') {
      console.error('Network error - server may be unavailable')
    }

    return Promise.reject(error)
  }
)

// v62: 添加 Blob 下载方法用于文件导出
async function getBlob(url: string): Promise<Blob> {
  const token = getToken()
  const response = await axios.get(`/api${url}`, {
    responseType: 'blob',
    headers: token ? { Authorization: `Bearer ${token}` } : {}
  })
  return response.data
}

// 扩展 apiClient 对象
const extendedApiClient = Object.assign(apiClient, {
  getBlob
})

export default extendedApiClient
export { apiClient, getBlob }
