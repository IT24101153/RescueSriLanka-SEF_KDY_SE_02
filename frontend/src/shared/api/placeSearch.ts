// Matches mobile/lib/shared/services/place_search_service.dart's Photon provider.
// Public demo fair use: https://github.com/komoot/photon#demo-server
export type PlaceResult = { name: string; latitude: number; longitude: number }

function record(value: unknown): Record<string, unknown> {
  return value !== null && typeof value === 'object' ? value as Record<string, unknown> : {}
}

export async function searchPlaces(query: string, signal: AbortSignal): Promise<PlaceResult[]> {
  if (query.trim().length < 3) return []
  const params = new URLSearchParams({ q: query.trim(), limit: '5', lang: 'en', bbox: '79.5,5.8,82.0,9.9' })
  // Deliberately bypass the authenticated application API client: only the query
  // and public search parameters belong on this external request.
  const response = await fetch(`https://photon.komoot.io/api/?${params}`, {
    signal: AbortSignal.any([signal, AbortSignal.timeout(12000)]), credentials: 'omit', referrerPolicy: 'strict-origin',
  })
  if (!response.ok) throw new Error('Place search unavailable')
  const body = record(await response.json())
  if (!Array.isArray(body.features)) throw new Error('Invalid place response')
  const results: PlaceResult[] = []
  for (const item of body.features) {
    const feature = record(item), properties = record(feature.properties)
    const coordinates = record(feature.geometry).coordinates
    if (properties.countrycode !== 'LK' || !Array.isArray(coordinates)) continue
    const [longitude, latitude] = coordinates
    if (typeof latitude !== 'number' || typeof longitude !== 'number'
      || !Number.isFinite(latitude) || !Number.isFinite(longitude)
      || latitude < 5.8 || latitude > 9.9 || longitude < 79.5 || longitude > 82) continue
    const parts = ['name', 'street', 'locality', 'city', 'county', 'state', 'country']
      .map((key) => properties[key]).filter((part): part is string => typeof part === 'string' && part.trim().length > 0)
    if (!parts.length) continue
    const name = [...new Set(parts)].join(', ')
    if (!results.some((place) => place.name === name && place.latitude === latitude && place.longitude === longitude)) results.push({ name, latitude, longitude })
    if (results.length === 5) break
  }
  return results
}
