import type { DashboardStatistics } from '../types'

type CoveragePanelProps = {
  stats: DashboardStatistics
}

/**
 * How much of the active caseload the Incident Analysis Agent has processed,
 * and how much of it a human has confirmed. Two meters, both labelled.
 */
export default function CoveragePanel({ stats }: CoveragePanelProps) {
  const total = stats.activeIncidents
  const analysed = Math.max(total - stats.awaitingAiAnalysis, 0)
  const verified = Math.max(total - stats.awaitingVerification, 0)

  const rows = [
    {
      label: 'AI analysed',
      done: analysed,
      hint: `${stats.awaitingAiAnalysis} awaiting the agent`,
    },
    {
      label: 'Human verified',
      done: verified,
      hint: `${stats.awaitingVerification} awaiting a coordinator`,
    },
  ]

  return (
    <section className="panel" aria-label="Processing coverage">
      <header className="panel__head">
        <h2 className="panel__title">Processing coverage</h2>
        <span className="panel__meta">{total} active</span>
      </header>

      <div className="meters">
        {rows.map((row) => {
          const percent = total === 0 ? 0 : Math.round((row.done / total) * 100)
          return (
            <div key={row.label} className="meter">
              <div className="meter__head">
                <span className="meter__label">{row.label}</span>
                <span className="meter__value">
                  {row.done}/{total} · {percent}%
                </span>
              </div>
              <div className="meter__track">
                <div className="meter__fill" style={{ width: `${percent}%` }} />
              </div>
              <p className="meter__hint">{row.hint}</p>
            </div>
          )
        })}
      </div>
    </section>
  )
}
