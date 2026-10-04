import { useState, type FormEvent } from 'react'
import { apiFetch } from '../../../shared/api/client'
import type { SafetyZone, SafetyZoneInput, ZoneStatus } from '../types'
import { ZONE_HEX } from '../severity'
import { useDistricts } from '../useDistricts'
import LocationPicker, { type LatLng } from './LocationPicker'

const STATUSES: ZoneStatus[] = ['Danger', 'Caution', 'Safe']

type ZoneFormProps = {
  /** The manual zone to edit; absent to declare a new one. */
  zone?: SafetyZone
  onSaved: () => void
  onClose: () => void
}

/** `datetime-local` wants local time without a zone. */
function toLocalInput(iso: string | null | undefined): string {
  if (!iso) return ''
  const date = new Date(iso)
  return new Date(date.getTime() - date.getTimezoneOffset() * 60000).toISOString().slice(0, 16)
}

/**
 * Declare or edit a zone by hand — an evacuation area, a closed road, a safe
 * shelter. Derived zones are not edited here: they follow their incident.
 */
export default function ZoneForm({ zone, onSaved, onClose }: ZoneFormProps) {
  const districts = useDistricts()
  const editing = zone !== undefined

  const [name, setName] = useState(zone?.name ?? '')
  const [status, setStatus] = useState<ZoneStatus>(zone?.status ?? 'Danger')
  const [radius, setRadius] = useState(zone?.radiusMeters ?? 2000)
  const [district, setDistrict] = useState(zone?.district ?? '')
  const [rationale, setRationale] = useState(zone?.rationale ?? '')
  const [expiresAt, setExpiresAt] = useState(toLocalInput(zone?.expiresAt))
  const [centre, setCentre] = useState<LatLng | null>(
    zone ? { latitude: zone.centerLatitude, longitude: zone.centerLongitude } : null,
  )

  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!centre) {
      setError('Place the zone centre on the map first.')
      return
    }

    const body: SafetyZoneInput = {
      name: name.trim(),
      status,
      centerLatitude: centre.latitude,
      centerLongitude: centre.longitude,
      radiusMeters: radius,
      district: district || null,
      rationale: rationale.trim() || null,
      expiresAt: expiresAt ? new Date(expiresAt).toISOString() : null,
    }

    setSaving(true)
    setError(null)
    try {
      await apiFetch(editing ? `/api/safetyzones/${zone.id}` : '/api/safetyzones', {
        method: editing ? 'PUT' : 'POST',
        body: JSON.stringify(body),
      })
      onSaved()
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Could not save the zone.')
      setSaving(false)
    }
  }

  return (
    <div className="drawer-overlay">
      <div className="drawer__scrim" onClick={onClose} aria-hidden="true" />
      <form className="modal" aria-label={editing ? 'Edit zone' : 'New zone'} onSubmit={submit}>
        <header className="modal__head">
          <div>
            <h3 className="modal__title">{editing ? 'Edit safety zone' : 'Declare a safety zone'}</h3>
            <p className="modal__sub">
              Shown on the public map and used by the “am I safe here?” check straight away.
            </p>
          </div>
          <button type="button" className="drawer__close" onClick={onClose} aria-label="Close">
            ×
          </button>
        </header>

        {error && (
          <div className="alert" role="alert">
            {error}
          </div>
        )}

        <div className="form-grid">
          <label className="field field--wide">
            <span>Name</span>
            <input
              required
              minLength={3}
              maxLength={200}
              value={name}
              onChange={(event) => setName(event.target.value)}
              placeholder="Kelani river evacuation area"
            />
          </label>

          <label className="field">
            <span>Status</span>
            <select value={status} onChange={(event) => setStatus(event.target.value as ZoneStatus)}>
              {STATUSES.map((value) => (
                <option key={value} value={value}>
                  {value}
                </option>
              ))}
            </select>
          </label>

          <label className="field">
            <span>District</span>
            <select value={district} onChange={(event) => setDistrict(event.target.value)}>
              <option value="">Not set</option>
              {districts.map((value) => (
                <option key={value} value={value}>
                  {value}
                </option>
              ))}
            </select>
          </label>

          <label className="field">
            <span>Radius — {(radius / 1000).toFixed(1)} km</span>
            <input
              type="range"
              min={100}
              max={20000}
              step={100}
              value={radius}
              onChange={(event) => setRadius(Number(event.target.value))}
            />
          </label>

          <label className="field">
            <span>Expires (optional)</span>
            <input type="datetime-local" value={expiresAt} onChange={(event) => setExpiresAt(event.target.value)} />
          </label>

          <label className="field field--wide">
            <span>Why</span>
            <input
              maxLength={500}
              value={rationale}
              onChange={(event) => setRationale(event.target.value)}
              placeholder="River above flood level; homes along the bank inundated."
            />
          </label>

          <div className="field field--wide">
            <span>Centre</span>
            <LocationPicker value={centre} onChange={setCentre} radiusMeters={radius} color={ZONE_HEX[status]} />
          </div>
        </div>

        <footer className="modal__foot">
          <button type="button" className="btn-link" onClick={onClose} disabled={saving}>
            Cancel
          </button>
          <button type="submit" className="btn-approve" disabled={saving || !centre}>
            {saving ? 'Saving…' : editing ? 'Save zone' : 'Declare zone'}
          </button>
        </footer>
      </form>
    </div>
  )
}
