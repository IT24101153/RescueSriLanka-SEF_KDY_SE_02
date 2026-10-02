import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { act, cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import RescueCoordinatorDashboard from './RescueCoordinatorDashboard'
import * as api from './api'
import { ApiError } from '../../shared/api/client'
import type { Incident } from '../componentA/types'
import type { AssignmentDto, RescueTeamDto } from './types'

vi.mock('./api')
const incident: Incident = {
  id: 'd1000000-0000-4000-8000-000000000001', title: 'Flood near school', description: 'Road flooded',
  type: 'Flood', district: 'Kandy', addressText: 'School road', severity: 'High', status: 'Verified',
  isActive: true, latitude: 7.3, longitude: 80.6, affectedRadiusMeters: 1000, estimatedAffectedPeople: 4,
  aiSeverity: null, aiSeverityScore: null, aiConfidence: null, aiRationale: null, aiAnalysedAt: null,
  severityOverridden: false, reportedAt: '2026-09-01T00:00:00Z', resolvedAt: null, imageCount: 0, images: [], distanceKm: null,
}
const team: RescueTeamDto = {
  id: 'team', name: 'Kandy rescue', status: 'Available', baseLatitude: null, baseLongitude: null,
  members: [{ id: 'member', fullName: 'Medic', phone: '0712345678', skill: 'FirstAid', isAvailable: true }],
  vehicles: [{ id: 'vehicle', plateNumber: 'ABC-123', type: 'Ambulance', capacity: 4, status: 'Available' }],
}
const assignment: AssignmentDto = {
  id: 'assignment', incidentId: incident.id, helpRequestId: null, rescueTeamId: team.id, rescueTeamName: team.name,
  vehicleId: 'vehicle', vehiclePlateNumber: 'ABC-123', requiredSkill: 'FirstAid', requiredCapacity: 1,
  status: 'Proposed', planVersion: 1, assignedAt: '2026-09-01T00:00:00Z', notes: null, dispatchId: null,
}
beforeEach(() => {
  vi.resetAllMocks()
  vi.mocked(api.getActiveIncidents).mockResolvedValue([incident])
  vi.mocked(api.getIncident).mockResolvedValue(incident)
  vi.mocked(api.getRescueTeams).mockResolvedValue([team])
  vi.mocked(api.getAssignments).mockResolvedValue([])
  vi.mocked(api.getDispatches).mockResolvedValue([])
  vi.mocked(api.createAssignment).mockResolvedValue(assignment)
  vi.mocked(api.reviseAssignment).mockResolvedValue(assignment)
})
afterEach(() => { cleanup(); vi.restoreAllMocks() })

async function mount() {
  render(<RescueCoordinatorDashboard user={{ id: 'user', fullName: 'Coordinator', email: 'rescue@example.test', role: 'RescueTeam' }} onLogout={vi.fn()} />)
  const user = userEvent.setup()
  await user.click(within(screen.getByRole('navigation')).getByRole('button', { name: /Assignments/ }))
  return user
}
const selectIncident = () => screen.getByRole('combobox', { name: 'Incident' }) as HTMLSelectElement
const createButton = () => screen.getByRole('button', { name: 'Create proposal' }) as HTMLButtonElement
async function fillPlan(user: ReturnType<typeof userEvent.setup>) {
  await user.selectOptions(selectIncident(), incident.id)
  await user.selectOptions(screen.getByLabelText('Team'), team.id)
  await user.selectOptions(screen.getByLabelText('Vehicle'), 'vehicle')
}

it('loads active incidents, displays readable options and sends the selected real ID without generating a UUID', async () => {
  const random = vi.spyOn(crypto, 'randomUUID')
  const user = await mount()
  expect(api.getActiveIncidents).toHaveBeenCalledOnce()
  expect(selectIncident().value).toBe('')
  expect(createButton().disabled).toBe(true)
  expect(screen.getByRole('option', { name: 'Select an incident' })).toBeTruthy()
  const option = screen.getByRole('option', { name: 'Flood near school — Kandy — High / Verified' }) as HTMLOptionElement
  expect(option.value).toBe(incident.id)
  expect(option.textContent).not.toContain(incident.id)
  await fillPlan(user)
  await user.type(screen.getByLabelText('Notes'), 'Bring supplies')
  await user.click(createButton())
  expect(api.createAssignment).toHaveBeenCalledWith({ incidentId: incident.id, helpRequestId: null, rescueTeamId: team.id, vehicleId: 'vehicle', requiredSkill: 'FirstAid', requiredCapacity: 1, notes: 'Bring supplies' })
  expect(random).not.toHaveBeenCalled()
})

it('keeps reported and in-progress incidents eligible with visible status, and excludes inactive and closed incidents', async () => {
  vi.mocked(api.getActiveIncidents).mockResolvedValue([
    incident, { ...incident, id: 'reported', title: 'Unverified report', status: 'Reported', district: null },
    { ...incident, id: 'progress', status: 'InProgress' }, { ...incident, id: 'inactive', isActive: false },
    { ...incident, id: 'resolved', status: 'Resolved' }, { ...incident, id: 'rejected', status: 'Rejected' },
  ])
  await mount()
  expect(Array.from(selectIncident().options).map((item) => item.value)).toEqual(['', incident.id, 'reported', 'progress'])
  expect(screen.getByRole('option', { name: 'Unverified report — School road — High / Reported' })).toBeTruthy()
})

it('shows loading and prevents creation until the incident request completes', async () => {
  let finish!: (value: Incident[]) => void
  vi.mocked(api.getActiveIncidents).mockReturnValue(new Promise((resolve) => { finish = resolve }))
  await mount()
  expect(screen.getByText('Loading incidents...')).toBeTruthy()
  expect(selectIncident().disabled).toBe(true)
  expect(createButton().disabled).toBe(true)
  await act(async () => { finish([incident]) })
  expect(selectIncident().disabled).toBe(false)
})

it('shows the empty state and disables creation', async () => {
  vi.mocked(api.getActiveIncidents).mockResolvedValue([])
  await mount()
  expect(screen.getByText('No active incidents are currently available for assignment.')).toBeTruthy()
  expect(createButton().disabled).toBe(true)
})

it('shows a local load error and Retry restores the incident options', async () => {
  vi.mocked(api.getActiveIncidents).mockRejectedValueOnce(new Error('Offline'))
  const user = await mount()
  expect(screen.getByRole('alert').textContent).toContain('Unable to load incidents.')
  expect(createButton().disabled).toBe(true)
  await user.click(screen.getByRole('button', { name: 'Retry' }))
  expect(await screen.findByRole('option', { name: /Flood near school/ })).toBeTruthy()
  expect(api.getActiveIncidents).toHaveBeenCalledTimes(2)
})

it('refreshes incident options while retaining a valid selection and explicitly blocks a stale selection', async () => {
  const user = await mount()
  await user.selectOptions(selectIncident(), incident.id)
  await user.click(screen.getByRole('button', { name: 'Refresh' }))
  expect(selectIncident().value).toBe(incident.id)
  vi.mocked(api.getActiveIncidents).mockResolvedValue([{ ...incident, id: 'another' }])
  await user.click(screen.getByRole('button', { name: 'Refresh' }))
  expect(selectIncident().value).toBe(incident.id)
  expect(screen.getByRole('alert').textContent).toContain('no longer eligible')
  expect(createButton().disabled).toBe(true)
})

it('surfaces the backend incident validation error without changing selection or retrying creation', async () => {
  vi.mocked(api.createAssignment).mockRejectedValue(new ApiError(400, 'Selected incident is no longer active. Refresh incidents.'))
  const user = await mount()
  await fillPlan(user)
  await user.click(createButton())
  expect(screen.getByRole('alert').textContent).toContain('Selected incident is no longer active.')
  expect(selectIncident().value).toBe(incident.id)
  expect(api.createAssignment).toHaveBeenCalledOnce()
})

it('locks the original incident on revision, loads details outside the active list, and never sends a replacement incident ID', async () => {
  vi.mocked(api.getActiveIncidents).mockResolvedValue([])
  vi.mocked(api.getAssignments).mockResolvedValue([assignment])
  const user = await mount()
  await user.click(screen.getByText('assignment'))
  expect(screen.queryByRole('combobox', { name: 'Incident' })).toBeNull()
  expect(await screen.findByText('Flood near school — Kandy — High / Verified')).toBeTruthy()
  expect(api.getIncident).toHaveBeenCalledWith(incident.id, expect.any(AbortSignal))
  expect(screen.getByText(incident.id)).toBeTruthy()
  expect(screen.getByText('Incident is preserved from the selected assignment.')).toBeTruthy()
  await user.click(screen.getByRole('button', { name: 'Save revision' }))
  expect(api.reviseAssignment).toHaveBeenCalledWith('assignment', { rescueTeamId: team.id, vehicleId: 'vehicle', requiredSkill: 'FirstAid', requiredCapacity: 1, notes: '' })
  expect(api.createAssignment).not.toHaveBeenCalled()
  await user.click(screen.getByRole('button', { name: 'Cancel editing / New assignment' }))
  expect(selectIncident().value).toBe('')
})

it.each([
  ['capacity', 'exceeds the selected vehicle capacity'],
  ['skill', 'no available member with Paramedic'],
  ['team', 'Selected rescue team must be available'],
  ['vehicle', 'Selected vehicle must be available'],
])('preserves %s validation after incident selection', async (kind, message) => {
  if (kind === 'team') vi.mocked(api.getRescueTeams).mockResolvedValue([{ ...team, status: 'OnMission' }])
  if (kind === 'vehicle') vi.mocked(api.getRescueTeams).mockResolvedValue([{ ...team, vehicles: [{ ...team.vehicles[0], status: 'InUse' }] }])
  const user = await mount()
  await fillPlan(user)
  if (kind === 'capacity') { await user.clear(screen.getByLabelText('People/patients requiring transport')); await user.type(screen.getByLabelText('People/patients requiring transport'), '5') }
  if (kind === 'skill') await user.selectOptions(screen.getByLabelText('Required skill'), 'Paramedic')
  await user.click(createButton())
  expect(screen.getByRole('alert').textContent).toContain(message)
  expect(api.createAssignment).not.toHaveBeenCalled()
})

it('keeps vehicle options scoped to the selected team', async () => {
  vi.mocked(api.getRescueTeams).mockResolvedValue([team, { ...team, id: 'other', name: 'Other team', vehicles: [{ ...team.vehicles[0], id: 'other-vehicle', plateNumber: 'XYZ-789' }] }])
  const user = await mount()
  await fillPlan(user)
  expect(screen.queryByRole('option', { name: /XYZ-789/ })).toBeNull()
  await user.selectOptions(screen.getByLabelText('Team'), 'other')
  expect((screen.getByLabelText('Vehicle') as HTMLSelectElement).value).toBe('')
  expect(screen.queryByRole('option', { name: /ABC-123/ })).toBeNull()
  expect(screen.getByRole('option', { name: /XYZ-789/ })).toBeTruthy()
})

it.each([401, 403])('preserves authorization failure handling for HTTP %s', async (status) => {
  vi.mocked(api.getActiveIncidents).mockRejectedValue(new ApiError(status, 'Denied'))
  await mount()
  await waitFor(() => expect(screen.getByRole('alert').textContent).toContain(status === 401 ? 'session has expired' : 'do not have permission'))
  expect(createButton().disabled).toBe(true)
  expect(api.createAssignment).not.toHaveBeenCalled()
})
