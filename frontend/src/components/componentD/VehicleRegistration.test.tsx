import { afterEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, within, waitFor } from '@testing-library/react'
import ResourceManagementPanel from './ResourceManagementPanel'
import { storeSession, clearSession } from '../../shared/auth/session'

afterEach(() => { cleanup(); vi.unstubAllGlobals(); clearSession() })
function mount() {
  storeSession({ token: 'test-only', expiresAt: '2099-01-01T00:00:00Z', user: { id: 'test', fullName: 'Test', email: 'test@example.test', role: 'RescueTeam' } })
  render(<ResourceManagementPanel teams={[{ id: 'team', name: 'Test team', status: 'Available', baseLatitude: null, baseLongitude: null, members: [], vehicles: [] }]} refresh={vi.fn()} />)
}
it('explains individual registrations and labels carrying capacity', () => {
  mount()
  expect(screen.getByText('Register each physical vehicle separately. Each vehicle must have a unique registration number.')).toBeTruthy()
  expect(screen.getByLabelText('People/Patients Capacity')).toBeTruthy()
})
it('shows a duplicate registration conflict and preserves the entered values', async () => {
  const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ error: 'A vehicle with this registration number already exists.' }), { status: 409 }))
  vi.stubGlobal('fetch', fetch)
  mount()
  const plate = screen.getByPlaceholderText('Registration number') as HTMLInputElement
  fireEvent.change(plate, { target: { value: 'WP CAB-1234' } })
  fireEvent.change(screen.getByLabelText('People/Patients Capacity'), { target: { value: '4' } })
  fireEvent.submit(plate.closest('form')!)
  expect(await screen.findByText('A vehicle with this registration number already exists.')).toBeTruthy()
  expect(plate.value).toBe('WP CAB-1234')
  expect(JSON.parse(fetch.mock.calls[0][1].body)).toMatchObject({ plateNumber: 'WP CAB-1234', capacity: 4 })
})

function mountVehicle(refresh = vi.fn().mockResolvedValue(undefined)) {
  render(<ResourceManagementPanel teams={[{ id: 'team', name: 'Test team', status: 'Available', baseLatitude: null, baseLongitude: null, members: [], vehicles: [{ id: 'vehicle', plateNumber: 'KK 0999', type: 'Ambulance', status: 'Available', capacity: 1 }] }]} refresh={refresh} />)
  const details = screen.getByText('KK 0999').closest('details')!
  details.open = true
  return { details, capacity: within(details).getByLabelText('People/Patients Capacity') as HTMLInputElement, refresh }
}
it('saves capacity with unchanged plate and updates the summary from the API response', async () => {
  const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ id: 'vehicle', plateNumber: 'KK 0999', type: 'Ambulance', status: 'Available', capacity: 4 }), { status: 200 }))
  vi.stubGlobal('fetch', fetch)
  const { details, capacity, refresh } = mountVehicle()
  fireEvent.change(capacity, { target: { value: '4' } })
  fireEvent.click(within(details).getByRole('button', { name: 'Save' }))
  expect(await within(details).findByText('Vehicle updated.')).toBeTruthy()
  expect(details.querySelector('summary')!.textContent).toContain('carries 4 people')
  expect(fetch.mock.calls[0][0]).toContain('/api/rescueteams/team/vehicles/vehicle')
  expect(fetch.mock.calls[0][1].method).toBe('PUT')
  expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ plateNumber: 'KK 0999', type: 'Ambulance', status: 'Available', capacity: 4 })
  expect(refresh).toHaveBeenCalledOnce()
})
it.each(['', '0', '101', '1.5'])('blocks invalid capacity %s before sending', (value) => {
  const fetch = vi.fn(); vi.stubGlobal('fetch', fetch)
  const { details, capacity } = mountVehicle()
  fireEvent.change(capacity, { target: { value } })
  fireEvent.submit(capacity.closest('form')!)
  expect(within(details).getByRole('alert').textContent).toBe('Capacity must be a whole number between 1 and 100.')
  expect(fetch).not.toHaveBeenCalled()
})
it.each([409, 500])('shows the real HTTP %s error beside Save and preserves the draft', async (status) => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ error: 'Vehicle update rejected.' }), { status })))
  const { details, capacity, refresh } = mountVehicle()
  fireEvent.change(capacity, { target: { value: '4' } })
  fireEvent.click(within(details).getByRole('button', { name: 'Save' }))
  expect(await within(details).findByText('Vehicle update rejected.')).toBeTruthy()
  expect(capacity.value).toBe('4')
  expect(details.querySelector('summary')!.textContent).toContain('carries 1 person')
  expect(refresh).not.toHaveBeenCalled()
})
it('keeps the saved summary and reports a refresh failure', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ id: 'vehicle', plateNumber: 'KK 0999', type: 'Ambulance', status: 'Available', capacity: 4 }), { status: 200 })))
  const { details, capacity } = mountVehicle(vi.fn().mockRejectedValue(new Error('Offline')))
  fireEvent.change(capacity, { target: { value: '4' } })
  fireEvent.submit(capacity.closest('form')!)
  await waitFor(() => expect(within(details).getByRole('alert').textContent).toContain('refresh failed: Offline'))
  expect(details.querySelector('summary')!.textContent).toContain('carries 4 people')
})
