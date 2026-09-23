import { useEffect } from 'react'
import {
  Circle,
  CircleMarker,
  MapContainer,
  Popup,
  TileLayer,
  useMap,
} from 'react-leaflet'
import type { LatLngBoundsExpression } from 'leaflet'
import type { Incident, SafetyZone } from '../types'
import { SEVERITY_HEX, SEVERITY_RADIUS, ZONE_HEX, timeAgo } from '../severity'
import {
  TILE_ATTRIBUTION,
  TILE_SIZE,
  TILE_URL,
  TILE_ZOOM_OFFSET,
} from '../../../shared/config/tiles'
import 'leaflet/dist/leaflet.css'

type IncidentMapProps = {
  incidents: Incident[]
  zones: SafetyZone[]
  showZones: boolean
  showIncidents?: boolean
  selectedId?: string | null
  height?: number
  onSelect: (incident: Incident) => void
}

/**
 * Island bounding box with a small margin. The map is hard-locked to this —
 * panning away from Sri Lanka is not a thing a coordinator ever needs to do,
 * and it stops the view getting lost in the Indian Ocean.
 */
const SRI_LANKA_BOUNDS: LatLngBoundsExpression = [
  [5.70, 79.40],
  [10.00, 82.10],
]

/** Small control that snaps the view back to the whole island. */
function ResetViewControl() {
  const map = useMap()

  return (
    <button
      type="button"
      className="map__reset"
      onClick={() => map.fitBounds(SRI_LANKA_BOUNDS)}
    >
      Fit island
    </button>
  )
}

/** Pans to the incident selected elsewhere in the dashboard. */
function FlyToSelected({ incident }: { incident: Incident | undefined }) {
  const map = useMap()

  useEffect(() => {
    if (incident) {
      map.flyTo([incident.latitude, incident.longitude], 12, { duration: 0.6 })
    }
  }, [incident, map])

  return null
}

export default function IncidentMap({
  incidents,
  zones,
  showZones,
  showIncidents = true,
  selectedId = null,
  height = 460,
  onSelect,
}: IncidentMapProps) {
  const selected = incidents.find((incident) => incident.id === selectedId)

  return (
    <div className="map" style={{ height }}>
      <MapContainer
        bounds={SRI_LANKA_BOUNDS}
        maxBounds={SRI_LANKA_BOUNDS}
        maxBoundsViscosity={1}
        minZoom={7}
        maxZoom={16}
        scrollWheelZoom
        className="map__canvas"
      >
        <TileLayer
          attribution={TILE_ATTRIBUTION}
          url={TILE_URL}
          tileSize={TILE_SIZE}
          zoomOffset={TILE_ZOOM_OFFSET}
          bounds={SRI_LANKA_BOUNDS}
        />

        <ResetViewControl />
        <FlyToSelected incident={selected} />

        {showZones &&
          zones.map((zone) => (
            <Circle
              key={zone.id}
              center={[zone.centerLatitude, zone.centerLongitude]}
              radius={zone.radiusMeters}
              pathOptions={{
                color: ZONE_HEX[zone.status],
                fillColor: ZONE_HEX[zone.status],
                fillOpacity: 0.12,
                weight: 1.5,
                dashArray: zone.source === 'ManualOverride' ? undefined : '4 4',
              }}
            />
          ))}

        {showIncidents &&
          incidents.map((incident) => {
            const isSelected = incident.id === selectedId
            return (
              <CircleMarker
                key={incident.id}
                center={[incident.latitude, incident.longitude]}
                radius={SEVERITY_RADIUS[incident.severity] + (isSelected ? 4 : 0)}
                pathOptions={{
                  color: isSelected ? '#0b0e13' : '#ffffff',
                  weight: isSelected ? 3 : 2,
                  fillColor: SEVERITY_HEX[incident.severity],
                  fillOpacity: 1,
                }}
                eventHandlers={{ click: () => onSelect(incident) }}
              >
                <Popup>
                  <strong className="pop__title">{incident.title}</strong>
                  <span className="pop__line">
                    {incident.severity} · {incident.type}
                  </span>
                  <span className="pop__line">
                    {incident.district ?? 'Unknown district'} ·{' '}
                    {timeAgo(incident.reportedAt)}
                  </span>
                </Popup>
              </CircleMarker>
            )
          })}
      </MapContainer>

      <div className="map__legend">
        {showIncidents && (
          <div className="map__legend-group">
            <span className="map__legend-title">Severity</span>
            {(['Low', 'Moderate', 'High', 'Critical'] as const).map((severity) => (
              <span key={severity} className="map__legend-item">
                <i
                  className="map__dot"
                  style={{
                    background: SEVERITY_HEX[severity],
                    // Marker size is a redundant encoding of severity, so the
                    // legend has to reproduce the size difference too.
                    width: SEVERITY_RADIUS[severity] * 2,
                    height: SEVERITY_RADIUS[severity] * 2,
                  }}
                />
                {severity}
              </span>
            ))}
          </div>
        )}

        {showZones && (
          <div className="map__legend-group">
            <span className="map__legend-title">Zones</span>
            {(['Danger', 'Caution', 'Safe'] as const).map((status) => (
              <span key={status} className="map__legend-item">
                <i
                  className="map__zone-swatch"
                  style={{
                    borderColor: ZONE_HEX[status],
                    background: `${ZONE_HEX[status]}1f`,
                  }}
                />
                {status}
              </span>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
