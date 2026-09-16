import type { Incident, SafetyZone, ZoneStatus } from '../../../types/incidents'
import IncidentMap from './IncidentMap'
import { ZONE_TOKEN } from '../severity'

type ZonesSectionProps = {
  zones: SafetyZone[]
  incidents: Incident[]
  onSelect: (incident: Incident) => void
}

const ZONE_ORDER: ZoneStatus[] = ['Danger', 'Caution', 'Safe']

export default function ZonesSection({
  zones,
  incidents,
  onSelect,
}: ZonesSectionProps) {
  const counts = ZONE_ORDER.map((status) => ({
    status,
    count: zones.filter((zone) => zone.status === status).length,
  }))

  const derived = zones.filter((zone) => zone.source === 'DerivedFromIncident').length
  const manual = zones.length - derived
  const coverageKm2 = zones.reduce(
    (sum, zone) => sum + Math.PI * (zone.radiusMeters / 1000) ** 2,
    0,
  )

  const ordered = [...zones].sort(
    (a, b) => ZONE_ORDER.indexOf(a.status) - ZONE_ORDER.indexOf(b.status),
  )

  return (
    <div className="section">
      <section className="stat-row" aria-label="Zone figures">
        {counts.map((entry) => (
          <article
            key={entry.status}
            className={`stat stat--${entry.status === 'Danger' ? 'critical' : entry.status === 'Caution' ? 'caution' : 'default'}`}
          >
            <p className="stat__label">{entry.status} zones</p>
            <p className="stat__value">{entry.count}</p>
            <p className="stat__hint">
              {entry.status === 'Danger'
                ? 'Evacuation guidance applies'
                : entry.status === 'Caution'
                  ? 'Active incident nearby'
                  : 'No hazard recorded'}
            </p>
          </article>
        ))}
        <article className="stat">
          <p className="stat__label">Auto-derived</p>
          <p className="stat__value">{derived}</p>
          <p className="stat__hint">{manual} manual override(s)</p>
        </article>
        <article className="stat">
          <p className="stat__label">Area covered</p>
          <p className="stat__value">{Math.round(coverageKm2)}</p>
          <p className="stat__hint">km² inside a zone</p>
        </article>
      </section>

      <section className="panel" aria-label="Safety zone map">
        <header className="panel__head">
          <h2 className="panel__title">Zone coverage</h2>
          <span className="panel__meta">incidents hidden — zones only</span>
        </header>
        <IncidentMap
          incidents={incidents}
          zones={zones}
          showZones
          showIncidents={false}
          height={400}
          onSelect={onSelect}
        />
      </section>

      <section className="panel" aria-label="Safety zone list">
        <header className="panel__head">
          <h2 className="panel__title">All safety zones</h2>
          <span className="panel__meta">{zones.length} active</span>
        </header>

        {ordered.length === 0 ? (
          <p className="empty">No active zones.</p>
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Status</th>
                  <th>Zone</th>
                  <th>District</th>
                  <th className="num">Radius</th>
                  <th>Source</th>
                  <th className="col-wide">Rationale</th>
                </tr>
              </thead>
              <tbody>
                {ordered.map((zone) => (
                  <tr key={zone.id}>
                    <td>
                      <span className={`chip chip--${ZONE_TOKEN[zone.status]}`}>
                        {zone.status}
                      </span>
                    </td>
                    <td className="cell-title">{zone.name}</td>
                    <td>{zone.district ?? '—'}</td>
                    <td className="num">
                      {(zone.radiusMeters / 1000).toFixed(1)} km
                    </td>
                    <td>
                      <span className="state">
                        {zone.source === 'ManualOverride' ? 'Manual' : 'Derived'}
                      </span>
                    </td>
                    <td className="col-wide">
                      <span className="state">{zone.rationale ?? '—'}</span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  )
}
