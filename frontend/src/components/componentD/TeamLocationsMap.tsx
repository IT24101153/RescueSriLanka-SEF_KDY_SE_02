import { useEffect, useRef } from 'react'
import { CircleMarker, MapContainer, Popup, TileLayer, useMap } from 'react-leaflet'
import type { CircleMarker as LeafletCircleMarker, Map as LeafletMap } from 'leaflet'
import { TILE_ATTRIBUTION, TILE_SIZE, TILE_URL, TILE_ZOOM_OFFSET } from '../../shared/config/tiles'
import type { RescueTeamDto, TeamStatus } from './types'
import { hasBaseLocation } from './baseLocation'
import type { LocatedTeam } from './baseLocation'
import 'leaflet/dist/leaflet.css'

export type TeamMapFocus = { id: string; request: number } | null

const statuses: Record<TeamStatus, { color: string; label: string }> = {
  Available: { color: '#16804a', label: 'Available' },
  OnMission: { color: '#c47a10', label: 'On mission' },
  OffDuty: { color: '#687385', label: 'Off duty' },
}

function fitTeams(map: LeafletMap, teams: LocatedTeam[]) {
  if (!teams.length) map.setView([7.8731, 80.7718], 7)
  else if (teams.length === 1) map.setView([teams[0].baseLatitude, teams[0].baseLongitude], 12)
  else map.fitBounds(teams.map((team) => [team.baseLatitude, team.baseLongitude]), { padding: [36, 36], maxZoom: 12 })
}

function MapControls({ teams }: { teams: LocatedTeam[] }) {
  const map = useMap()
  const initialized = useRef(false)
  useEffect(() => {
    if (!initialized.current) { fitTeams(map, teams); initialized.current = true }
  }, [map, teams])
  return <div className="team-map__controls"><button type="button" className="btn-ghost" onClick={(event) => { event.stopPropagation(); fitTeams(map, teams) }}>Fit teams</button></div>
}

function TeamMarker({ team, focus }: { team: LocatedTeam; focus: TeamMapFocus }) {
  const map = useMap()
  const marker = useRef<LeafletCircleMarker>(null)
  const selected = focus?.id === team.id
  useEffect(() => {
    if (focus?.id !== team.id) return
    map.setView([team.baseLatitude, team.baseLongitude], 12)
    marker.current?.openPopup()
  }, [focus, team.id, team.baseLatitude, team.baseLongitude, map])
  return <CircleMarker ref={marker} center={[team.baseLatitude, team.baseLongitude]} radius={selected ? 11 : 8}
    pathOptions={{ color: selected ? '#182230' : '#ffffff', weight: selected ? 3 : 2, fillColor: statuses[team.status].color, fillOpacity: 1 }}>
    <Popup><div className="team-map__popup">
      <strong>{team.name}</strong><span>{statuses[team.status].label}</span>
      <span>{team.members.length} members · {team.vehicles.length} vehicles</span>
      <span>Base location</span><span>{team.baseLatitude.toFixed(5)}, {team.baseLongitude.toFixed(5)}</span>
    </div></Popup>
  </CircleMarker>
}

export default function TeamLocationsMap({ teams, focus = null }: { teams: RescueTeamDto[]; focus?: TeamMapFocus }) {
  const located = teams.filter(hasBaseLocation)
  const missing = teams.length - located.length
  return <div className="team-map" aria-label="Team base locations map">
    <div className="team-map__summary" role="status">{located.length} {located.length === 1 ? 'team' : 'teams'} shown
      {missing > 0 && ` · ${missing} ${missing === 1 ? 'team has' : 'teams have'} no base location`}</div>
    {located.length ? <MapContainer center={[7.8731, 80.7718]} zoom={7} maxZoom={18} scrollWheelZoom={false} className="team-map__canvas">
      <TileLayer url={TILE_URL} attribution={TILE_ATTRIBUTION} tileSize={TILE_SIZE} zoomOffset={TILE_ZOOM_OFFSET} />
      <MapControls teams={located} />
      {located.map((team) => <TeamMarker key={team.id} team={team} focus={focus} />)}
    </MapContainer> : <div className="team-map__empty"><strong>No team base locations are available for the current filters.</strong><p>Add a base latitude and longitude when creating or editing a team.</p></div>}
    <ul className="team-map__legend" aria-label="Team status legend">{Object.entries(statuses).map(([status, { color, label }]) =>
      <li key={status}><span aria-hidden="true" style={{ backgroundColor: color }} />{label}</li>)}</ul>
  </div>
}
