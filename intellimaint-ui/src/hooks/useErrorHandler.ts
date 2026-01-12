import { message } from 'antd'
import { useCallback } from 'react'

/**
 * v48: API 错误响应格式
 */
export interface ApiError {
  success: false
  error: string
  errorCode?: string
  timestamp: number
  details?: string
}

/**
 * v48: 错误代码到用户友好消息的映射
 */
const ERROR_MESSAGES: Record<string, string> = {
  'INVALID_ARGUMENT': '请求参数错误',
  'NOT_FOUND': '资源不存在',
  'FORBIDDEN': '无权访问',
  'INVALID_OPERATION': '操作无效',
  'INTERNAL_ERROR': '服务器内部错误，请稍后重试',
  'NETWORK_ERROR': '网络错误，请检查连接',
  'TIMEOUT': '请求超时，请稍后重试',
}

/**
 * v48: 解析 API 错误
 */
export function parseApiError(error: any): { message: string; code?: string } {
  // Axios 错误
  if (error.response) {
    const data = error.response.data as ApiError | undefined
    if (data?.error) {
      return {
        message: data.error,
        code: data.errorCode
      }
    }
    // HTTP 状态码错误
    const status = error.response.status
    if (status === 401) {
      return { message: '登录已过期，请重新登录', code: 'UNAUTHORIZED' }
    }
    if (status === 403) {
      return { message: '无权执行此操作', code: 'FORBIDDEN' }
    }
    if (status === 404) {
      return { message: '请求的资源不存在', code: 'NOT_FOUND' }
    }
    if (status === 429) {
      return { message: '请求过于频繁，请稍后重试', code: 'RATE_LIMITED' }
    }
    if (status >= 500) {
      return { message: '服务器错误，请稍后重试', code: 'SERVER_ERROR' }
    }
  }
  
  // 网络错误
  if (error.code === 'ECONNABORTED' || error.message?.includes('timeout')) {
    return { message: ERROR_MESSAGES['TIMEOUT'], code: 'TIMEOUT' }
  }
  
  if (error.message === 'Network Error') {
    return { message: ERROR_MESSAGES['NETWORK_ERROR'], code: 'NETWORK_ERROR' }
  }
  
  // 未知错误
  return {
    message: error.message || '未知错误',
    code: 'UNKNOWN'
  }
}

/**
 * v48: 统一错误处理 Hook
 */
export function useErrorHandler() {
  const handleError = useCallback((error: any, customMessage?: string) => {
    const { message: errorMessage, code } = parseApiError(error)
    
    // 显示错误提示
    message.error(customMessage || errorMessage)
    
    // 开发环境打印详细错误
    if (import.meta.env.DEV) {
      console.error('API Error:', { error, code, message: errorMessage })
    }
    
    return { message: errorMessage, code }
  }, [])
  
  const handleSuccess = useCallback((msg: string) => {
    message.success(msg)
  }, [])
  
  const handleWarning = useCallback((msg: string) => {
    message.warning(msg)
  }, [])
  
  return { handleError, handleSuccess, handleWarning }
}

/**
 * v48: 包装异步操作并处理错误
 */
export async function withErrorHandling<T>(
  operation: () => Promise<T>,
  onError?: (error: any) => void
): Promise<T | null> {
  try {
    return await operation()
  } catch (error) {
    const { message: errorMessage } = parseApiError(error)
    message.error(errorMessage)
    onError?.(error)
    return null
  }
}
