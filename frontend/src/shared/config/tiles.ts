/**
 * Basemap tile source for the console's maps.
 *
 * Mapbox when a token is supplied, OpenStreetMap when it is not. No token is
 * committed: GitHub's push protection rejects Mapbox tokens outright, and a
 * credential in the repository is wrong regardless of whether a scanner
 * catches it.
 *
 * To use Mapbox locally, put the token in `frontend/.env` (gitignored):
 *
 *     VITE_MAPBOX_TOKEN=pk....
 *     VITE_MAPBOX_STYLE=mapbox/streets-v12   # optional
 *
 * Without it the console still works — it just draws OpenStreetMap tiles,
 * which is what the Flutter app uses too. See docs/adr/0003-map-tile-provider.md.
 */
const token: string = import.meta.env.VITE_MAPBOX_TOKEN ?? ''

/** True when a Mapbox token was supplied at build time. */
export const MAPBOX_ENABLED = token.length > 0

/**
 * `streets-v12` keeps road names legible, which is what a coordinator routing
 * a team needs. `outdoors-v12` adds terrain contours for landslide country;
 * `satellite-streets-v12` for imagery.
 */
const style: string = import.meta.env.VITE_MAPBOX_STYLE ?? 'mapbox/streets-v12'

export const TILE_URL = MAPBOX_ENABLED
  ? `https://api.mapbox.com/styles/v1/${style}/tiles/{z}/{x}/{y}@2x?access_token=${token}`
  : 'https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png'

/**
 * Leaflet assumes 256px tiles. Mapbox serves 512px ones, which need an
 * explicit size and a -1 zoom offset or every zoom level is off by one.
 */
export const TILE_SIZE = MAPBOX_ENABLED ? 512 : 256
export const TILE_ZOOM_OFFSET = MAPBOX_ENABLED ? -1 : 0

/** Mapbox's terms require their credit; the OSM credit is required either way. */
export const TILE_ATTRIBUTION = MAPBOX_ENABLED
  ? '&copy; <a href="https://www.mapbox.com/about/maps/">Mapbox</a> ' +
    '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> ' +
    '<a href="https://www.mapbox.com/map-feedback/" target="_blank" rel="noreferrer">Improve this map</a>'
  : '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
