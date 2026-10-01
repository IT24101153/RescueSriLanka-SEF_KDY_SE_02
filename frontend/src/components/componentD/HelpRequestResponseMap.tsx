import { useEffect } from 'react'
import { CircleMarker, MapContainer, Popup, TileLayer, useMap } from 'react-leaflet'
import { TILE_ATTRIBUTION, TILE_SIZE, TILE_URL, TILE_ZOOM_OFFSET } from '../../shared/config/tiles'
import { hasBaseLocation } from './baseLocation'
import type { RescueCandidate, RescueHelpRequest } from './types'
import 'leaflet/dist/leaflet.css'

function FitResponse({ request, teams }: { request: RescueHelpRequest; teams: RescueCandidate[] }) {
  const map = useMap()
  useEffect(() => {
    map.fitBounds([[request.latitude!, request.longitude!], ...teams.map((team): [number, number] => [team.baseLatitude, team.baseLongitude])], { padding: [32, 32], maxZoom: 13 })
  }, [map, request, teams])
  return null
}

export default function HelpRequestResponseMap({ request, candidates, recommendedTeamId, selectedTeamId }: {
  request: RescueHelpRequest; candidates: RescueCandidate[]; recommendedTeamId?: string; selectedTeamId?: string;
}) {
  const teams = [...new Map(candidates.filter(hasBaseLocation).map((team) => [team.teamId, team])).values()]
  const locationValid = hasBaseLocation({ baseLatitude: request.latitude, baseLongitude: request.longitude })
  return <section aria-label="Help Request and registered team bases">
    <p>Team markers show registered bases, not live positions.</p>
    <p>Orange: Help Request · Purple: recommended base · Green outline: selected base</p>
    {locationValid ? <MapContainer className="help-response-map" center={[request.latitude!, request.longitude!]} zoom={12} scrollWheelZoom={false}>
      <TileLayer url={TILE_URL} attribution={TILE_ATTRIBUTION} tileSize={TILE_SIZE} zoomOffset={TILE_ZOOM_OFFSET} />
      <FitResponse request={request} teams={teams} />
      <CircleMarker center={[request.latitude!, request.longitude!]} pathOptions={{ color: '#b45309', fillColor: '#f59e0b', fillOpacity: 1 }}>
        <Popup>Help Request: {request.type}</Popup>
      </CircleMarker>
      {teams.map((team) => <CircleMarker key={team.teamId} center={[team.baseLatitude, team.baseLongitude]} radius={team.teamId === recommendedTeamId ? 11 : 8}
        pathOptions={{ color: team.teamId === selectedTeamId ? '#15803d' : '#475569', weight: team.teamId === selectedTeamId ? 5 : 2, fillColor: team.teamId === recommendedTeamId ? '#7c3aed' : '#64748b', fillOpacity: 1 }}>
        <Popup>{team.teamName} — registered base<br />Straight-line distance: {team.distanceKm.toFixed(2)} km
          {team.teamId === recommendedTeamId && <p>Recommended team</p>}{team.teamId === selectedTeamId && <p>Selected team</p>}
        </Popup>
      </CircleMarker>)}
    </MapContainer> : <p role="alert">Help Request location is missing or invalid.</p>}
  </section>
}
