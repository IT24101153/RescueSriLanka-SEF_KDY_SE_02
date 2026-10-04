import { useCallback, useState } from 'react'
import { apiFetch } from '../../../shared/api/client'
import type { Incident, SafetyZone, ZoneProposal, ZoneStatus } from '../types'
import IncidentMap from './IncidentMap'
import ZoneForm from './ZoneForm'
import ZonePlanner from './ZonePlanner'
import { ZONE_TOKEN, timeUntil } from '../severity'

type ZonesSectionProps = {
  zones: SafetyZone[]
  incidents: Incident[]
  onSelect: (incident: Incident) => void
  /** After a zone is declared, edited, retired or a plan approved. */
  onChanged: () => void
}

const ZONE_ORDER: ZoneStatus[] = ['Danger', 'Caution', 'Safe']

export default function ZonesSection({
  zones,
  incidents,
  onSelect,
  onChanged,
}: ZonesSectionProps) {
  const [preview, setPreview] = useState<ZoneProposal[]>([])
  // undefined: closed · null: new zone · a zone: editing it
  const [editing, setEditing] = useState<SafetyZone | null | undefined>(undefined)
  const [retiring, setRetiring] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const onPreview = useCallback((next: ZoneProposal[]) => setPreview(next), [])

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

  async function retire(zone: SafetyZone) {
    setRetiring(zone.id)
    setError(null)
    try {
      await apiFetch(`/api/safetyzones/${zone.id}`, { method: 'DELETE' })
      onChanged()
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Could not retire the zone.')
    } finally {
      setRetiring(null)
    }
  }

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
          <p className="stat__hint">{manual} manual zone(s)</p>
        </article>
        <article className="stat">
          <p className="stat__label">Area covered</p>
          <p className="stat__value">{Math.round(coverageKm2)}</p>
          <p className="stat__hint">km² inside a zone</p>
        </article>
      </section>

      <ZonePlanner onPreview={onPreview} onChanged={onChanged} />

      <section className="panel" aria-label="Safety zone map">
        <header className="panel__head">
          <h2 className="panel__title">Zone coverage</h2>
          <span className="panel__meta">
            {preview.length > 0 ? `${preview.length} proposed zone(s) shown dashed` : 'incidents hidden — zones only'}
          </span>
        </header>
        <IncidentMap
          incidents={incidents}
          zones={zones}
          showZones
          showIncidents={false}
          previewZones={preview}
          height={400}
          onSelect={onSelect}
        />
      </section>

      <section className="panel" aria-label="Safety zone list">
        <header className="panel__head">
          <h2 className="panel__title">All safety zones</h2>
          <div className="panel__tools">
            <span className="panel__meta">{zones.length} active</span>
            <button type="button" className="btn-small" onClick={() => setEditing(null)}>
              + New zone
            </button>
          </div>
        </header>

        {error && (
          <div className="alert" role="alert">
            {error}
          </div>
        )}

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
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {ordered.map((zone) => {
                  const isManual = zone.source === 'ManualOverride'
                  return (
                    <tr key={zone.id}>
                      <td>
                        <span className={`chip chip--${ZONE_TOKEN[zone.status]}`}>
                          {zone.status}
                        </span>
                      </td>
                      <td className="cell-title">
                        {zone.name}
                        {zone.expiresAt && (
                          <span className="muted"> · expires {timeUntil(zone.expiresAt)}</span>
                        )}
                      </td>
                      <td>{zone.district ?? '—'}</td>
                      <td className="num">
                        {(zone.radiusMeters / 1000).toFixed(1)} km
                      </td>
                      <td>
                        <span className="state">
                          {isManual ? (zone.sourceAgentRunId ? 'Agent plan' : 'Manual') : 'Derived'}
                        </span>
                      </td>
                      <td className="col-wide">
                        <span className="state">{zone.rationale ?? '—'}</span>
                      </td>
                      <td>
                        {isManual ? (
                          <span className="row-actions">
                            <button type="button" className="btn-link" onClick={() => setEditing(zone)}>
                              Edit
                            </button>
                            <button
                              type="button"
                              className="btn-link review__delete"
                              disabled={retiring !== null}
                              onClick={() => void retire(zone)}
                            >
                              {retiring === zone.id ? 'Retiring…' : 'Retire'}
                            </button>
                          </span>
                        ) : (
                          <span className="muted" title="Follows its incident — edit the incident instead">
                            from incident
                          </span>
                        )}
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {editing !== undefined && (
        <ZoneForm
          zone={editing ?? undefined}
          onClose={() => setEditing(undefined)}
          onSaved={() => {
            setEditing(undefined)
            onChanged()
          }}
        />
      )}
    </div>
  )
}
