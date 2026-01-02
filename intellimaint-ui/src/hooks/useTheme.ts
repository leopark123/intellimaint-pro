import { useState, useEffect, useCallback } from 'react'

export type Theme = 'dark' | 'light'

const THEME_KEY = 'intellimaint_theme'

// 获取初始主题
function getInitialTheme(): Theme {
  // 1. 优先使用 localStorage 存储的值
  const stored = localStorage.getItem(THEME_KEY)
  if (stored === 'dark' || stored === 'light') {
    return stored
  }
  
  // 2. 其次跟随系统偏好
  if (window.matchMedia?.('(prefers-color-scheme: light)').matches) {
    return 'light'
  }
  
  // 3. 默认深色
  return 'dark'
}

// 应用主题到 DOM
function applyTheme(theme: Theme) {
  document.documentElement.setAttribute('data-theme', theme)
  
  // 更新 meta theme-color（影响移动端浏览器地址栏颜色）
  const metaThemeColor = document.querySelector('meta[name="theme-color"]')
  if (metaThemeColor) {
    metaThemeColor.setAttribute('content', theme === 'dark' ? '#0a0a0f' : '#f8fafc')
  }
}

export function useTheme() {
  const [theme, setTheme] = useState<Theme>(getInitialTheme)
  
  // 初始化时应用主题
  useEffect(() => {
    applyTheme(theme)
  }, [])
  
  // 切换主题
  const toggleTheme = useCallback(() => {
    const next: Theme = theme === 'dark' ? 'light' : 'dark'
    setTheme(next)
    applyTheme(next)
    localStorage.setItem(THEME_KEY, next)
  }, [theme])
  
  // 设置指定主题
  const setThemeValue = useCallback((newTheme: Theme) => {
    setTheme(newTheme)
    applyTheme(newTheme)
    localStorage.setItem(THEME_KEY, newTheme)
  }, [])
  
  // 监听系统主题变化
  useEffect(() => {
    const mediaQuery = window.matchMedia('(prefers-color-scheme: light)')
    
    const handleChange = (e: MediaQueryListEvent) => {
      // 只有当用户没有手动设置过主题时，才跟随系统
      const stored = localStorage.getItem(THEME_KEY)
      if (!stored) {
        const newTheme: Theme = e.matches ? 'light' : 'dark'
        setTheme(newTheme)
        applyTheme(newTheme)
      }
    }
    
    mediaQuery.addEventListener('change', handleChange)
    return () => mediaQuery.removeEventListener('change', handleChange)
  }, [])
  
  return {
    theme,
    isDark: theme === 'dark',
    isLight: theme === 'light',
    toggleTheme,
    setTheme: setThemeValue
  }
}

export default useTheme
