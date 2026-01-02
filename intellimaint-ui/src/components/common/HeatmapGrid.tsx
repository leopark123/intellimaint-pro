interface HeatmapCell {
  value: number  // 0-3: 正常、警告、异常、危急
  label?: string
}

interface HeatmapGridProps {
  data: HeatmapCell[][]
  title?: string
  showLegend?: boolean
}

const cellColors = [
  '#10b981', // 0: 正常 - 绿色
  '#f59e0b', // 1: 警告 - 黄色
  '#f97316', // 2: 异常 - 橙色
  '#ef4444'  // 3: 危急 - 红色
]

const legendItems = [
  { color: '#10b981', label: '正常' },
  { color: '#f59e0b', label: '警告' },
  { color: '#f97316', label: '异常' },
  { color: '#ef4444', label: '危急' }
]

export default function HeatmapGrid({ 
  data, 
  title = '异常热力图',
  showLegend = true 
}: HeatmapGridProps) {
  return (
    <div style={{
      background: 'var(--color-bg-dark)',
      border: '1px solid var(--color-border)',
      borderRadius: 12,
      padding: 24
    }}>
      {/* 标题和图例 */}
      <div style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        marginBottom: 16
      }}>
        <h3 style={{
          fontSize: 16,
          fontWeight: 600,
          color: 'var(--color-text-primary)',
          margin: 0
        }}>
          {title}
        </h3>
        
        {showLegend && (
          <div style={{ display: 'flex', gap: 16 }}>
            {legendItems.map((item, idx) => (
              <div key={idx} style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                <span style={{
                  width: 12,
                  height: 12,
                  borderRadius: 2,
                  background: item.color
                }} />
                <span style={{ fontSize: 12, color: 'var(--color-text-muted)' }}>{item.label}</span>
              </div>
            ))}
          </div>
        )}
      </div>

      {/* 热力图网格 */}
      <div style={{
        display: 'flex',
        flexDirection: 'column',
        gap: 4
      }}>
        {data.map((row, rowIdx) => (
          <div 
            key={rowIdx} 
            style={{ 
              display: 'flex', 
              gap: 4 
            }}
          >
            {row.map((cell, colIdx) => (
              <div
                key={colIdx}
                style={{
                  width: 36,
                  height: 36,
                  borderRadius: 4,
                  background: cellColors[Math.min(cell.value, 3)],
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  cursor: 'pointer',
                  transition: 'transform 0.2s, opacity 0.2s'
                }}
                title={cell.label || `值: ${cell.value}`}
                onMouseEnter={(e) => {
                  e.currentTarget.style.transform = 'scale(1.1)'
                  e.currentTarget.style.opacity = '0.8'
                }}
                onMouseLeave={(e) => {
                  e.currentTarget.style.transform = 'scale(1)'
                  e.currentTarget.style.opacity = '1'
                }}
              />
            ))}
          </div>
        ))}
      </div>
    </div>
  )
}

// 生成模拟热力图数据
export function generateMockHeatmapData(rows: number, cols: number): HeatmapCell[][] {
  const data: HeatmapCell[][] = []
  for (let i = 0; i < rows; i++) {
    const row: HeatmapCell[] = []
    for (let j = 0; j < cols; j++) {
      // 大部分是正常，少量异常
      const rand = Math.random()
      let value = 0
      if (rand > 0.95) value = 3      // 5% 危急
      else if (rand > 0.9) value = 2  // 5% 异常
      else if (rand > 0.8) value = 1  // 10% 警告
      
      row.push({ value, label: `设备 ${i + 1}-${j + 1}` })
    }
    data.push(row)
  }
  return data
}
