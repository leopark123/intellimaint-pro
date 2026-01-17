/**
 * v56: 统一日志服务
 * 生产环境自动禁用 console 输出，可扩展为远程日志收集
 */

type LogLevel = 'debug' | 'info' | 'warn' | 'error'

interface LogEntry {
  level: LogLevel
  message: string
  data?: unknown
  timestamp: Date
  source?: string
}

class Logger {
  private isDev = import.meta.env.DEV
  private logBuffer: LogEntry[] = []
  private maxBufferSize = 100

  private log(level: LogLevel, message: string, data?: unknown, source?: string) {
    const entry: LogEntry = {
      level,
      message,
      data,
      timestamp: new Date(),
      source
    }

    // 开发环境输出到控制台
    if (this.isDev) {
      const prefix = source ? `[${source}]` : ''
      const consoleMethod = level === 'error' ? console.error
        : level === 'warn' ? console.warn
        : level === 'info' ? console.info
        : console.debug

      if (data !== undefined) {
        consoleMethod(`${prefix} ${message}`, data)
      } else {
        consoleMethod(`${prefix} ${message}`)
      }
    }

    // 保存到缓冲区（用于调试或远程上报）
    this.logBuffer.push(entry)
    if (this.logBuffer.length > this.maxBufferSize) {
      this.logBuffer.shift()
    }

    // 生产环境的错误可以上报到远程服务
    if (!this.isDev && level === 'error') {
      this.reportError(entry)
    }
  }

  debug(message: string, data?: unknown, source?: string) {
    this.log('debug', message, data, source)
  }

  info(message: string, data?: unknown, source?: string) {
    this.log('info', message, data, source)
  }

  warn(message: string, data?: unknown, source?: string) {
    this.log('warn', message, data, source)
  }

  error(message: string, data?: unknown, source?: string) {
    this.log('error', message, data, source)
  }

  // 获取最近的日志（用于调试）
  getRecentLogs(count = 20): LogEntry[] {
    return this.logBuffer.slice(-count)
  }

  // 清空日志缓冲
  clearLogs() {
    this.logBuffer = []
  }

  // 远程错误上报（可扩展）
  private reportError(entry: LogEntry) {
    // TODO: 实现远程错误上报
    // 例如: fetch('/api/logs/error', { method: 'POST', body: JSON.stringify(entry) })
  }
}

// 单例导出
export const logger = new Logger()

// 便捷方法导出
export const logDebug = (msg: string, data?: unknown, src?: string) => logger.debug(msg, data, src)
export const logInfo = (msg: string, data?: unknown, src?: string) => logger.info(msg, data, src)
export const logWarn = (msg: string, data?: unknown, src?: string) => logger.warn(msg, data, src)
export const logError = (msg: string, data?: unknown, src?: string) => logger.error(msg, data, src)
