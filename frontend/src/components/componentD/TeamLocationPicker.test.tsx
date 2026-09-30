import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import type { ReactNode } from 'react'
import TeamLocationPicker from './TeamLocationPicker'
import ResourceManagementPanel from './ResourceManagementPanel'
import type { RescueTeamDto } from './types'

const map = vi.hoisted(() => ({ click: undefined as undefined | ((event: { latlng: { lat: number; lng: number } }) => void), setView: vi.fn(), fitBounds: vi.fn() }))
vi.mock('react-leaflet', () => ({
  MapContainer: ({ children, center, zoom }: { children: ReactNode; center: number[]; zoom: number }) => <div data-testid="map" data-center={center.join(',')} data-zoom={zoom}><button type="button" onClick={() => map.click?.({ latlng: { lat: 8.123456789, lng: 81.234567891 } })}>Mock map click</button>{children}</div>,
  TileLayer: () => null,
  Marker: ({ position, draggable, eventHandlers }: { position: number[]; draggable: boolean; eventHandlers: { dragend: (event: unknown) => void } }) => <button type="button" data-testid="selected-marker" data-position={position.join(',')} data-draggable={String(draggable)} onClick={() => eventHandlers.dragend({ target: { getLatLng: () => ({ lat: 6.25, lng: 79.75 }) } })}>Mock drag marker</button>,
  CircleMarker: ({ children, center }: { children: ReactNode; center: number[] }) => <div data-testid="team-marker" data-position={center.join(',')}>{children}</div>,
  Popup: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  useMapEvents: (events: typeof map) => { map.click = events.click },
  useMap: () => map,
}))

beforeEach(() => vi.clearAllMocks())
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.useRealTimers(); map.click = undefined })
const fixture: RescueTeamDto = { id: '1', name: 'Kandy', status: 'Available', baseLatitude: 7.29, baseLongitude: 80.63,
  members: [{ id: 'm', fullName: 'Private Person', phone: '0712345678', skill: 'FirstAid', isAvailable: true }],
  vehicles: [{ id: 'v', plateNumber: 'PRIVATE-PLATE', type: 'Ambulance', status: 'Available', capacity: 3 }] }

function picker(latitude: number | null = null, longitude: number | null = null) {
  const onConfirm = vi.fn(), onCancel = vi.fn()
  render(<TeamLocationPicker latitude={latitude} longitude={longitude} onConfirm={onConfirm} onCancel={onCancel} />)
  return { onConfirm, onCancel }
}
function clickMap() { fireEvent.click(screen.getByRole('button', { name: 'Mock map click' })) }
function useLocation() { fireEvent.click(screen.getByRole('button', { name: 'Use location' })) }
function openPicker(form: HTMLFormElement) { fireEvent.click(within(form).getByRole('button', { name: 'Pick on map' })) }
function input(form: HTMLFormElement, label: string) { return within(form).getByLabelText(label) as HTMLInputElement }
function createForm() { return screen.getByRole('button', { name: 'Create Team' }).closest('form')! }
function editForm() {
  const details = screen.getByText('Edit Team').closest('details')!
  details.open = true
  return details.querySelector('form')!
}
function mount(teams: RescueTeamDto[] = []) {
  const refresh = vi.fn().mockResolvedValue(undefined)
  const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify(fixture), { status: 200 }))
  vi.stubGlobal('fetch', fetch)
  return { ...render(<ResourceManagementPanel teams={teams} refresh={refresh} />), refresh, fetch }
}

describe('location picker', () => {
  it('renders instructions and uses an unselected Sri Lanka default', () => {
    const { onConfirm } = picker()
    expect(screen.getByRole('heading', { name: 'Select team base location' })).toBeTruthy()
    expect(screen.getByText('Click the map or drag the marker to choose the team’s registered base.')).toBeTruthy()
    expect(screen.getByTestId('map').dataset.center).toBe('7.8731,80.7718')
    expect(screen.getByTestId('map').dataset.zoom).toBe('7')
    expect(screen.queryByTestId('selected-marker')).toBeNull()
    expect((screen.getByRole('button', { name: 'Use location' }) as HTMLButtonElement).disabled).toBe(true)
    expect(onConfirm).not.toHaveBeenCalled()
  })
  it('preloads valid coordinates and allows confirming them', () => {
    const { onConfirm } = picker(7.29, 80.63)
    expect(screen.getByTestId('map').dataset.center).toBe('7.29,80.63')
    expect(screen.getByTestId('selected-marker').dataset.position).toBe('7.29,80.63')
    useLocation(); expect(onConfirm).toHaveBeenCalledWith(7.29, 80.63)
  })
  it.each([[91, 80], [7, 181], [NaN, 80], [null, 80]])('does not preselect invalid coordinates %s, %s', (lat, lng) => {
    picker(lat, lng)
    expect(screen.queryByTestId('selected-marker')).toBeNull()
    expect((screen.getByRole('button', { name: 'Use location' }) as HTMLButtonElement).disabled).toBe(true)
  })
  it('selects a clicked location locally and confirms full precision with a readable display', () => {
    const { onConfirm } = picker()
    clickMap()
    expect(onConfirm).not.toHaveBeenCalled()
    expect(screen.getByText('Latitude: 8.123457')).toBeTruthy()
    expect(screen.getByText('Longitude: 81.234568')).toBeTruthy()
    useLocation(); expect(onConfirm).toHaveBeenCalledWith(8.123456789, 81.234567891)
  })
  it('updates the location when the draggable marker moves', () => {
    const { onConfirm } = picker(7.29, 80.63)
    expect(screen.getByTestId('selected-marker').dataset.draggable).toBe('true')
    fireEvent.click(screen.getByRole('button', { name: 'Mock drag marker' }))
    expect(screen.getByText('Latitude: 6.250000')).toBeTruthy()
    expect(screen.getByText('Longitude: 79.750000')).toBeTruthy()
    useLocation(); expect(onConfirm).toHaveBeenCalledWith(6.25, 79.75)
  })
  it('cancels without confirming a draft', () => {
    const { onConfirm, onCancel } = picker(); clickMap()
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    expect(onCancel).toHaveBeenCalledOnce(); expect(onConfirm).not.toHaveBeenCalled()
  })
})

describe('picker in team forms', () => {
  it('preserves typed coordinates on cancel and restores button focus', () => {
    const { fetch } = mount(); const form = createForm()
    fireEvent.change(input(form, 'Base latitude'), { target: { value: '6.5' } })
    fireEvent.change(input(form, 'Base longitude'), { target: { value: '80.5' } })
    openPicker(form)
    expect(screen.getByTestId('selected-marker').dataset.position).toBe('6.5,80.5')
    clickMap(); fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    expect(input(form, 'Base latitude').value).toBe('6.5')
    expect(input(form, 'Base longitude').value).toBe('80.5')
    expect(document.activeElement).toBe(within(form).getByRole('button', { name: 'Pick on map' }))
    expect(fetch).not.toHaveBeenCalled()
  })
  it('creates with the picked coordinates only on Create and resets the controlled fields', async () => {
    const { fetch } = mount(); const form = createForm()
    fireEvent.change(input(form, 'Team name'), { target: { value: 'New team' } })
    openPicker(form); clickMap(); useLocation()
    expect(input(form, 'Base latitude').value).toBe('8.123456789')
    expect(screen.queryByRole('heading', { name: 'Select team base location' })).toBeNull()
    expect(fetch).not.toHaveBeenCalled()
    fireEvent.submit(form)
    await screen.findByText('Rescue team created.')
    expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ name: 'New team', baseLatitude: 8.123456789, baseLongitude: 81.234567891 })
    expect(input(form, 'Base latitude').value).toBe('')
    expect(input(form, 'Base longitude').value).toBe('')
    openPicker(form); expect(screen.queryByTestId('selected-marker')).toBeNull()
  })
  it('preloads edit coordinates, saves a changed map selection, and refreshes the team map', async () => {
    const { fetch, refresh, rerender } = mount([fixture]); const form = editForm()
    expect(input(form, 'Base latitude').value).toBe('7.29')
    openPicker(form)
    expect(screen.getByTestId('selected-marker').dataset.position).toBe('7.29,80.63')
    clickMap(); useLocation(); fireEvent.submit(form)
    await screen.findByText('Team updated.')
    const payload = JSON.parse(fetch.mock.calls[0][1].body)
    expect(payload).toEqual({ name: 'Kandy', status: 'Available', baseLatitude: 8.123456789, baseLongitude: 81.234567891 })
    expect(refresh).toHaveBeenCalledOnce()
    rerender(<ResourceManagementPanel teams={[{ ...fixture, ...payload }]} refresh={refresh} />)
    fireEvent.click(screen.getByRole('button', { name: 'View Kandy on map' }))
    expect(screen.getByTestId('team-marker').dataset.position).toBe('8.123456789,81.234567891')
    expect(map.setView).toHaveBeenLastCalledWith([8.123456789, 81.234567891], 12)
  })
  it.each(['create', 'edit'])('clears both %s fields, closes the picker, and saves null', async (mode) => {
    const { fetch } = mount(mode === 'edit' ? [fixture] : [])
    const form = mode === 'edit' ? editForm() : createForm()
    if (mode === 'create') fireEvent.change(input(form, 'Team name'), { target: { value: 'New team' } })
    openPicker(form); clickMap()
    fireEvent.click(within(form).getByRole('button', { name: 'Clear location' }))
    expect(input(form, 'Base latitude').value).toBe('')
    expect(input(form, 'Base longitude').value).toBe('')
    expect(screen.queryByRole('heading', { name: 'Select team base location' })).toBeNull()
    fireEvent.submit(form)
    await waitFor(() => expect(fetch).toHaveBeenCalledOnce())
    expect(JSON.parse(fetch.mock.calls[0][1].body)).toMatchObject({ baseLatitude: null, baseLongitude: null })
    await screen.findByText(mode === 'edit' ? 'Team updated.' : 'Rescue team created.')
  })
  it('exposes no team/member/vehicle details in the picker', () => {
    mount([fixture]); openPicker(editForm())
    const region = screen.getByRole('region', { name: 'Select team base location' })
    for (const value of ['Kandy', 'Private Person', '0712345678', 'PRIVATE-PLATE']) expect(region.textContent).not.toContain(value)
    for (const button of within(region).getAllByRole('button')) expect(button.getAttribute('type')).toBe('button')
  })
})

function feature(name = 'Kandy', lat = 7.29, lng = 80.63, countrycode = 'LK') {
  return { geometry: { coordinates: [lng, lat] }, properties: { name, state: 'Central Province', country: 'Sri Lanka', countrycode } }
}
function response(features = [feature()]) { return new Response(JSON.stringify({ features }), { status: 200 }) }
function searchFor(query = 'Kandy') {
  const field = screen.getByLabelText('Search location')
  fireEvent.change(field, { target: { value: query } })
  fireEvent.keyDown(field, { key: 'Enter' })
}
async function chooseKandy() { fireEvent.click(await screen.findByRole('button', { name: 'Kandy, Central Province, Sri Lanka' })) }

describe('picker location search', () => {
  it('debounces typing, ignores short queries, and sends only public Sri Lanka search parameters', async () => {
    vi.useFakeTimers()
    const fetch = vi.fn().mockResolvedValue(response()); vi.stubGlobal('fetch', fetch); picker()
    const field = screen.getByPlaceholderText('Search for a place in Sri Lanka')
    fireEvent.change(field, { target: { value: 'Ka' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(600) })
    expect(fetch).not.toHaveBeenCalled()
    fireEvent.change(field, { target: { value: 'Kan' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(300) })
    fireEvent.change(field, { target: { value: 'Kandy' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(499) })
    expect(fetch).not.toHaveBeenCalled()
    await act(async () => { await vi.advanceTimersByTimeAsync(1) })
    expect(fetch).toHaveBeenCalledOnce()
    const url = new URL(fetch.mock.calls[0][0])
    expect(url.hostname).toBe('photon.komoot.io')
    expect(url.searchParams.get('bbox')).toBe('79.5,5.8,82.0,9.9')
    expect(url.searchParams.get('limit')).toBe('5')
    expect(fetch.mock.calls[0][1].credentials).toBe('omit')
    expect(fetch.mock.calls[0][1].headers).toBeUndefined()
  })
  it('limits readable results to five and rejects invalid or foreign coordinates', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response([
      feature('Foreign', 7, 80, 'IN'), feature('Invalid', 100, 80), feature('Missing', NaN, 80),
      ...Array.from({ length: 8 }, (_, index) => feature(`Place ${index}`)),
    ])))
    picker(); searchFor()
    const list = await screen.findByRole('list', { name: 'Location search results' })
    expect(within(list).getAllByRole('button')).toHaveLength(5)
    expect(within(list).getByRole('button', { name: 'Place 0, Central Province, Sri Lanka' })).toBeTruthy()
    expect(list.textContent).not.toMatch(/Foreign|Invalid|Missing/)
  })
  it('places and recenters a searched marker without confirming, then uses the final selection', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response()))
    const { onConfirm } = picker(); searchFor(); await chooseKandy()
    expect(screen.getByTestId('selected-marker').dataset.position).toBe('7.29,80.63')
    expect(map.setView).toHaveBeenLastCalledWith([7.29, 80.63], 15)
    expect(screen.queryByRole('list', { name: 'Location search results' })).toBeNull()
    expect(onConfirm).not.toHaveBeenCalled()
    useLocation(); expect(onConfirm).toHaveBeenCalledWith(7.29, 80.63)
  })
  it('allows clicking and dragging after search without snapping back, and clear search keeps the marker', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response()))
    const { onConfirm } = picker(); searchFor(); await chooseKandy()
    map.setView.mockClear(); clickMap()
    expect(screen.getByTestId('selected-marker').dataset.position).toBe('8.123456789,81.234567891')
    fireEvent.click(screen.getByRole('button', { name: 'Mock drag marker' }))
    fireEvent.click(screen.getByRole('button', { name: 'Clear search' }))
    expect(screen.getByTestId('selected-marker').dataset.position).toBe('6.25,79.75')
    expect(map.setView).not.toHaveBeenCalled()
    useLocation(); expect(onConfirm).toHaveBeenCalledWith(6.25, 79.75)
  })
  it('cancels a searched edit without altering original form coordinates', async () => {
    const { fetch } = mount([fixture]); fetch.mockResolvedValue(response())
    const form = editForm(); openPicker(form); searchFor(); await chooseKandy()
    clickMap(); fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    expect(input(form, 'Base latitude').value).toBe('7.29')
    expect(input(form, 'Base longitude').value).toBe('80.63')
    expect(fetch).toHaveBeenCalledOnce()
    expect(fetch.mock.calls[0][0]).toContain('photon.komoot.io')
  })
  it('shows a friendly error and keeps manual selection usable', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('Provider raw private details')))
    const { onConfirm } = picker(); searchFor()
    expect(await screen.findByText('Unable to search locations. Try again.')).toBeTruthy()
    expect(screen.queryByText('Provider raw private details')).toBeNull()
    clickMap(); useLocation(); expect(onConfirm).toHaveBeenCalledWith(8.123456789, 81.234567891)
  })
  it('reports no matches and caches repeated searches while open', async () => {
    const fetch = vi.fn().mockResolvedValue(response([])); vi.stubGlobal('fetch', fetch)
    picker(); searchFor()
    expect(await screen.findByText('No locations found.')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Search' }))
    expect(await screen.findByText('No locations found.')).toBeTruthy()
    expect(fetch).toHaveBeenCalledOnce()
  })
  it('aborts stale requests and ignores late responses even when transport ignores abort', async () => {
    let resolveOld!: (value: Response) => void
    const fetch = vi.fn().mockImplementationOnce(() => new Promise<Response>((resolve) => { resolveOld = resolve }))
      .mockResolvedValueOnce(response([feature('Galle', 6.03, 80.22)]))
    vi.stubGlobal('fetch', fetch); picker(); searchFor('Kandy')
    expect(screen.getByText('Searching...')).toBeTruthy()
    searchFor('Galle')
    expect(await screen.findByRole('button', { name: 'Galle, Central Province, Sri Lanka' })).toBeTruthy()
    expect(fetch.mock.calls[0][1].signal.aborted).toBe(true)
    await act(async () => { resolveOld(response()); await Promise.resolve() })
    expect(screen.queryByRole('button', { name: 'Kandy, Central Province, Sri Lanka' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Galle, Central Province, Sri Lanka' })).toBeTruthy()
  })
})
