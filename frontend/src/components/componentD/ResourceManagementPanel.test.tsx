import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import ResourceManagementPanel from './ResourceManagementPanel'
import type { RescueTeamDto, TeamStatus } from './types'

// Listing uses props; fail immediately if these interactions unexpectedly call an API.
vi.mock('./api', () => {
  const unexpected = () => { throw new Error('Unexpected API call during list interaction') }
  return Object.fromEntries(['addTeamMember', 'addVehicle', 'createRescueTeam', 'deleteRescueTeam', 'removeTeamMember', 'removeVehicle', 'updateRescueTeam', 'updateTeamMember', 'updateVehicle'].map((name) => [name, unexpected]))
})

afterEach(cleanup)

function team(id: string, name: string, status: TeamStatus = 'Available'): RescueTeamDto {
  return {
    id, name, status, baseLatitude: null, baseLongitude: null,
    members: [
      { id: `${id}-m1`, fullName: `Nimal ${name}`, phone: '0712345678', skill: 'FirstAid', isAvailable: true },
      { id: `${id}-m2`, fullName: `Kumari ${name}`, phone: '0766413012', skill: 'Driving', isAvailable: false },
    ],
    vehicles: [{ id: `${id}-v1`, plateNumber: `WP-${id}-1234`, type: 'Ambulance', status: 'Available', capacity: 4 }],
  }
}

function fixture() {
  return [team('7', 'Galle'), team('3', 'Colombo', 'OnMission'), team('1', 'Anuradhapura'), team('6', 'FLOOD Unit', 'OffDuty'), team('2', 'Badulla'), team('5', 'Embilipitiya'), team('4', 'Dambulla', 'OnMission')]
}

function show(teams = fixture()) {
  return render(<ResourceManagementPanel teams={teams} refresh={vi.fn().mockResolvedValue(undefined)} />)
}
function names() {
  return screen.queryAllByRole('article').map((card) => within(card).getByRole('heading', { level: 2 }).textContent)
}
function search(value: string) {
  fireEvent.change(screen.getByLabelText('Search teams, members or vehicles'), { target: { value } })
}
function next() { fireEvent.click(screen.getByRole('button', { name: 'Next' })) }

describe('Component D Rescue Teams list', () => {
  it('searches by team name', () => {
    show(); search('Colombo')
    expect(names()).toEqual(['Colombo'])
  })

  it('searches by member name and keeps the entire team card', () => {
    show(); search('Nimal Galle')
    expect(names()).toEqual(['Galle'])
    expect(screen.getByText('Kumari Galle')).toBeTruthy()
    expect(screen.getByText('WP-7-1234')).toBeTruthy()
  })

  it('searches by vehicle plate and retains all members', () => {
    show(); search('WP-7-1234')
    expect(names()).toEqual(['Galle'])
    expect(screen.getByText('Nimal Galle')).toBeTruthy()
    expect(screen.getByText('Kumari Galle')).toBeTruthy()
  })

  it('matches case-insensitively and trims search text', () => {
    show(); search('  flood UNIT  ')
    expect(names()).toEqual(['FLOOD Unit'])
  })

  it.each(['Available', 'OnMission', 'OffDuty'] as const)('filters by %s status', (status) => {
    const teams = fixture(); show(teams)
    fireEvent.change(screen.getByLabelText('Team Status'), { target: { value: status } })
    expect(names()).toEqual(teams.filter((t) => t.status === status).map((t) => t.name).sort((a, b) => a.toLowerCase().localeCompare(b.toLowerCase())))
  })

  it('defaults to all statuses and case-insensitive A-Z without mutating props', () => {
    const teams = Object.freeze(fixture()) as unknown as RescueTeamDto[]
    const original = teams.map((t) => t.id); show(teams)
    expect((screen.getByLabelText('Team Status') as HTMLSelectElement).value).toBe('')
    expect(names()).toEqual(['Anuradhapura', 'Badulla', 'Colombo', 'Dambulla', 'Embilipitiya'])
    expect(teams.map((t) => t.id)).toEqual(original)
  })

  it('sorts Z-A case-insensitively', () => {
    show()
    fireEvent.change(screen.getByLabelText('Sort'), { target: { value: 'desc' } })
    expect(names()).toEqual(['Galle', 'FLOOD Unit', 'Embilipitiya', 'Dambulla', 'Colombo'])
  })

  it.each(['asc', 'desc'])('uses team ID to break equal-name ties in %s order', (sort) => {
    show([team('b', 'Kandy'), team('a', 'kandy')])
    fireEvent.change(screen.getByLabelText('Sort'), { target: { value: sort } })
    expect(names()).toEqual(['kandy', 'Kandy'])
  })

  it('shows five teams and disables Previous on the first page', () => {
    show()
    expect(names()).toHaveLength(5)
    expect(screen.getByText('Showing 1-5 of 7 teams')).toBeTruthy()
    expect(screen.getByText('Page 1 of 2')).toBeTruthy()
    expect((screen.getByRole('button', { name: 'Previous' }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('moves Next to the last page and disables Next', () => {
    show(); next()
    expect(names()).toEqual(['FLOOD Unit', 'Galle'])
    expect(screen.getByText('Showing 6-7 of 7 teams')).toBeTruthy()
    expect(screen.getByText('Page 2 of 2')).toBeTruthy()
    expect((screen.getByRole('button', { name: 'Next' }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('returns to the first page with Previous', () => {
    show(); next(); fireEvent.click(screen.getByRole('button', { name: 'Previous' }))
    expect(names()).toHaveLength(5)
    expect(screen.getByText('Page 1 of 2')).toBeTruthy()
  })

  it.each(['search', 'status', 'sort'])('resets pagination when %s changes', (control) => {
    show(); next()
    if (control === 'search') search('a')
    if (control === 'status') fireEvent.change(screen.getByLabelText('Team Status'), { target: { value: 'Available' } })
    if (control === 'sort') fireEvent.change(screen.getByLabelText('Sort'), { target: { value: 'desc' } })
    expect(screen.getByText(/Page 1 of/)).toBeTruthy()
    expect((screen.getByRole('button', { name: 'Previous' }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('combines nested search, status and sort before pagination', () => {
    const teams = Array.from({ length: 12 }, (_, i) => team(String(i).padStart(2, '0'), `Unit ${String(i).padStart(2, '0')}`, i % 2 ? 'OffDuty' : 'Available'))
    show(teams); search('nimal')
    fireEvent.change(screen.getByLabelText('Team Status'), { target: { value: 'Available' } })
    fireEvent.change(screen.getByLabelText('Sort'), { target: { value: 'desc' } })
    expect(names()).toEqual(['Unit 10', 'Unit 08', 'Unit 06', 'Unit 04', 'Unit 02'])
    expect(screen.getByText('Showing 1-5 of 6 teams')).toBeTruthy()
    next(); expect(names()).toEqual(['Unit 00'])
  })

  it('distinguishes zero matches from a genuinely empty list', () => {
    show(); search('not present')
    expect(screen.getByText('No rescue teams match your search or filter.')).toBeTruthy()
    expect(screen.queryByText('No rescue teams yet. Add your first team.')).toBeNull()
    expect(names()).toHaveLength(0)
    expect(screen.getByText('Showing 0-0 of 0 teams')).toBeTruthy()
    expect(screen.getByText('Page 1 of 1')).toBeTruthy()
  })

  it('preserves the true-empty message', () => {
    show([])
    expect(screen.getByText('No rescue teams yet. Add your first team.')).toBeTruthy()
    expect(screen.queryByText('No rescue teams match your search or filter.')).toBeNull()
    expect((screen.getByRole('button', { name: 'Next' }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('clamps after data shrink and retains that page if data grows again', () => {
    const teams = Array.from({ length: 12 }, (_, i) => team(String(i), `Team ${String(i).padStart(2, '0')}`))
    const refresh = vi.fn().mockResolvedValue(undefined)
    const { rerender } = render(<ResourceManagementPanel teams={teams} refresh={refresh} />)
    next(); next(); expect(screen.getByText('Page 3 of 3')).toBeTruthy()
    rerender(<ResourceManagementPanel teams={teams.slice(0, 6)} refresh={refresh} />)
    expect(screen.getByText('Page 2 of 2')).toBeTruthy()
    expect(names()).toEqual(['Team 05'])
    rerender(<ResourceManagementPanel teams={teams} refresh={refresh} />)
    expect(screen.getByText('Page 2 of 3')).toBeTruthy()
    rerender(<ResourceManagementPanel teams={[]} refresh={refresh} />)
    expect(screen.getByText('Page 1 of 1')).toBeTruthy()
  })
})
