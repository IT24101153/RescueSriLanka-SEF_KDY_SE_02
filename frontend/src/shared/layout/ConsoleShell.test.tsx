// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router-dom'
import ConsoleShell from './ConsoleShell'
import { storeSession } from '../auth/session'
import type { Role, Session } from '../auth/session'

vi.mock('../../components/componentA/DisasterDashboard', () => ({
  default: () => <h1>Incident &amp; Disaster Map</h1>,
}))
vi.mock('../../components/componentB/HelpRequestDashboard', () => ({ default: () => null }))
vi.mock('../../components/componentB/HelpRequestsReview', () => ({ default: () => null }))
vi.mock('../../components/componentC/ResourceDashboard', () => ({ default: () => null }))
vi.mock('../../components/componentD/api', async (importOriginal) => ({
  ...await importOriginal<typeof import('../../components/componentD/api')>(),
  getRescueTeams: vi.fn().mockResolvedValue([]),
  getAssignments: vi.fn().mockResolvedValue([]),
  getDispatches: vi.fn().mockResolvedValue([]),
}))

function Location() {
  return <output data-testid="location">{useLocation().pathname}</output>
}

function mount(role: Role, path: string) {
  const session: Session = {
    token: 'test-only', expiresAt: '2099-01-01T00:00:00Z',
    user: { id: 'test-user', fullName: 'Test operator', email: 'test@example.test', role },
  }
  storeSession(session, false)
  render(<MemoryRouter initialEntries={[path]}>
    <ConsoleShell session={session} onSignOut={vi.fn()} />
    <Location />
  </MemoryRouter>)
}

beforeEach(() => { localStorage.clear(); sessionStorage.clear() })
afterEach(() => { cleanup(); localStorage.clear(); sessionStorage.clear() })

describe('console role routing', () => {
  it.each(['/', '/dashboard', '/disaster', '/console/disaster', '/rescue'])('RescueTeam lands in rescue from %s', async (path) => {
    mount('RescueTeam', path)
    expect(await screen.findByRole('heading', { name: 'Rescue Coordination Center' })).toBeTruthy()
    await screen.findByRole('button', { name: 'Refresh' })
    expect(screen.getByTestId('location').textContent).toBe('/rescue')
    expect(screen.queryByRole('heading', { name: 'Incident & Disaster Map' })).toBeNull()
    expect(screen.queryByText('Disaster dashboard')).toBeNull()
  })

  it.each(['/', '/rescue', '/dashboard'])('EmergencyCoordinator lands in disaster from %s', async (path) => {
    mount('EmergencyCoordinator', path)
    expect(await screen.findByRole('heading', { name: 'Incident & Disaster Map' })).toBeTruthy()
    expect(screen.getByTestId('location').textContent).toBe('/')
    expect(screen.queryByRole('heading', { name: 'Rescue Coordination Center' })).toBeNull()
    expect(screen.queryByRole('navigation', { name: 'Rescue coordination sections' })).toBeNull()
  })

  it.each(['/', '/rescue'])('Citizen gets no management dashboard at %s', (path) => {
    mount('Citizen', path)
    expect(screen.getByText('Your account does not have access to the console.')).toBeTruthy()
    expect(screen.queryByRole('heading')).toBeNull()
    expect(screen.queryByRole('navigation')).toBeNull()
  })

  it('keeps the existing Component D sections reachable', async () => {
    mount('RescueTeam', '/')
    await screen.findByRole('heading', { name: 'Rescue Coordination Center' })
    await screen.findByRole('button', { name: 'Refresh' })
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Logout' })).toBeNull()
    const user = userEvent.setup()
    const nav = within(screen.getByRole('navigation', { name: 'Rescue coordination sections' }))
    for (const name of ['Overview', 'Rescue teams', 'Assignments', 'AI safety review', 'Dispatches']) {
      await user.click(nav.getByRole('button', { name: new RegExp(name) }))
      expect(screen.getByTestId('location').textContent).toBe('/rescue')
      expect(screen.queryByRole('heading', { name: 'Incident & Disaster Map' })).toBeNull()
    }
    expect(screen.getByRole('heading', { name: 'Active dispatches' })).toBeTruthy()
  })
})
