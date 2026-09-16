import type { SafetyZone } from '../../../types/incidents'
import { ZONE_TOKEN } from '../severity'

type ZonePanelProps = {
  zones: SafetyZone[]
}

export default function ZonePanel({ zones }: ZonePanelProps) {
  const ordered = [...zones].sort((a, b) => {
    const rank = { Danger: 0, Caution: 1, Safe: 2 }
    return rank[a.status] - rank[b.status]
  })

  return (
    <section className="panel" aria-label="Safety zones">
      <header className="panel__head">
        <h2 className="panel__title">Safety zones</h2>
        <span className="panel__meta">{zones.length} active</span>
      </header>

      {ordered.length === 0 ? (
        <p className="empty">No active zones.</p>
      ) : (
        <ul className="zones">
          {ordered.map((zone) => (
            <li key={zone.id} className="zone">
              <span className={`chip chip--${ZONE_TOKEN[zone.status]}`}>
                {zone.status}
              </span>
              <span className="zone__body">
                <span className="zone__name">{zone.name}</span>
                <span className="zone__meta">
                  {(zone.radiusMeters / 1000).toFixed(1)} km radius ·{' '}
                  {zone.source === 'ManualOverride' ? 'manual' : 'derived'}
                </span>
              </span>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
