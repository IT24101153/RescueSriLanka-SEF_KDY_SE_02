import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import type { ReactNode, Ref } from 'react'
import TeamLocationsMap from './TeamLocationsMap'
import ResourceManagementPanel from './ResourceManagementPanel'
import type { RescueTeamDto } from './types'

const leaflet = vi.hoisted(() => ({ setView: vi.fn(), fitBounds: vi.fn(), openPopup: vi.fn() }))
vi.mock('react-leaflet', async () => {
  const { useImperativeHandle } = await import('react')
  return {
    MapContainer: ({ children }: { children: ReactNode }) => <div data-testid="map">{children}</div>,
    TileLayer: () => null,
    Popup: ({ children }: { children: ReactNode }) => <div data-testid="popup">{children}</div>,
    CircleMarker: ({ children, center, ref }: { children: ReactNode; center: number[]; ref: Ref<unknown> }) => {
      useImperativeHandle(ref, () => ({ openPopup: leaflet.openPopup }))
      return <div data-testid="marker" data-center={center.join(',')}>{children}</div>
    },
    useMap: () => leaflet,
  }
})

beforeEach(() => vi.clearAllMocks())
afterEach(() => { cleanup(); vi.unstubAllGlobals() })

function team(id = '1', overrides: Partial<RescueTeamDto> = {}): RescueTeamDto {
  return { id, name: `Team ${id}`, status: 'Available', baseLatitude: 7.29, baseLongitude: 80.63,
    members: [{ id: 'member', fullName: 'Private Person', phone: '0712345678', skill: 'FirstAid', isAvailable: true }],
    vehicles: [{ id: 'vehicle', plateNumber: 'SECRET-PLATE', type: 'Ambulance', status: 'Available', capacity: 4 }], ...overrides }
}
function panel(teams: RescueTeamDto[] = []) {
  return render(<ResourceManagementPanel teams={teams} refresh={vi.fn().mockResolvedValue(undefined)} />)
}
function showMap() { fireEvent.click(screen.getByRole('button', { name: 'Show map' })) }
function markers() { return screen.queryAllByTestId('marker') }
function createForm() { return screen.getByRole('button', { name: 'Create Team' }).closest('form')! }
function editForm() {
  const details = screen.getByText('Edit Team').closest('details')!
  details.open = true
  return details.querySelector('form')!
}
function coordinates(form: HTMLFormElement, latitude: string, longitude: string) {
  fireEvent.change(within(form).getByLabelText('Base latitude'), { target: { value: latitude } })
  fireEvent.change(within(form).getByLabelText('Base longitude'), { target: { value: longitude } })
}
function mockFetch() {
  const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify(team()), { status: 200 }))
  vi.stubGlobal('fetch', fetch)
  return fetch
}

describe('team base map', () => {
  it('plots valid coordinates, including zero and boundary values', () => {
    render(<TeamLocationsMap teams={[team(), team('2', { baseLatitude: 0, baseLongitude: 0 }), team('3', { baseLatitude: -90, baseLongitude: 180 })]} />)
    expect(markers().map((marker) => marker.dataset.center)).toEqual(['7.29,80.63', '0,0', '-90,180'])
  })
  it.each([
    [null, null], [null, 80], [7, null], [91, 80], [-91, 80], [7, 181], [7, -181], [NaN, 80], [7, Infinity],
  ])('omits unusable coordinates %s, %s', (baseLatitude, baseLongitude) => {
    render(<TeamLocationsMap teams={[team('bad', { baseLatitude, baseLongitude })]} />)
    expect(markers()).toHaveLength(0)
    expect(screen.getByText('No team base locations are available for the current filters.')).toBeTruthy()
  })
  it('reports partial locations and textual status labels', () => {
    render(<TeamLocationsMap teams={[team(), team('2', { baseLatitude: null, baseLongitude: null })]} />)
    expect(screen.getByRole('status').textContent).toBe('1 team shown · 1 team has no base location')
    const legend = within(screen.getByRole('list', { name: 'Team status legend' }))
    for (const label of ['Available', 'On mission', 'Off duty']) expect(legend.getByText(label)).toBeTruthy()
  })
  it('only renders permitted summary fields in the popup', () => {
    render(<TeamLocationsMap teams={[team()]} />)
    const popup = screen.getByTestId('popup')
    expect(popup.textContent).toContain('Team 1Available1 members · 1 vehiclesBase location7.29000, 80.63000')
    for (const sensitive of ['Private Person', '0712345678', 'SECRET-PLATE', 'assignment', 'dispatch']) expect(popup.textContent).not.toContain(sensitive)
  })
  it('centers a single team and fits multiple teams only initially or on request', () => {
    const { rerender } = render(<TeamLocationsMap teams={[team()]} />)
    expect(leaflet.setView).toHaveBeenLastCalledWith([7.29, 80.63], 12)
    leaflet.setView.mockClear()
    rerender(<TeamLocationsMap teams={[team(), team('2', { baseLatitude: 8 })]} />)
    expect(leaflet.setView).not.toHaveBeenCalled()
    expect(leaflet.fitBounds).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Fit teams' }))
    expect(leaflet.fitBounds).toHaveBeenCalledWith([[7.29, 80.63], [8, 80.63]], { padding: [36, 36], maxZoom: 12 })
  })
  it('fits multiple teams at first render', () => {
    render(<TeamLocationsMap teams={[team(), team('2')]} />)
    expect(leaflet.fitBounds).toHaveBeenCalledOnce()
  })
})

describe('team map integration', () => {
  it('starts collapsed and supports show/hide', () => {
    panel([team()])
    expect(screen.getByRole('button', { name: 'Show map' }).getAttribute('aria-expanded')).toBe('false')
    expect(markers()).toHaveLength(0)
    showMap(); expect(markers()).toHaveLength(1)
    fireEvent.click(screen.getByRole('button', { name: 'Hide map' })); expect(markers()).toHaveLength(0)
  })
  it('uses all matching teams across pagination and respects search, status and sort', () => {
    panel(Array.from({ length: 7 }, (_, index) => team(String(index), { status: index === 6 ? 'OffDuty' : 'Available' })))
    showMap()
    expect(screen.getAllByRole('article')).toHaveLength(5)
    expect(markers()).toHaveLength(7)
    fireEvent.click(screen.getByRole('button', { name: 'Next' }))
    expect(screen.getAllByRole('article')).toHaveLength(2)
    expect(markers()).toHaveLength(7)
    fireEvent.change(screen.getByLabelText('Team Status'), { target: { value: 'Available' } })
    expect(markers()).toHaveLength(6)
    fireEvent.change(screen.getByLabelText('Sort'), { target: { value: 'desc' } })
    expect(markers()[0].textContent).toContain('Team 5')
    fireEvent.change(screen.getByLabelText('Search teams, members or vehicles'), { target: { value: 'Team 4' } })
    expect(markers()).toHaveLength(1)
    expect(markers()[0].textContent).toContain('Team 4')
    fireEvent.change(screen.getByLabelText('Search teams, members or vehicles'), { target: { value: 'missing' } })
    expect(screen.getByText('No team base locations are available for the current filters.')).toBeTruthy()
  })
  it('offers map actions only for located teams and focuses/reopens their popup', () => {
    panel([team(), team('2', { baseLatitude: null }), team('3', { baseLatitude: 91 })])
    expect(screen.queryByRole('button', { name: 'View Team 2 on map' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'View Team 3 on map' })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'View Team 1 on map' }))
    expect(screen.getByRole('button', { name: 'Hide map' })).toBeTruthy()
    expect(leaflet.setView).toHaveBeenLastCalledWith([7.29, 80.63], 12)
    expect(leaflet.openPopup).toHaveBeenCalledOnce()
    fireEvent.click(screen.getByRole('button', { name: 'View Team 1 on map' }))
    expect(leaflet.openPopup).toHaveBeenCalledTimes(2)
  })
})

describe('optional base coordinate inputs', () => {
  it.each([['7.29', '80.63', 7.29, 80.63], ['', '', null, null], ['0', '0', 0, 0]])('creates a team with coordinates %s, %s', async (latitude, longitude, baseLatitude, baseLongitude) => {
    const fetch = mockFetch(); panel()
    const form = createForm()
    fireEvent.change(within(form).getByLabelText('Team name'), { target: { value: 'Kandy' } })
    coordinates(form, latitude as string, longitude as string)
    fireEvent.submit(form)
    await waitFor(() => expect(fetch).toHaveBeenCalledOnce())
    expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ name: 'Kandy', baseLatitude, baseLongitude })
    expect(fetch.mock.calls[0][1].method).toBe('POST')
    await screen.findByText('Rescue team created.')
  })
  it('prepopulates edit coordinates and sends updated values through the existing helper', async () => {
    const fetch = mockFetch(); panel([team()])
    const form = editForm()
    expect((within(form).getByLabelText('Base latitude') as HTMLInputElement).value).toBe('7.29')
    expect((within(form).getByLabelText('Base longitude') as HTMLInputElement).value).toBe('80.63')
    coordinates(form, '8.1', '81.2'); fireEvent.submit(form)
    await waitFor(() => expect(fetch).toHaveBeenCalledOnce())
    expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ name: 'Team 1', status: 'Available', baseLatitude: 8.1, baseLongitude: 81.2 })
    expect(fetch.mock.calls[0][1].method).toBe('PUT')
    await screen.findByText('Team updated.')
  })
  it('clears existing coordinates with explicit null values', async () => {
    const fetch = mockFetch(); panel([team()])
    const form = editForm(); coordinates(form, '', ''); fireEvent.submit(form)
    await waitFor(() => expect(fetch).toHaveBeenCalledOnce())
    expect(JSON.parse(fetch.mock.calls[0][1].body)).toMatchObject({ baseLatitude: null, baseLongitude: null })
    await screen.findByText('Team updated.')
  })
  describe.each(['create', 'edit'])('%s validation', (mode) => {
    it.each([
      ['7', '', 'Supply both'], ['', '80', 'Supply both'], ['91', '80', 'Base latitude'],
      ['-91', '80', 'Base latitude'], ['7', '181', 'Base longitude'], ['7', '-181', 'Base longitude'],
      ['NaN', '80', 'Base latitude'], ['7', 'Infinity', 'Base longitude'], ['abc', '80', 'Base latitude'],
    ])('rejects %s, %s without an API request', (latitude, longitude, message) => {
      const fetch = mockFetch(); panel(mode === 'edit' ? [team()] : [])
      const form = mode === 'edit' ? editForm() : createForm()
      if (mode === 'create') fireEvent.change(within(form).getByLabelText('Team name'), { target: { value: 'Kandy' } })
      coordinates(form, latitude, longitude); fireEvent.submit(form)
      expect(screen.getByRole('alert').textContent).toContain(message)
      expect(fetch).not.toHaveBeenCalled()
    })
  })
})
