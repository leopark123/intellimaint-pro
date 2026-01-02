import type { ReactNode } from 'react'

interface ChartCardProps {
  title: string
  subtitle?: string
  extra?: ReactNode
  children: ReactNode
  height?: number | string
}

export default function ChartCard({ 
  title, 
  subtitle,
  extra, 
  children, 
  height = 300 
}: ChartCardProps) {
  return (
    <div style={{
      background: 'var(--color-bg-dark)',
      border: '1px solid var(--color-border)',
      borderRadius: 12,
      padding: 24,
      height: '100%'
    }}>
      {/* 标题区 */}
      <div style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        marginBottom: 20
      }}>
        <div>
          <h3 style={{
            fontSize: 16,
            fontWeight: 600,
            color: 'var(--color-text-primary)',
            margin: 0
          }}>
            {title}
          </h3>
          {subtitle && (
            <p style={{
              fontSize: 13,
              color: 'var(--color-text-dim)',
              margin: '4px 0 0 0'
            }}>
              {subtitle}
            </p>
          )}
        </div>
        {extra && (
          <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
            {extra}
          </div>
        )}
      </div>

      {/* 内容区 */}
      <div style={{ height: typeof height === 'number' ? height : height }}>
        {children}
      </div>
    </div>
  )
}
