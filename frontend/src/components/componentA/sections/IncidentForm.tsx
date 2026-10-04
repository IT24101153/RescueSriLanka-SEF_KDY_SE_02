import { useState, type FormEvent } from 'react'
import { apiFetch } from '../../../shared/api/client'
import type { Incident, IncidentInput, IncidentType } from '../types'
import { useDistricts } from '../useDistricts'
import LocationPicker, { type LatLng } from './LocationPicker'

const TYPES: IncidentType[] = ['Flood', 'Landslide', 'Fire', 'Accident', 'Storm', 'Tsunami', 'Other']

type IncidentFormProps = {
  /** The report to edit; absent to file a new one. */
  incident?: Incident
  onSaved: (incident: Incident) => void
  onClose: () => void
}

/**
 * A coordinator files a report (a phone call, a field team's word) or corrects
 * one. Their own report is approved on arrival — they are vouching for it —
 * while severity is still proposed by the Incident Analysis Agent and the
 * Enrichment Agent still checks it for duplicates.
 */
export default function IncidentForm({ incident, onSaved, onClose }: IncidentFormProps) {
  const districts = useDistricts()
  const editing = incident !== undefined

  const [title, setTitle] = useState(incident?.title ?? '')
  const [description, setDescription] = useState(incident?.description ?? '')
  const [type, setType] = useState<IncidentType>(incident?.type ?? 'Flood')
  const [district, setDistrict] = useState(incident?.district ?? '')
  const [address, setAddress] = useState(incident?.addressText ?? '')
  const [people, setPeople] = useState(incident?.estimatedAffectedPeople?.toString() ?? '')
  const [radius, setRadius] = useState(incident?.affectedRadiusMeters ?? 1000)
  const [location, setLocation] = useState<LatLng | null>(
    incident ? { latitude: incident.latitude, longitude: incident.longitude } : null,
  )

  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!location) {
      setError('Place the pin on the map first.')
      return
    }

    const body: IncidentInput = {
      title: title.trim(),
      description: description.trim(),
      type,
      latitude: location.latitude,
      longitude: location.longitude,
      affectedRadiusMeters: radius,
      district: district || null,
      addressText: address.trim() || null,
      estimatedAffectedPeople: people.trim() === '' ? null : Number(people),
    }

    setSaving(true)
    setError(null)
    try {
      const saved = editing
        ? await apiFetch<Incident>(`/api/incidents/${incident.id}`, { method: 'PUT', body: JSON.stringify(body) })
        : await apiFetch<Incident>('/api/incidents', { method: 'POST', body: JSON.stringify(body) })
      onSaved(saved)
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Could not save the report.')
      setSaving(false)
    }
  }

  return (
    <div className="drawer-overlay">
      <div className="drawer__scrim" onClick={onClose} aria-hidden="true" />
      <form className="modal" aria-label={editing ? 'Edit report' : 'New report'} onSubmit={submit}>
        <header className="modal__head">
          <div>
            <h3 className="modal__title">{editing ? 'Edit report details' : 'File a report'}</h3>
            <p className="modal__sub">
              {editing
                ? 'Correct what the reporter got wrong. Severity and status keep their own controls.'
                : 'Your report is approved on arrival. The AI agents still grade its severity and check it for duplicates.'}
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
            <span>Short summary</span>
            <input
              required
              minLength={3}
              maxLength={200}
              value={title}
              onChange={(event) => setTitle(event.target.value)}
              placeholder="Kelani bridge road flooded"
            />
          </label>

          <label className="field field--wide">
            <span>What is happening</span>
            <textarea
              required
              minLength={3}
              maxLength={4000}
              rows={3}
              value={description}
              onChange={(event) => setDescription(event.target.value)}
              placeholder="Police report the road under waist-deep water; about 40 families cut off."
            />
          </label>

          <label className="field">
            <span>Type</span>
            <select value={type} onChange={(event) => setType(event.target.value as IncidentType)}>
              {TYPES.map((value) => (
                <option key={value} value={value}>
                  {value}
                </option>
              ))}
            </select>
          </label>

          <label className="field">
            <span>District</span>
            <select value={district} onChange={(event) => setDistrict(event.target.value)}>
              <option value="">Not known</option>
              {districts.map((value) => (
                <option key={value} value={value}>
                  {value}
                </option>
              ))}
              {district && !districts.includes(district) && <option value={district}>{district}</option>}
            </select>
          </label>

          <label className="field">
            <span>People affected</span>
            <input
              type="number"
              min={0}
              max={1000000}
              value={people}
              onChange={(event) => setPeople(event.target.value)}
              placeholder="Estimate"
            />
          </label>

          <label className="field">
            <span>Affected radius — {(radius / 1000).toFixed(1)} km</span>
            <input
              type="range"
              min={100}
              max={10000}
              step={100}
              value={radius}
              onChange={(event) => setRadius(Number(event.target.value))}
            />
          </label>

          <label className="field field--wide">
            <span>Landmark or address</span>
            <input
              maxLength={300}
              value={address}
              onChange={(event) => setAddress(event.target.value)}
              placeholder="Near the Kelani bridge, Peliyagoda side"
            />
          </label>

          <div className="field field--wide">
            <span>Location</span>
            <LocationPicker value={location} onChange={setLocation} radiusMeters={radius} />
          </div>
        </div>

        <footer className="modal__foot">
          <button type="button" className="btn-link" onClick={onClose} disabled={saving}>
            Cancel
          </button>
          <button type="submit" className="btn-approve" disabled={saving || !location}>
            {saving ? 'Saving…' : editing ? 'Save changes' : 'File report'}
          </button>
        </footer>
      </form>
    </div>
  )
}
