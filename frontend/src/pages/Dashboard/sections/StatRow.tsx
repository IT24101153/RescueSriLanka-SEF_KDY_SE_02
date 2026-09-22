import type { DashboardStatistics } from '../../../types/incidents'

type StatRowProps = {
  stats: DashboardStatistics
}

type Tile = {
  label: string
  value: number
  hint: string
  tone?: 'critical' | 'caution' | 'default'
}

/** Headline numbers. Bare stat tiles — no plot, so no hover layer. */
export default function StatRow({ stats }: StatRowProps) {
  const tiles: Tile[] = [
    {
      label: 'Active incidents',
      value: stats.activeIncidents,
      hint: `${stats.reportedLast24Hours} reported in 24h`,
    },
    {
      label: 'Critical',
      value: stats.criticalIncidents,
      hint: 'Highest severity in force',
      tone: 'critical',
    },
    {
      label: 'Awaiting verification',
      value: stats.awaitingVerification,
      hint: 'Reported, not yet confirmed',
      tone: 'caution',
    },
    {
      label: 'People affected',
      value: stats.peopleAffected,
      hint: 'Estimated, active incidents',
    },
    {
      label: 'Danger zones',
      value: stats.activeDangerZones,
      hint: 'Derived from active incidents',
      tone: 'critical',
    },
    {
      label: 'Awaiting AI analysis',
      value: stats.awaitingAiAnalysis,
      hint: 'No severity proposal yet',
    },
  ]

  return (
    <section className="stat-row" aria-label="Key figures">
      {tiles.map((tile) => (
        <article key={tile.label} className={`stat stat--${tile.tone ?? 'default'}`}>
          <p className="stat__label">{tile.label}</p>
          <p className="stat__value">{tile.value.toLocaleString()}</p>
          <p className="stat__hint">{tile.hint}</p>
        </article>
      ))}
    </section>
  )
}
