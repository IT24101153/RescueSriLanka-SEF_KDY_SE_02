import { useEffect, useRef, useState } from 'react'
import { Circle, CircleMarker, MapContainer, TileLayer, useMap, useMapEvents } from 'react-leaflet'
import type { LatLngBoundsExpression } from 'leaflet'
import { searchPlaces, type PlaceResult } from '../../../shared/api/placeSearch'
import {
  TILE_ATTRIBUTION,
  TILE_SIZE,
  TILE_URL,
  TILE_ZOOM_OFFSET,
} from '../../../shared/config/tiles'
import 'leaflet/dist/leaflet.css'

export type LatLng = { latitude: number; longitude: number }

type LocationPickerProps = {
  value: LatLng | null
  onChange: (value: LatLng) => void
  /** Draws the affected area or zone around the pin, in metres. */
  radiusMeters?: number
  /** Circle colour, e.g. the zone status colour. */
  color?: string
  height?: number
}

const SRI_LANKA_BOUNDS: LatLngBoundsExpression = [
  [5.7, 79.4],
  [10.0, 82.1],
]

/** A click anywhere on the map moves the pin there. */
function ClickToPlace({ onPick }: { onPick: (value: LatLng) => void }) {
  useMapEvents({
    click(event) {
      onPick({ latitude: event.latlng.lat, longitude: event.latlng.lng })
    },
  })
  return null
}

/** Follows the pin when it is set from outside the map (a search result, an edit). */
function FollowPin({ value, zoom }: { value: LatLng | null; zoom: number | null }) {
  const map = useMap()
  useEffect(() => {
    if (value && zoom !== null) map.flyTo([value.latitude, value.longitude], zoom, { duration: 0.5 })
  }, [value, zoom, map])
  return null
}

/**
 * Pick a spot on the island: search a place, or click the map. Used by the
 * coordinator's report form and the zone form, so a location is always chosen
 * by sight rather than typed as coordinates.
 */
export default function LocationPicker({
  value,
  onChange,
  radiusMeters,
  color = '#b45309',
  height = 300,
}: LocationPickerProps) {
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<PlaceResult[]>([])
  const [searching, setSearching] = useState(false)
  const [searchError, setSearchError] = useState<string | null>(null)
  // Only a search result moves the camera; a click already shows where it landed.
  const [flyTo, setFlyTo] = useState<{ value: LatLng; zoom: number } | null>(
    value ? { value, zoom: 13 } : null,
  )
  const searchAbort = useRef<AbortController | null>(null)

  // Too short to search: show nothing, without clearing state from an effect.
  const searchable = query.trim().length >= 3
  const shownResults = searchable ? results : []
  const shownError = searchable ? searchError : null

  useEffect(() => {
    if (query.trim().length < 3) return

    const timer = window.setTimeout(async () => {
      searchAbort.current?.abort()
      const controller = new AbortController()
      searchAbort.current = controller
      setSearching(true)
      try {
        const found = await searchPlaces(query, controller.signal)
        if (controller.signal.aborted) return
        setResults(found)
        setSearchError(found.length === 0 ? 'No place in Sri Lanka matched.' : null)
      } catch {
        if (!controller.signal.aborted) setSearchError('Place search is unavailable — click the map instead.')
      } finally {
        if (!controller.signal.aborted) setSearching(false)
      }
    }, 350)

    return () => window.clearTimeout(timer)
  }, [query])

  function pickPlace(place: PlaceResult) {
    const next = { latitude: place.latitude, longitude: place.longitude }
    onChange(next)
    setFlyTo({ value: next, zoom: 14 })
    setQuery(place.name.split(',')[0])
    setResults([])
  }

  return (
    <div className="picker">
      <div className="picker__search">
        <input
          type="search"
          value={query}
          placeholder="Search a town, road or landmark…"
          onChange={(event) => setQuery(event.target.value)}
          aria-label="Search a place"
        />
        {searching && searchable && <span className="picker__hint">Searching…</span>}
      </div>

      {(shownResults.length > 0 || shownError) && (
        <ul className="picker__results">
          {shownError && <li className="picker__hint">{shownError}</li>}
          {shownResults.map((place) => (
            <li key={`${place.name}-${place.latitude}`}>
              <button type="button" onClick={() => pickPlace(place)}>
                {place.name}
              </button>
            </li>
          ))}
        </ul>
      )}

      <div className="picker__map" style={{ height }}>
        <MapContainer
          bounds={value ? undefined : SRI_LANKA_BOUNDS}
          center={value ? [value.latitude, value.longitude] : undefined}
          zoom={value ? 13 : undefined}
          maxBounds={SRI_LANKA_BOUNDS}
          maxBoundsViscosity={1}
          minZoom={7}
          maxZoom={17}
          scrollWheelZoom
          className="map__canvas"
        >
          <TileLayer
            attribution={TILE_ATTRIBUTION}
            url={TILE_URL}
            tileSize={TILE_SIZE}
            zoomOffset={TILE_ZOOM_OFFSET}
          />
          <ClickToPlace onPick={onChange} />
          <FollowPin value={flyTo?.value ?? null} zoom={flyTo?.zoom ?? null} />

          {value && radiusMeters !== undefined && radiusMeters > 0 && (
            <Circle
              center={[value.latitude, value.longitude]}
              radius={radiusMeters}
              pathOptions={{ color, fillColor: color, fillOpacity: 0.14, weight: 1.5 }}
            />
          )}
          {value && (
            <CircleMarker
              center={[value.latitude, value.longitude]}
              radius={8}
              pathOptions={{ color: '#ffffff', weight: 3, fillColor: '#0b0e13', fillOpacity: 1 }}
            />
          )}
        </MapContainer>
      </div>

      <p className="picker__hint">
        {value
          ? `Pinned at ${value.latitude.toFixed(5)}, ${value.longitude.toFixed(5)} — click the map to move it.`
          : 'Click the map, or search above, to place the pin.'}
      </p>
    </div>
  )
}
