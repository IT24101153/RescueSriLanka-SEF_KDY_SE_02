import { useEffect, useId, useRef, useState } from 'react'
import { MapContainer, Marker, TileLayer, useMap, useMapEvents } from 'react-leaflet'
import { divIcon } from 'leaflet'
import type { LeafletEvent, Marker as LeafletMarker } from 'leaflet'
import { TILE_URL, TILE_ATTRIBUTION, TILE_SIZE, TILE_ZOOM_OFFSET } from '../../shared/config/tiles'
import { hasBaseLocation } from './baseLocation'
import { searchPlaces } from '../../shared/api/placeSearch'
import type { PlaceResult } from '../../shared/api/placeSearch'
import 'leaflet/dist/leaflet.css'

type Location = { baseLatitude: number; baseLongitude: number }
type Props = {
  latitude: number | null
  longitude: number | null
  onConfirm: (latitude: number, longitude: number) => void
  onCancel: () => void
}

const baseIcon = divIcon({ className: 'team-picker__marker', html: '<span></span>', iconSize: [28, 28], iconAnchor: [14, 14] })

function ClickSelection({ onSelect }: { onSelect: (latitude: number, longitude: number) => void }) {
  useMapEvents({ click: (event) => onSelect(event.latlng.lat, event.latlng.lng) })
  return null
}

function SearchView({ place }: { place: PlaceResult | null }) {
  const map = useMap()
  useEffect(() => { if (place) map.setView([place.latitude, place.longitude], 15) }, [map, place])
  return null
}

export default function TeamLocationPicker({ latitude, longitude, onConfirm, onCancel }: Props) {
  const initial = { baseLatitude: latitude, baseLongitude: longitude }
  const [selected, setSelected] = useState<Location | null>(() => hasBaseLocation(initial) ? initial : null)
  const titleId = useId()
  const heading = useRef<HTMLHeadingElement>(null)
  const searchId = useId()
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<PlaceResult[]>([])
  const [searchState, setSearchState] = useState<'idle' | 'loading' | 'done' | 'error'>('idle')
  const [searchPlace, setSearchPlace] = useState<PlaceResult | null>(null)
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null)
  const request = useRef<AbortController | null>(null)
  const generation = useRef(0)
  const cache = useRef(new Map<string, PlaceResult[]>())
  useEffect(() => () => { if (timer.current) clearTimeout(timer.current); request.current?.abort(); generation.current++ }, [])
  useEffect(() => { heading.current?.focus({ preventScroll: true }) }, [])

  function stopSearch() {
    if (timer.current) clearTimeout(timer.current)
    request.current?.abort()
    generation.current++
  }
  async function search(value: string) {
    stopSearch()
    const trimmed = value.trim()
    if (trimmed.length < 3) { setSearchState('idle'); setResults([]); return }
    const current = generation.current
    const controller = new AbortController()
    request.current = controller
    setSearchState('loading'); setResults([])
    try {
      const places = cache.current.get(trimmed) ?? await searchPlaces(trimmed, controller.signal)
      if (current !== generation.current || controller.signal.aborted) return
      if (cache.current.size >= 20) cache.current.clear()
      cache.current.set(trimmed, places)
      setResults(places); setSearchState('done')
    } catch {
      if (current === generation.current && !controller.signal.aborted) setSearchState('error')
    }
  }
  function changeQuery(value: string) {
    stopSearch(); setQuery(value); setResults([]); setSearchState('idle')
    if (value.trim().length >= 3) timer.current = setTimeout(() => void search(value), 500)
  }
  function choosePlace(place: PlaceResult) {
    stopSearch(); setSelected({ baseLatitude: place.latitude, baseLongitude: place.longitude })
    setSearchPlace({ ...place }); setResults([]); setSearchState('idle')
  }

  function select(lat: number, lng: number) {
    // Leaflet may report longitudes in adjacent world copies after panning.
    const location = { baseLatitude: lat, baseLongitude: lng >= -180 && lng <= 180 ? lng : ((lng + 180) % 360 + 360) % 360 - 180 }
    if (hasBaseLocation(location)) setSelected(location)
  }
  function dragEnd(event: LeafletEvent) {
    const position = (event.target as LeafletMarker).getLatLng()
    select(position.lat, position.lng)
  }

  return <div className="team-picker" role="region" aria-labelledby={titleId}>
    <div className="team-picker__heading"><h4 id={titleId} ref={heading} tabIndex={-1}>Select team base location</h4>
      <p>Click the map or drag the marker to choose the team’s registered base.</p><p>This is a static base location, not live tracking. Manual coordinate inputs remain available above.</p></div>
    <div className="team-picker__search">
      <label htmlFor={searchId}>Search location</label>
      <div className="team-picker__search-controls"><input id={searchId} value={query} placeholder="Search for a place in Sri Lanka" autoComplete="off"
        onChange={(event) => changeQuery(event.target.value)} onKeyDown={(event) => { if (event.key === 'Enter') { event.preventDefault(); if (searchState !== 'loading') void search(query) } }} />
        <button type="button" className="btn-ghost" disabled={query.trim().length < 3 || searchState === 'loading'} onClick={() => void search(query)}>Search</button>
        {query && <button type="button" className="btn-ghost" onClick={() => changeQuery('')}>Clear search</button>}</div>
      <div role="status">{searchState === 'loading' ? 'Searching...' : searchState === 'error' ? 'Unable to search locations. Try again.' : searchState === 'done' && !results.length ? 'No locations found.' : ''}</div>
      {results.length > 0 && <ul className="team-picker__results" aria-label="Location search results">{results.map((place) => <li key={`${place.name}:${place.latitude}:${place.longitude}`}><button type="button" onClick={() => choosePlace(place)}>{place.name}</button></li>)}</ul>}
      <small>Search by <a href="https://photon.komoot.io" target="_blank" rel="noreferrer">Photon</a> · © <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noreferrer">OpenStreetMap contributors</a></small>
    </div>
    <MapContainer center={selected ? [selected.baseLatitude, selected.baseLongitude] : [7.8731, 80.7718]} zoom={selected ? 12 : 7} maxZoom={18} scrollWheelZoom={false} className="team-picker__canvas">
      <TileLayer url={TILE_URL} attribution={TILE_ATTRIBUTION} tileSize={TILE_SIZE} zoomOffset={TILE_ZOOM_OFFSET} />
      <ClickSelection onSelect={select} />
      <SearchView place={searchPlace} />
      {selected && <Marker position={[selected.baseLatitude, selected.baseLongitude]} icon={baseIcon} draggable title="Selected team base location" eventHandlers={{ dragend: dragEnd }} />}
    </MapContainer>
    <div className="team-picker__selection" role="status">{selected ? <><span>Latitude: {selected.baseLatitude.toFixed(6)}</span><span>Longitude: {selected.baseLongitude.toFixed(6)}</span></> : 'No base location selected. Click the map to choose one.'}</div>
    <div className="team-picker__actions"><button type="button" className="btn-ghost" onClick={onCancel}>Cancel</button>
      <button type="button" className="btn" disabled={!selected} onClick={() => { if (selected) onConfirm(selected.baseLatitude, selected.baseLongitude) }}>Use location</button></div>
  </div>
}
