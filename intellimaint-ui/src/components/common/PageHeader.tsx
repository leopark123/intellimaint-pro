interface PageHeaderProps {
  title: string
  description?: string
  extra?: React.ReactNode
}

export default function PageHeader({ title, description, extra }: PageHeaderProps) {
  return (
    <div style={{
      display: 'flex',
      alignItems: 'flex-start',
      justifyContent: 'space-between',
      marginBottom: 24
    }}>
      <div>
        <h1 style={{
          fontSize: 24,
          fontWeight: 700,
          color: 'var(--color-text-primary)',
          margin: '0 0 8px 0',
          lineHeight: 1.3
        }}>
          {title}
        </h1>
        {description && (
          <p style={{
            fontSize: 14,
            color: 'var(--color-text-muted)',
            margin: 0,
            lineHeight: 1.5
          }}>
            {description}
          </p>
        )}
      </div>
      {extra && (
        <div style={{ flexShrink: 0 }}>
          {extra}
        </div>
      )}
    </div>
  )
}
