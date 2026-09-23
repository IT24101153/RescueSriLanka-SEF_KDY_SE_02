import { SEVERITY_ORDER } from '../types'
import type { DashboardStatistics } from '../types'
import { SEVERITY_TOKEN } from '../severity'

type DistributionPanelProps = {
  stats: DashboardStatistics
}

type BarRow = {
  key: string
  label: string
  value: number
  token: string
}

function BarList({ rows, total }: { rows: BarRow[]; total: number }) {
  return (
    <ul className="bars">
      {rows.map((row) => {
        const share = total === 0 ? 0 : Math.round((row.value / total) * 100)
        return (
          <li key={row.key} className="bar" title={`${row.label}: ${row.value} (${share}%)`}>
            <span className="bar__label">{row.label}</span>
            <span className="bar__track">
              <span
                className={`bar__fill bar__fill--${row.token}`}
                style={{ width: `${total === 0 ? 0 : (row.value / total) * 100}%` }}
              />
            </span>
            <span className="bar__value">{row.value}</span>
          </li>
        )
      })}
    </ul>
  )
}

export default function DistributionPanel({ stats }: DistributionPanelProps) {
  const severityRows: BarRow[] = SEVERITY_ORDER.map((severity) => ({
    key: severity,
    label: severity,
    value: stats.bySeverity[severity] ?? 0,
    token: SEVERITY_TOKEN[severity],
  }))
  const severityTotal = severityRows.reduce((sum, row) => sum + row.value, 0)

  // Type and district are plain magnitude — one neutral hue, sorted, never the
  // severity palette (those colours are reserved for status).
  const typeRows: BarRow[] = Object.entries(stats.byType)
    .sort((a, b) => b[1] - a[1])
    .map(([key, value]) => ({ key, label: key, value, token: 'neutral' }))
  const typeTotal = typeRows.reduce((sum, row) => sum + row.value, 0)

  const districtRows: BarRow[] = Object.entries(stats.byDistrict)
    .sort((a, b) => b[1] - a[1])
    .slice(0, 8)
    .map(([key, value]) => ({ key, label: key, value, token: 'neutral' }))
  const districtTotal = districtRows.reduce((sum, row) => sum + row.value, 0)

  return (
    <>
      <section className="panel" aria-label="Severity distribution">
        <header className="panel__head">
          <h2 className="panel__title">Severity distribution</h2>
          <span className="panel__meta">{severityTotal} active</span>
        </header>
        <BarList rows={severityRows} total={severityTotal} />
      </section>

      <section className="panel" aria-label="Incidents by type">
        <header className="panel__head">
          <h2 className="panel__title">By disaster type</h2>
          <span className="panel__meta">{typeRows.length} types</span>
        </header>
        <BarList rows={typeRows} total={typeTotal} />
      </section>

      <section className="panel" aria-label="Incidents by district">
        <header className="panel__head">
          <h2 className="panel__title">By district</h2>
          <span className="panel__meta">top {districtRows.length}</span>
        </header>
        <BarList rows={districtRows} total={districtTotal} />
      </section>
    </>
  )
}
