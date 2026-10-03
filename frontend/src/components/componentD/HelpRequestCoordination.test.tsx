import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import type { ReactNode } from 'react'
import HelpRequestCoordinationPanel from './HelpRequestCoordinationPanel'
import HelpRequestResponseMap from './HelpRequestResponseMap'
import RescueCoordinatorDashboard from './RescueCoordinatorDashboard'
import * as api from './api'
import type { AssignmentDto, RescueCandidate, RescueHelpRequest, RescueRecommendation } from './types'

vi.mock('./api')
const map = vi.hoisted(() => ({ fitBounds: vi.fn() }))
vi.mock('react-leaflet', () => ({
  MapContainer: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  TileLayer: () => null,
  CircleMarker: ({ children, center, pathOptions }: { children: ReactNode; center: number[]; pathOptions: object }) => <div data-testid="marker" data-center={center.join(',')} data-style={JSON.stringify(pathOptions)}>{children}</div>,
  Popup: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  useMap: () => map,
}))
const request: RescueHelpRequest = { id: 'help-1', type: 'Rescue', description: 'Flood evacuation', latitude: 7, longitude: 80, urgencyScore: 5, status: 'Pending', verificationStatus: 'Verified', createdAt: '2026-09-01T00:00:00Z' }
const nearest: RescueCandidate = { teamId: 'near', teamName: 'Near team', vehicleId: 'v1', vehicleType: 'Boat', distanceKm: 2, matchingSkill: 'WaterRescue', vehicleCapacity: 4, teamAvailability: 'Available', vehicleAvailability: 'Available', baseLatitude: 7.01, baseLongitude: 80 }
const other: RescueCandidate = { ...nearest, teamId: 'other', teamName: 'Other team', vehicleId: 'v2', distanceKm: 4, baseLatitude: 7.02 }
const recommendation: RescueRecommendation = { candidates: [nearest, other], recommendedCandidate: nearest, aiAvailable: false, explanation: 'AI unavailable. Backend checked candidates remain available.' }
const assignment: AssignmentDto = { id: 'plan', incidentId: null, helpRequestId: request.id, rescueTeamId: other.teamId, rescueTeamName: other.teamName, vehicleId: other.vehicleId, vehiclePlateNumber: 'Boat 2', requiredSkill: 'WaterRescue', requiredCapacity: 3, planVersion: 1, status: 'Proposed', assignedAt: request.createdAt, notes: 'Transport confirmed', dispatchId: null }
beforeEach(() => {
  vi.resetAllMocks()
  vi.mocked(api.getRescueHelpRequests).mockResolvedValue([request])
  vi.mocked(api.recommendRescueTeam).mockResolvedValue(recommendation)
  vi.mocked(api.createAssignment).mockResolvedValue(assignment)
})
afterEach(cleanup)
async function selectRequest() { fireEvent.click(await screen.findByRole('button', { name: /Flood evacuation/ })) }
function confirmDemand() {
  fireEvent.change(screen.getByLabelText('Required skill'), { target: { value: 'WaterRescue' } })
  fireEvent.change(screen.getByLabelText('People/patients requiring transport'), { target: { value: '3' } })
  const confirmation = screen.getByLabelText('I confirm the number requiring transport') as HTMLInputElement
  if (!confirmation.checked) fireEvent.click(confirmation)
}
async function recommend() {
  confirmDemand(); fireEvent.click(screen.getByRole('button', { name: 'Recommend rescue team' }))
  await screen.findByRole('button', { name: 'Use recommended team' })
}
function panel() {
  const onCreated = vi.fn().mockResolvedValue(undefined)
  return { ...render(<HelpRequestCoordinationPanel refreshVersion={0} onCreated={onCreated} />), onCreated }
}

it('shows the citizen name and phone number in the queue card and the response plan, falling back when absent', async () => {
  vi.mocked(api.getRescueHelpRequests).mockResolvedValue([
    { ...request, citizenName: 'Nimal Perera', citizenPhoneNumber: '0771234567' },
  ])
  panel()
  await screen.findByText('Nimal Perera · 0771234567')
  await selectRequest()
  expect(screen.getByText('Reported by: Nimal Perera · 0771234567')).toBeTruthy()
  cleanup()

  vi.mocked(api.getRescueHelpRequests).mockResolvedValue([request])
  panel()
  await screen.findByText('Citizen name unavailable')
  await selectRequest()
  expect(screen.getByText('Reported by: Unknown citizen')).toBeTruthy()
})

it('requires explicit skill and transport demand without mapping the request type or defaulting to one', async () => {
  panel(); await selectRequest()
  expect(screen.getByText('Approximate people affected: Not specified')).toBeTruthy()
  expect((screen.getByLabelText('Required skill') as HTMLSelectElement).value).toBe('')
  expect((screen.getByLabelText('People/patients requiring transport') as HTMLInputElement).value).toBe('')
  expect((screen.getByRole('button', { name: 'Recommend rescue team' }) as HTMLButtonElement).disabled).toBe(true)
  for (const value of ['0', '-1', '1.5', '2147483648']) {
    confirmDemand(); fireEvent.change(screen.getByLabelText('People/patients requiring transport'), { target: { value } })
    expect((screen.getByRole('button', { name: 'Recommend rescue team' }) as HTMLButtonElement).disabled).toBe(true)
  }
  expect(api.recommendRescueTeam).not.toHaveBeenCalled(); expect(api.createAssignment).not.toHaveBeenCalled()
})

it('prefills affected people but requires confirmation, keeps edits local, and recommends with confirmed demand', async () => {
  const counted = { ...request, estimatedPeopleCount: 12 }
  vi.mocked(api.getRescueHelpRequests).mockResolvedValue([counted])
  panel(); await selectRequest()
  expect(screen.getByText('Approximate people affected: 12')).toBeTruthy()
  const transport = screen.getByLabelText('People/patients requiring transport') as HTMLInputElement
  expect(transport.value).toBe('12')
  fireEvent.change(screen.getByLabelText('Required skill'), { target: { value: 'WaterRescue' } })
  expect((screen.getByRole('button', { name: 'Recommend rescue team' }) as HTMLButtonElement).disabled).toBe(true)
  await recommend()
  expect(transport.value).toBe('3')
  expect(counted.estimatedPeopleCount).toBe(12)
  expect(screen.getByText('Approximate people affected: 12')).toBeTruthy()
  expect(api.recommendRescueTeam).toHaveBeenCalledExactlyOnceWith(request.id, 'WaterRescue', 3)
  fireEvent.change(transport, { target: { value: '2' } })
  expect((screen.getByLabelText('I confirm the number requiring transport') as HTMLInputElement).checked).toBe(false)
  expect((screen.getByRole('button', { name: 'Recommend rescue team' }) as HTMLButtonElement).disabled).toBe(true)
  expect(api.createAssignment).not.toHaveBeenCalled()
})

it('recommends without mutation, supports a different eligible pair, and creates only on final submission', async () => {
  const { onCreated } = panel(); await selectRequest(); await recommend()
  expect(api.recommendRescueTeam).toHaveBeenCalledWith(request.id, 'WaterRescue', 3)
  expect(screen.getByText('Deterministic recommendation')).toBeTruthy()
  fireEvent.click(screen.getByRole('button', { name: 'Use recommended team' }))
  expect((screen.getByLabelText('Eligible team and vehicle') as HTMLSelectElement).value).toBe('near/v1')
  fireEvent.click(screen.getByRole('button', { name: 'Choose another eligible team' }))
  expect(document.activeElement).toBe(screen.getByLabelText('Eligible team and vehicle'))
  fireEvent.change(screen.getByLabelText('Eligible team and vehicle'), { target: { value: 'other/v2' } })
  expect(api.createAssignment).not.toHaveBeenCalled()
  fireEvent.change(screen.getByLabelText('Notes'), { target: { value: 'Transport confirmed' } })
  fireEvent.click(screen.getByRole('button', { name: 'Create response plan' }))
  await waitFor(() => expect(onCreated).toHaveBeenCalledWith(assignment))
  expect(api.createAssignment).toHaveBeenCalledExactlyOnceWith({ helpRequestId: request.id, incidentId: null, rescueTeamId: 'other', vehicleId: 'v2', requiredSkill: 'WaterRescue', requiredCapacity: 3, notes: 'Transport confirmed' })
})

it('invalidates candidates when transport demand changes and ignores a late recommendation', async () => {
  let resolve!: (value: RescueRecommendation) => void
  vi.mocked(api.recommendRescueTeam).mockReturnValueOnce(new Promise((done) => { resolve = done }))
  panel(); await selectRequest(); confirmDemand()
  fireEvent.click(screen.getByRole('button', { name: 'Recommend rescue team' }))
  fireEvent.change(screen.getByLabelText('People/patients requiring transport'), { target: { value: '4' } })
  await act(async () => resolve(recommendation))
  expect(screen.queryByRole('button', { name: 'Use recommended team' })).toBeNull()
  expect((screen.getByRole('button', { name: 'Create response plan' }) as HTMLButtonElement).disabled).toBe(true)
})

it('shows queue errors with retry and supports an empty queue', async () => {
  vi.mocked(api.getRescueHelpRequests).mockRejectedValueOnce(new Error('offline')).mockResolvedValueOnce([])
  panel(); fireEvent.click(await screen.findByRole('button', { name: 'Retry Help Requests' }))
  expect(await screen.findByText('No eligible Help Requests are currently available.')).toBeTruthy()
})

it('refresh removes old selections when a request is no longer eligible', async () => {
  const { rerender, onCreated } = panel(); await selectRequest(); await recommend()
  vi.mocked(api.getRescueHelpRequests).mockResolvedValue([])
  rerender(<HelpRequestCoordinationPanel refreshVersion={1} onCreated={onCreated} />)
  await screen.findByText('No eligible Help Requests are currently available.')
  expect(screen.queryByRole('button', { name: 'Create response plan' })).toBeNull()
})

it('prevents duplicate submission while creating and requires fresh candidates after a rejected create', async () => {
  let reject!: (reason: Error) => void
  vi.mocked(api.createAssignment).mockReturnValueOnce(new Promise((_, fail) => { reject = fail }))
  panel(); await selectRequest(); await recommend()
  fireEvent.click(screen.getByRole('button', { name: 'Use recommended team' }))
  const form = screen.getByRole('button', { name: 'Create response plan' }).closest('form')!
  fireEvent.submit(form); fireEvent.submit(form)
  expect(api.createAssignment).toHaveBeenCalledOnce()
  await act(async () => reject(new Error('stale')))
  expect(screen.getByRole('alert')).toBeTruthy()
  expect(screen.queryByLabelText('Eligible team and vehicle')).toBeNull()
})

it('handles no candidates without permitting creation', async () => {
  vi.mocked(api.recommendRescueTeam).mockResolvedValue({ candidates: [], recommendedCandidate: null, aiAvailable: false, explanation: 'No eligible team and vehicle.' })
  panel(); await selectRequest(); confirmDemand()
  fireEvent.click(screen.getByRole('button', { name: 'Recommend rescue team' }))
  await screen.findByText('No eligible team and vehicle.')
  expect(screen.queryByLabelText('Eligible team and vehicle')).toBeNull()
  expect((screen.getByRole('button', { name: 'Create response plan' }) as HTMLButtonElement).disabled).toBe(true)
})

it.each([null, 91, NaN, Infinity])('handles invalid request latitude %s without map or recommendation', async (latitude) => {
  vi.mocked(api.getRescueHelpRequests).mockResolvedValue([{ ...request, latitude }])
  panel(); await selectRequest(); confirmDemand()
  expect(screen.getByRole('alert').textContent).toContain('location is missing or invalid')
  expect(screen.queryAllByTestId('marker')).toHaveLength(0)
  expect((screen.getByRole('button', { name: 'Recommend rescue team' }) as HTMLButtonElement).disabled).toBe(true)
})

it('plots request and unique bases, highlights recommended and selected teams, and labels distance correctly', () => {
  render(<HelpRequestResponseMap request={request} candidates={[nearest, { ...nearest, vehicleId: 'another-boat' }, other]} recommendedTeamId="near" selectedTeamId="other" />)
  const markers = screen.getAllByTestId('marker')
  expect(markers.map((m) => m.dataset.center)).toEqual(['7,80', '7.01,80', '7.02,80'])
  expect(markers[1].dataset.style).toContain('#7c3aed'); expect(markers[2].dataset.style).toContain('#15803d')
  expect(screen.getByText('Team markers show registered bases, not live positions.')).toBeTruthy()
  expect(markers[1].textContent).toContain('Straight-line distance: 2.00 km')
})

it('opens existing safety review after the Rescue Coordinator creates a Help Request response plan', async () => {
  vi.mocked(api.getAssignments).mockResolvedValue([])
  vi.mocked(api.getDispatches).mockResolvedValue([])
  vi.mocked(api.getRescueTeams).mockResolvedValue([])
  vi.mocked(api.getActiveIncidents).mockResolvedValue([])
  vi.mocked(api.createAssignment).mockImplementation(async () => {
    vi.mocked(api.getAssignments).mockResolvedValue([assignment]); return assignment
  })
  render(<RescueCoordinatorDashboard user={{ id: 'user', fullName: 'Operator', email: 'operator@example.test', role: 'RescueTeam' }} onLogout={vi.fn()} />)
  await waitFor(() => expect((screen.getByRole('button', { name: 'Refresh' }) as HTMLButtonElement).disabled).toBe(false))
  fireEvent.click(screen.getByRole('button', { name: /Assignments.*Plan response work/ }))
  fireEvent.click(screen.getByRole('button', { name: 'Help Request responses' }))
  await selectRequest(); await recommend()
  fireEvent.click(screen.getByRole('button', { name: 'Use recommended team' }))
  fireEvent.click(screen.getByRole('button', { name: 'Create response plan' }))
  expect(await screen.findByRole('button', { name: 'Run AI Safety Validation' })).toBeTruthy()
  expect(screen.queryByRole('button', { name: 'Approve & Dispatch' })).toBeNull()
})
