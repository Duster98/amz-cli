import {
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react'
import type {
  MonitorSeriesAlert,
  MonitorSeriesPoint,
  MonitorThreshold,
} from '../lib/api'
import { useI18n } from '../i18n/I18nProvider'
import { Icon } from './ui'

export type ChartRange = '24h' | '7d' | '30d' | '90d' | 'all'

const chartHeight = 320
const padding = { top: 24, right: 32, bottom: 38, left: 76 }

export function PriceChart({
  points,
  alerts,
  thresholds,
  currency,
  range,
  onRangeChange,
  loading,
  error,
  emptyLabel,
  observations,
  children,
}: {
  points: MonitorSeriesPoint[]
  alerts: MonitorSeriesAlert[]
  thresholds: MonitorThreshold[]
  currency?: string | null
  range: ChartRange
  onRangeChange: (range: ChartRange) => void
  loading?: boolean
  error?: string | null
  emptyLabel: string
  observations?: number
  children?: ReactNode
}) {
  const { t, languageLocale, formatNumber } = useI18n()
  const containerRef = useRef<HTMLDivElement>(null)
  const [hoverIndex, setHoverIndex] = useState<number | null>(null)
  const [chartWidth, setChartWidth] = useState(960)

  useEffect(() => {
    const container = containerRef.current
    if (!container) {
      return
    }
    const update = () => {
      const next = Math.max(320, Math.round(container.clientWidth))
      setChartWidth((current) => (current === next ? current : next))
    }
    update()
    const observer = new ResizeObserver(update)
    observer.observe(container)
    return () => observer.disconnect()
  }, [error, loading, points.length])
  const number = useMemo(
    () =>
      new Intl.NumberFormat(languageLocale, {
        style: currency ? 'currency' : 'decimal',
        currency: currency || undefined,
        maximumFractionDigits: 2,
      }),
    [currency, languageLocale],
  )
  const geometry = useMemo(
    () => buildGeometry(points, number, chartWidth),
    [chartWidth, number, points],
  )
  const date = useMemo(
    () =>
      new Intl.DateTimeFormat(languageLocale, {
        day: '2-digit',
        month: 'short',
        hour: '2-digit',
        minute: '2-digit',
      }),
    [languageLocale],
  )
  const axisDate = useMemo(() => {
    const times = points
      .map((point) => Date.parse(point.observed_at))
      .filter(Number.isFinite)
    const span = times.length > 1
      ? Math.max(...times) - Math.min(...times)
      : 0
    if (span <= 36 * 60 * 60 * 1000) {
      return new Intl.DateTimeFormat(languageLocale, {
        hour: '2-digit',
        minute: '2-digit',
      })
    }
    if (span <= 14 * 24 * 60 * 60 * 1000) {
      return new Intl.DateTimeFormat(languageLocale, {
        day: '2-digit',
        month: 'short',
      })
    }
    return new Intl.DateTimeFormat(languageLocale, {
      month: 'short',
      year: '2-digit',
    })
  }, [languageLocale, points])

  if (loading) {
    return (
      <div className="price-chart-shell">
        <div className="price-chart-loading">
          <span className="skeleton price-chart-skeleton" />
        </div>
        {children}
      </div>
    )
  }

  if (error) {
    return (
      <div className="price-chart-shell">
        <div className="price-chart-placeholder">
          <div className="price-chart-empty">
            <Icon name="alert" />
            <p>{error}</p>
          </div>
        </div>
        {children}
      </div>
    )
  }

  if (!geometry) {
    return (
      <div className="price-chart-shell">
        <div className="price-chart-placeholder">
          <div className="price-chart-empty">
            <Icon name="activity" />
            <p>{emptyLabel}</p>
          </div>
        </div>
        {children}
      </div>
    )
  }

  const hovered = hoverIndex !== null
    ? geometry.points[hoverIndex] ?? null
    : null
  const active = hovered ?? geometry.points[geometry.points.length - 1]

  const handleMove = (event: React.PointerEvent<SVGSVGElement>) => {
    const container = containerRef.current
    if (!container || geometry.points.length === 0) {
      return
    }
    const rect = container.getBoundingClientRect()
    const x = ((event.clientX - rect.left) / rect.width) * chartWidth
    let nearest = 0
    let distance = Number.POSITIVE_INFINITY
    geometry.points.forEach((point, index) => {
      const current = Math.abs(point.x - x)
      if (current < distance) {
        distance = current
        nearest = index
      }
    })
    setHoverIndex(nearest)
  }

  return (
    <div className="price-chart-shell">
      <div className="price-chart-topline">
        <div className="price-chart-readout">
          <span className="price-chart-readout-label">
            {hovered ? t('monitoring.chartHover') : t('monitoring.chartLatest')}
          </span>
          <span className="price-chart-readout-main">
            <strong className="price-chart-readout-price">
              {number.format(active.point.price)}
            </strong>
            <span className="price-chart-readout-date">
              {date.format(new Date(active.point.observed_at))}
            </span>
          </span>
          {hovered?.point.min != null &&
          hovered.point.max != null &&
          hovered.point.min !== hovered.point.max ? (
            <span className="price-chart-readout-range">
              {number.format(hovered.point.min)} –{' '}
              {number.format(hovered.point.max)}
            </span>
          ) : null}
          {hovered && !hovered.point.complete ? (
            <span className="price-chart-readout-note">
              {t('monitoring.incomplete')}
            </span>
          ) : null}
        </div>
        <div className="price-chart-controls">
          <ChartRangeSelector range={range} onChange={onRangeChange} />
          {geometry.points.length > 0 ? (
            <span className="price-chart-count">
              {t('monitoring.observations', {
                count: formatNumber(observations ?? points.length),
              })}
            </span>
          ) : null}
        </div>
      </div>
      <div className="price-chart-canvas" ref={containerRef}>
        <svg
          className="price-chart-svg"
          viewBox={`0 0 ${chartWidth} ${chartHeight}`}
          role="img"
          aria-label={t('monitoring.chartAria')}
          onPointerMove={handleMove}
          onPointerDown={handleMove}
          onPointerLeave={() => setHoverIndex(null)}
        >
          <defs>
            <linearGradient
              id="price-chart-area-gradient"
              x1="0"
              y1="0"
              x2="0"
              y2="1"
            >
              <stop offset="0" className="price-chart-area-stop" />
              <stop
                offset="1"
                className="price-chart-area-stop price-chart-area-stop--end"
              />
            </linearGradient>
          </defs>
          <rect
            x={0}
            y={0}
            width={chartWidth}
            height={chartHeight}
            fill="transparent"
          />
          {geometry.ticks.map((tick) => (
            <g key={`y-${tick.value}`}>
              <line
                x1={padding.left}
                y1={tick.y}
                x2={chartWidth - padding.right}
                y2={tick.y}
                className="price-chart-grid"
              />
              <text
                x={padding.left - 10}
                y={tick.y + 4}
                textAnchor="end"
                className="price-chart-axis-label"
              >
                {tick.label}
              </text>
            </g>
          ))}
          {geometry.xTicks.map((tick) => (
            <g key={`x-${tick.timestamp}`}>
              <line
                x1={tick.x}
                y1={chartHeight - padding.bottom}
                x2={tick.x}
                y2={chartHeight - padding.bottom + 5}
                className="price-chart-axis-line"
              />
              <text
                x={tick.x}
                y={chartHeight - 10}
                textAnchor="middle"
                className="price-chart-axis-label"
              >
                {axisDate.format(tick.timestamp)}
              </text>
            </g>
          ))}
          {thresholds.map((threshold) => {
            const y = geometry.y(threshold.value)
            if (y < padding.top || y > chartHeight - padding.bottom) {
              return null
            }
            return (
              <g key={`${threshold.label}-${threshold.value}`}>
                <line
                  x1={padding.left}
                  y1={y}
                  x2={chartWidth - padding.right}
                  y2={y}
                  className={`price-chart-threshold price-chart-threshold--${threshold.label}`}
                />
                <text
                  x={chartWidth - padding.right}
                  y={Math.max(padding.top + 11, y - 5)}
                  textAnchor="end"
                  className="price-chart-threshold-label"
                >
                  {threshold.label === 'total_max'
                    ? t('monitoring.totalMax')
                    : threshold.label === 'drop_percent'
                      ? t('monitoring.dropPercent')
                      : threshold.label === 'rise_percent'
                        ? t('monitoring.risePercent')
                        : t('monitoring.maxPrice')}{' '}
                  {number.format(threshold.value)}
                </text>
              </g>
            )
          })}
          {geometry.areas.map((area, index) => (
            <path
              key={`area-${index}`}
              d={area}
              className="price-chart-area"
              fill="url(#price-chart-area-gradient)"
            />
          ))}
          {geometry.segments.map((segment, index) => (
            <path
              key={`segment-${index}`}
              d={segment}
              className="price-chart-line"
            />
          ))}
          {geometry.points.length <= 24
            ? geometry.points.map((entry, index) => (
                <circle
                  key={`dot-${index}`}
                  cx={entry.x}
                  cy={entry.y}
                  r={2.6}
                  className="price-chart-dot"
                />
              ))
            : geometry.isolated.map((entry, index) => (
                <circle
                  key={`isolated-${index}`}
                  cx={entry.x}
                  cy={entry.y}
                  r={2.6}
                  className="price-chart-dot"
                />
              ))}
          {alerts.map((alert, index) => {
            const x = geometry.x(Date.parse(alert.at))
            const value = alert.observed
            if (x === null || value == null) {
              return null
            }
            const y = geometry.y(value)
            if (y < padding.top || y > chartHeight - padding.bottom) {
              return null
            }
            return (
              <circle
                key={`alert-${index}`}
                cx={x}
                cy={y}
                r={3.5}
                className="price-chart-alert"
              >
                <title>{alert.message}</title>
              </circle>
            )
          })}
          {hovered ? (
            <g>
              <line
                x1={hovered.x}
                y1={padding.top}
                x2={hovered.x}
                y2={chartHeight - padding.bottom}
                className="price-chart-crosshair"
              />
              <circle
                cx={hovered.x}
                cy={hovered.y}
                r={4.5}
                className="price-chart-point"
              />
            </g>
          ) : null}
        </svg>
      </div>
      {children}
      <details className="price-chart-data">
        <summary>{t('monitoring.chartData')}</summary>
        <div className="price-chart-table-wrap">
          <table>
            <thead>
              <tr>
                <th>{t('monitoring.observedAt')}</th>
                <th>{t('monitoring.price')}</th>
                <th>{t('monitoring.status')}</th>
              </tr>
            </thead>
            <tbody>
              {points.slice(-30).map((point) => (
                <tr key={`${point.observed_at}-${point.count}`}>
                  <td>{date.format(new Date(point.observed_at))}</td>
                  <td>{number.format(point.price)}</td>
                  <td>
                    {point.complete
                      ? t('monitoring.complete')
                      : t('monitoring.incomplete')}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </details>
    </div>
  )
}

function ChartRangeSelector({
  range,
  onChange,
}: {
  range: ChartRange
  onChange: (range: ChartRange) => void
}) {
  const { t } = useI18n()
  const options: ChartRange[] = ['24h', '7d', '30d', '90d', 'all']
  return (
    <div className="price-chart-ranges" role="group" aria-label={t('monitoring.range')}>
      {options.map((option) => (
        <button
          key={option}
          type="button"
          className={range === option ? 'is-active' : ''}
          aria-pressed={range === option}
          onClick={() => onChange(option)}
        >
          {option === 'all' ? t('monitoring.rangeAll') : option}
        </button>
      ))}
    </div>
  )
}

function buildGeometry(
  points: MonitorSeriesPoint[],
  number: Intl.NumberFormat,
  chartWidth: number,
) {
  if (points.length === 0) {
    return null
  }
  const usableWidth = chartWidth - padding.left - padding.right
  const usableHeight = chartHeight - padding.top - padding.bottom
  const times = points.map((point) => Date.parse(point.observed_at))
  const validTimes = times.filter(Number.isFinite)
  if (validTimes.length === 0) {
    return null
  }
  const minTime = Math.min(...validTimes)
  const maxTime = Math.max(...validTimes)
  const values = points.flatMap((point) => [point.price, point.min, point.max])
    .filter((value): value is number => typeof value === 'number')
  if (values.length === 0) {
    return null
  }
  const rawMin = Math.min(...values)
  const rawMax = Math.max(...values)
  const span = rawMax - rawMin || Math.max(1, rawMax * 0.1)
  const minValue = rawMin - span * 0.08
  const maxValue = rawMax + span * 0.08
  const x = (timestamp: number) =>
    maxTime === minTime
      ? padding.left + usableWidth / 2
      : padding.left + ((timestamp - minTime) / (maxTime - minTime)) * usableWidth
  const y = (value: number) =>
    padding.top + ((maxValue - value) / (maxValue - minValue)) * usableHeight
  const plotted = points.map((point, index) => ({
    point,
    index,
    x: x(validTimes[index] ?? minTime),
    y: y(point.price),
  }))
  const gaps: number[] = []
  for (let index = 1; index < validTimes.length; index += 1) {
    gaps.push(validTimes[index] - validTimes[index - 1])
  }
  gaps.sort((left, right) => left - right)
  const medianGap = gaps.length > 0 ? gaps[Math.floor(gaps.length / 2)] : 0
  const maxGap = Math.max(6 * 60 * 60 * 1000, medianGap * 6)
  const segments: string[] = []
  const areas: string[] = []
  const isolated: { x: number; y: number }[] = []
  const bottom = chartHeight - padding.bottom
  let current: { x: number; y: number }[] = []
  const flush = () => {
    if (current.length === 0) {
      return
    }
    if (current.length === 1) {
      isolated.push(current[0])
      current = []
      return
    }
    const line = current
      .map((entry) => `${entry.x.toFixed(2)} ${entry.y.toFixed(2)}`)
      .join(' L ')
    segments.push(`M ${line}`)
    const first = current[0]
    const last = current[current.length - 1]
    areas.push(
      `M ${first.x.toFixed(2)} ${bottom} L ${line} L ${last.x.toFixed(2)} ${bottom} Z`,
    )
    current = []
  }
  plotted.forEach((entry, index) => {
    if (
      index > 0
      && validTimes[index] - validTimes[index - 1] > maxGap
    ) {
      flush()
    }
    current.push({ x: entry.x, y: entry.y })
  })
  flush()
  const ticks = Array.from({ length: 5 }, (_, index) => {
    const value = maxValue - ((maxValue - minValue) / 4) * index
    return {
      value,
      y: y(value),
      label: number.format(value),
    }
  })
  const tickCount = 4
  const xTicks = Array.from({ length: tickCount }, (_, index) => {
    const timestamp = minTime + ((maxTime - minTime) / (tickCount - 1)) * index
    return {
      timestamp,
      x: maxTime === minTime
        ? padding.left + usableWidth / 2
        : x(timestamp),
    }
  })
  return {
    points: plotted,
    segments,
    areas,
    isolated,
    ticks,
    xTicks,
    y,
    x: (timestamp: number) =>
      Number.isFinite(timestamp) &&
      timestamp >= minTime &&
      timestamp <= maxTime
        ? x(timestamp)
        : null,
  }
}
