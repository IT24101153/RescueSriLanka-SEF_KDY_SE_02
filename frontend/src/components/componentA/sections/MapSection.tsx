import type { Incident, SafetyZone } from '../types'
import IncidentMap from './IncidentMap'
import { SEVERITY_TOKEN, timeAgo } from '../severity'

type MapSectionProps = {
  incidents: Incident[]
  zones: SafetyZone[]
  showZones: boolean
  onToggleZones: (value: boolean) => void
  selectedId: string | null
  onSelect: (incident: Incident) => void
}

/** The map plus a side list, so the map is navigable without hunting for pins. */
export default function MapSection({
  incidents,
  zones,
  showZones,
  onToggleZones,
  selectedId,
  onSelect,
}: MapSectionProps) {
  const ranked = [...incidents].sort((a, b) => {
    const order = { Critical: 0, High: 1, Moderate: 2, Low: 3 }
    return order[a.severity] - order[b.severity]
  })

  return (
    <div className="section">
      <section className="panel panel--map" aria-label="Live disaster map">
        <header className="panel__head">
          <h2 className="panel__title">Live disaster map</h2>
          <div className="panel__tools">
            <label className="switch">
              <input
                type="checkbox"
                checked={showZones}
                onChange={(event) => onToggleZones(event.target.checked)}
              />
              <span>Safety zones</span>
            </label>
            <span className="panel__meta">
              {incidents.length} incidents · {showZones ? zones.length : 0} zones
            </span>
          </div>
        </header>

        <div className="map-split">
          <IncidentMap
            incidents={incidents}
            zones={zones}
            showZones={showZones}
            selectedId={selectedId}
            height={520}
            onSelect={onSelect}
          />

          <aside className="map-list" aria-label="Incidents on the map">
            <p className="map-list__head">By severity</p>
            <ul>
              {ranked.map((incident) => (
                <li key={incident.id}>
                  <button
                    type="button"
                    className={`map-list__item${incident.id === selectedId ? ' is-active' : ''}`}
                    onClick={() => onSelect(incident)}
                  >
                    <span className={`chip chip--${SEVERITY_TOKEN[incident.severity]}`}>
                      {incident.severity}
                    </span>
                    <span className="map-list__body">
                      <span className="map-list__title">{incident.title}</span>
                      <span className="map-list__meta">
                        {incident.district ?? 'Unknown'} ·{' '}
                        {timeAgo(incident.reportedAt)}
                      </span>
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          </aside>
        </div>
      </section>
    </div>
  )
}
