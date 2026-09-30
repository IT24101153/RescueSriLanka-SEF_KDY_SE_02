import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { cleanup, render, screen, within, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import RescueCoordinatorDashboard from './RescueCoordinatorDashboard'
import * as api from './api'
import type { AssignmentDto, DispatchDto } from './types'

vi.mock('./api', () => ({ getAssignments: vi.fn(), getDispatches: vi.fn(), getRescueTeams: vi.fn(), transitionDispatch: vi.fn() }))
afterEach(cleanup)
const assignment = (id: string, status: AssignmentDto['status'] = 'Approved'): AssignmentDto => ({
  id, status, incidentId: null, helpRequestId: null, rescueTeamId: 'team', rescueTeamName: 'Kandy', vehicleId: 'vehicle', vehiclePlateNumber: 'PJ 2343', requiredSkill: 'FirstAid', requiredCapacity: 1, planVersion: 1, assignedAt: '2026-09-01T00:00:00Z', notes: null, dispatchId: status === 'Approved' ? `dispatch-${id}` : null,
})
const dispatch = (id: string, status: DispatchDto['status']): DispatchDto => ({
  id: `dispatch-${id}`, assignmentId: id, status, approvalStatus: 'Approved', approvedByUserId: null, approvedAt: null, dispatchedAt: null, enRouteAt: null, onSceneAt: null, resolvedAt: status === 'Resolved' ? '2026-09-02T00:00:00Z' : null, cancelledAt: status === 'Cancelled' ? '2026-09-03T00:00:00Z' : null, notes: null,
})
beforeEach(() => {
  vi.resetAllMocks()
  vi.mocked(api.getAssignments).mockResolvedValue([assignment('ongoing'), assignment('resolved'), assignment('cancelled'), assignment('proposal', 'Proposed')])
  vi.mocked(api.getDispatches).mockResolvedValue([dispatch('ongoing', 'OnScene'), dispatch('resolved', 'Resolved'), dispatch('cancelled', 'Cancelled')])
  vi.mocked(api.getRescueTeams).mockResolvedValue([])
})
async function mount() {
  render(<RescueCoordinatorDashboard user={{ id: 'user', fullName: 'Operator', email: 'test@example.test', role: 'RescueTeam' }} onLogout={vi.fn()} />)
  await screen.findByRole('button', { name: 'Refresh' })
  const user = userEvent.setup()
  const navigate = async (name: string) => user.click(within(screen.getByRole('navigation')).getByRole('button', { name: new RegExp(name) }))
  return { user, navigate }
}
it('keeps current planning and active missions but excludes terminal assignments and revision candidates', async () => {
  const { user, navigate } = await mount()
  await navigate('Assignments')
  expect(screen.getByText('ongoing')).toBeTruthy()
  expect(screen.getByText('proposal')).toBeTruthy()
  expect(screen.queryByText('resolved')).toBeNull()
  expect(screen.queryByText('cancelled')).toBeNull()
  expect((screen.getByText('ongoing').closest('button') as HTMLButtonElement).disabled).toBe(true)
  await user.click(screen.getByText('proposal'))
  expect(screen.getByRole('heading', { name: /Revise assignment/ })).toBeTruthy()
})
it('separates active dispatches from history sorted by terminal time', async () => {
  const { user, navigate } = await mount()
  await navigate('Dispatches')
  expect(screen.getByText('dispatch-ongoing')).toBeTruthy()
  expect(screen.queryByText('dispatch-resolved')).toBeNull()
  expect(screen.queryByText('dispatch-cancelled')).toBeNull()
  await user.selectOptions(screen.getByLabelText('Dispatch view'), 'history')
  expect(screen.getAllByText(/^dispatch-/).map((item) => item.textContent)).toEqual(['dispatch-cancelled', 'dispatch-resolved'])
  expect(screen.queryByRole('button', { name: 'Resolve mission' })).toBeNull()
})
it('resolving refetches resources and immediately moves mission and assignment out of current work', async () => {
  vi.mocked(api.getAssignments).mockResolvedValue([assignment('ongoing')])
  vi.mocked(api.getDispatches).mockResolvedValue([dispatch('ongoing', 'OnScene')])
  vi.mocked(api.transitionDispatch).mockImplementation(async () => {
    const resolved = dispatch('ongoing', 'Resolved')
    vi.mocked(api.getDispatches).mockResolvedValue([resolved])
    vi.mocked(api.getRescueTeams).mockResolvedValue([{ id: 'team', name: 'Kandy', status: 'Available', baseLatitude: null, baseLongitude: null, members: [], vehicles: [{ id: 'vehicle', plateNumber: 'PJ 2343', type: 'Ambulance', status: 'Available', capacity: 1 }] }])
    return resolved
  })
  const { user, navigate } = await mount()
  await navigate('Dispatches')
  await user.click(screen.getByRole('button', { name: 'Resolve mission' }))
  expect(await screen.findByText('No active dispatches.')).toBeTruthy()
  expect(api.transitionDispatch).toHaveBeenCalledWith('dispatch-ongoing', 'Resolved')
  expect(api.getAssignments).toHaveBeenCalledTimes(2)
  expect(api.getRescueTeams).toHaveBeenCalledTimes(2)
  await user.selectOptions(screen.getByLabelText('Dispatch view'), 'history')
  expect(screen.getByText('dispatch-ongoing')).toBeTruthy()
  await navigate('Assignments')
  expect(screen.queryByText('ongoing')).toBeNull()
  await waitFor(() => expect(screen.queryByRole('heading', { name: /Revise assignment/ })).toBeNull())
  await navigate('Rescue teams')
  expect(screen.getByText('Kandy')).toBeTruthy()
  expect(screen.getByText('PJ 2343').closest('summary')?.textContent).toContain('Available')
})

it.each(['Resolved', 'Cancelled'] as const)('clears a selected revision when refresh reveals a %s dispatch', async (status) => {
  vi.mocked(api.getAssignments).mockResolvedValue([assignment('proposal', 'Proposed')])
  vi.mocked(api.getDispatches).mockResolvedValue([])
  const { user, navigate } = await mount()
  await navigate('Assignments')
  await user.click(screen.getByText('proposal'))
  expect(screen.getByRole('heading', { name: /Revise assignment/ })).toBeTruthy()
  vi.mocked(api.getDispatches).mockResolvedValue([dispatch('proposal', status)])
  await user.click(screen.getByRole('button', { name: 'Refresh' }))
  expect(screen.queryByText('proposal')).toBeNull()
  await waitFor(() => expect(screen.queryByRole('heading', { name: /Revise assignment/ })).toBeNull())
})
