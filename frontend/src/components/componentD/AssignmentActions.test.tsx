import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import RescueCoordinatorDashboard from './RescueCoordinatorDashboard'
import * as api from './api'
import { ApiError } from '../../shared/api/client'
import type { AssignmentDto, DispatchDto, RescueTeamDto } from './types'

vi.mock('./api')
const team: RescueTeamDto = { id: 'team', name: 'Rescue team', status: 'Available', baseLatitude: 7, baseLongitude: 80,
  members: [{ id: 'member', fullName: 'Medic', phone: '0712345678', skill: 'FirstAid', isAvailable: true }],
  vehicles: [{ id: 'vehicle', plateNumber: 'TEST-1', type: 'Ambulance', status: 'Available', capacity: 4 }] }
const assignment: AssignmentDto = { id: 'plan', incidentId: 'incident', helpRequestId: null, rescueTeamId: 'team', rescueTeamName: team.name, vehicleId: 'vehicle', vehiclePlateNumber: 'TEST-1', requiredSkill: 'FirstAid', requiredCapacity: 2, notes: 'Confirmed demand', status: 'Proposed', planVersion: 1, assignedAt: '2026-09-01T00:00:00Z', dispatchId: null }
beforeEach(() => {
  vi.resetAllMocks()
  vi.mocked(api.getAssignments).mockResolvedValue([assignment])
  vi.mocked(api.getActiveIncidents).mockResolvedValue([])
  vi.mocked(api.getIncident).mockRejectedValue(new ApiError(404, 'Incident unavailable'))
  vi.mocked(api.getRescueTeams).mockResolvedValue([team])
  vi.mocked(api.getDispatches).mockResolvedValue([])
})
afterEach(cleanup)
async function mount() {
  render(<RescueCoordinatorDashboard user={{ id: 'user', fullName: 'Rescue Coordinator', email: 'rescue@example.test', role: 'RescueTeam' }} onLogout={vi.fn()} />)
  await waitFor(() => expect((screen.getByRole('button', { name: 'Refresh' }) as HTMLButtonElement).disabled).toBe(false))
  fireEvent.click(within(screen.getByRole('navigation')).getByRole('button', { name: /Assignments/ }))
}
const cancelCard = () => fireEvent.click(screen.getByRole('button', { name: 'Cancel assignment' }))
const confirm = () => fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Cancel assignment' }))

it.each(['Proposed', 'PendingApproval', 'Rejected'] as const)('shows actions and loads current %s assignment values', async (status) => {
  vi.mocked(api.getAssignments).mockResolvedValue([{ ...assignment, status }])
  await mount(); fireEvent.click(screen.getByRole('button', { name: 'Update assignment' }))
  expect(screen.getByRole('heading', { name: 'Revise assignment · Plan v1' })).toBeTruthy()
  for (const [label, value] of [['Team', 'team'], ['Vehicle', 'vehicle'], ['Required skill', 'FirstAid'], ['People/patients requiring transport', '2'], ['Notes', 'Confirmed demand']])
    expect((screen.getByLabelText(label) as HTMLInputElement).value).toBe(value)
  expect(screen.getByRole('button', { name: 'Cancel assignment' })).toBeTruthy()
})

it.each(['incident', 'help'])('preserves %s objective and uses the existing revision endpoint', async (objective) => {
  const original = objective === 'help' ? { ...assignment, incidentId: null, helpRequestId: 'help' } : assignment
  vi.mocked(api.getAssignments).mockResolvedValue([original])
  vi.mocked(api.reviseAssignment).mockImplementation(async (_id, input) => {
    const revised = { ...original, ...input, planVersion: 2 }
    vi.mocked(api.getAssignments).mockResolvedValue([revised]); return revised
  })
  await mount(); fireEvent.click(screen.getByRole('button', { name: 'Update assignment' }))
  expect(screen.queryByRole('combobox', { name: 'Incident' })).toBeNull()
  if (objective === 'help') expect(screen.getByText('Help Request is preserved from the selected assignment.')).toBeTruthy()
  fireEvent.change(screen.getByLabelText('Notes'), { target: { value: 'Changed notes' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save revision' }))
  await screen.findByRole('heading', { name: 'Revise assignment · Plan v2' })
  expect(screen.getByText('Assignment updated. Fresh safety review required.')).toBeTruthy()
  expect(api.reviseAssignment).toHaveBeenCalledExactlyOnceWith('plan', { rescueTeamId: 'team', vehicleId: 'vehicle', requiredSkill: 'FirstAid', requiredCapacity: 2, notes: 'Changed notes' })
  expect(api.createAssignment).not.toHaveBeenCalled()
})

it('permits cancellation but not update for an Approved plan without a dispatch', async () => {
  vi.mocked(api.getAssignments).mockResolvedValue([{ ...assignment, status: 'Approved' }])
  await mount(); expect(screen.queryByRole('button', { name: 'Update assignment' })).toBeNull()
  expect(screen.getByRole('button', { name: 'Cancel assignment' })).toBeTruthy()
})

it.each(['Pending', 'Dispatched', 'EnRoute', 'OnScene', 'Resolved', 'Cancelled'] as const)('hides actions when any %s dispatch exists', async (status) => {
  vi.mocked(api.getAssignments).mockResolvedValue([{ ...assignment, dispatchId: 'dispatch' }])
  vi.mocked(api.getDispatches).mockResolvedValue([{ id: 'dispatch', assignmentId: 'plan', status } as DispatchDto])
  await mount()
  expect(screen.queryByRole('button', { name: 'Update assignment' })).toBeNull()
  expect(screen.queryByRole('button', { name: 'Cancel assignment' })).toBeNull()
})

it('excludes Cancelled plans from current assignments', async () => {
  vi.mocked(api.getAssignments).mockResolvedValue([{ ...assignment, status: 'Cancelled' }])
  await mount(); expect(screen.queryByText('plan')).toBeNull()
  expect(screen.queryByRole('button', { name: 'Update assignment' })).toBeNull()
  expect(screen.queryByRole('button', { name: 'Cancel assignment' })).toBeNull()
})

it('requires confirmation, supports Keep assignment, and refreshes resources after success', async () => {
  vi.mocked(api.cancelAssignment).mockImplementation(async () => {
    const cancelled: AssignmentDto = { ...assignment, status: 'Cancelled', planVersion: 2 }
    vi.mocked(api.getAssignments).mockResolvedValue([cancelled]); return cancelled
  })
  await mount(); cancelCard()
  expect(api.cancelAssignment).not.toHaveBeenCalled()
  expect(screen.getByText('This response plan will be cancelled and kept in history. It will no longer reserve its rescue team or vehicle.')).toBeTruthy()
  expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Keep assignment' }))
  fireEvent.click(screen.getByRole('button', { name: 'Keep assignment' }))
  expect(screen.queryByRole('dialog')).toBeNull(); expect(api.cancelAssignment).not.toHaveBeenCalled()
  cancelCard(); confirm()
  await screen.findByText('Assignment cancelled.')
  expect(api.cancelAssignment).toHaveBeenCalledExactlyOnceWith('plan')
  await waitFor(() => expect(api.getRescueTeams).toHaveBeenCalledTimes(2))
  expect(api.getAssignments).toHaveBeenCalledTimes(2); expect(api.getDispatches).toHaveBeenCalledTimes(2)
  expect(screen.queryByText('plan')).toBeNull()
})

it('shows backend cancellation conflicts locally without losing the assignment', async () => {
  vi.mocked(api.cancelAssignment).mockRejectedValue(new ApiError(409, 'This assignment already has a dispatch. Cancel the dispatch instead.'))
  await mount(); cancelCard(); confirm()
  expect((await screen.findByRole('alert')).textContent).toContain('Cancel the dispatch instead')
  expect(screen.getByText('plan')).toBeTruthy()
  expect(screen.queryByText('Assignment cancelled.')).toBeNull()
})

it('clears a selected plan and its safety review after cancellation', async () => {
  vi.mocked(api.cancelAssignment).mockImplementation(async () => {
    const cancelled: AssignmentDto = { ...assignment, status: 'Cancelled', planVersion: 2 }
    vi.mocked(api.getAssignments).mockResolvedValue([cancelled]); return cancelled
  })
  await mount(); fireEvent.click(screen.getByRole('button', { name: 'Update assignment' }))
  cancelCard(); confirm(); await screen.findByText('Assignment cancelled.')
  expect(screen.queryByRole('heading', { name: /Revise assignment/ })).toBeNull()
  fireEvent.click(within(screen.getByRole('navigation')).getByRole('button', { name: /AI safety review/ }))
  expect(screen.queryByRole('button', { name: 'Run AI Safety Validation' })).toBeNull()
})


it('renders a modal outside the assignment panel and closes on Escape or backdrop without mutation', async () => {
  await mount()
  const trigger = screen.getByRole('button', { name: 'Cancel assignment' }); trigger.focus(); cancelCard()
  const dialog = screen.getByRole('dialog', { name: 'Cancel this assignment?' })
  expect(dialog.getAttribute('aria-modal')).toBe('true')
  expect(dialog.closest('.assignment-list')).toBeNull()
  expect(document.querySelector('.assignment-cancel-confirmation')).toBeNull()
  expect(document.body.style.overflow).toBe('hidden')
  fireEvent.keyDown(dialog, { key: 'Escape' })
  expect(screen.queryByRole('dialog')).toBeNull(); expect(document.activeElement).toBe(trigger)
  cancelCard(); fireEvent.click(document.querySelector('.assignment-modal-overlay')!)
  expect(screen.queryByRole('dialog')).toBeNull(); expect(api.cancelAssignment).not.toHaveBeenCalled()
})

it('traps keyboard focus on both modal buttons', async () => {
  await mount(); cancelCard()
  const dialog = screen.getByRole('dialog')
  const keep = within(dialog).getByRole('button', { name: 'Keep assignment' })
  const cancel = within(dialog).getByRole('button', { name: 'Cancel assignment' })
  fireEvent.keyDown(keep, { key: 'Tab', shiftKey: true }); expect(document.activeElement).toBe(cancel)
  fireEvent.keyDown(cancel, { key: 'Tab' }); expect(document.activeElement).toBe(keep)
})

it('blocks duplicate cancellation and dismissal while pending, then separates the backend error from the message', async () => {
  let fail!: (reason: Error) => void
  vi.mocked(api.cancelAssignment).mockReturnValue(new Promise((_, reject) => { fail = reject }))
  await mount(); cancelCard()
  const dialog = screen.getByRole('dialog')
  const description = document.getElementById(dialog.getAttribute('aria-describedby')!)!
  const message = description.textContent
  expect(screen.queryByRole('alert')).toBeNull()
  confirm()
  expect(api.cancelAssignment).toHaveBeenCalledOnce()
  expect((within(dialog).getByRole('button', { name: /Cancelling assignment/ }) as HTMLButtonElement).disabled).toBe(true)
  expect((within(dialog).getByRole('button', { name: 'Keep assignment' }) as HTMLButtonElement).disabled).toBe(true)
  fireEvent.click(within(dialog).getByRole('button', { name: /Cancelling assignment/ }))
  fireEvent.keyDown(dialog, { key: 'Escape' }); fireEvent.click(document.querySelector('.assignment-modal-overlay')!)
  expect(screen.getByRole('dialog')).toBeTruthy(); expect(api.cancelAssignment).toHaveBeenCalledOnce()
  await act(async () => fail(new ApiError(404, 'The requested resource is no longer available.')))
  expect(screen.getByRole('alert').textContent).toBe('The requested resource is no longer available.')
  expect(description.textContent).toBe(message); expect(description.contains(screen.getByRole('alert'))).toBe(false)
  fireEvent.click(screen.getByRole('button', { name: 'Keep assignment' })); cancelCard()
  expect(screen.queryByRole('alert')).toBeNull()
})
