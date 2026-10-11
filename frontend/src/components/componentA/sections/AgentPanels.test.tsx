import { beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import EnrichmentPanel from './EnrichmentPanel'
import ZonePlanner from './ZonePlanner'
import { ApiError, apiFetch } from '../../../shared/api/client'
import type { AgentRun } from '../../../shared/types'
import type { EnrichmentProposal, Incident, ZonePlan } from '../types'

vi.mock('../../../shared/api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../shared/api/client')>()),
  apiFetch: vi.fn(),
  queryString: (params: Record<string, unknown>) =>
    '?' + Object.entries(params).map(([k, v]) => `${k}=${String(v)}`).join('&'),
}))
const api = vi.mocked(apiFetch)

const QUOTA = "Google AI's daily quota for this key is used up, so AI recommendations are paused until it resets."

/** A run the model could not serve: no output, and the provider's reason. */
function failedRun(agentName: string, extra: Partial<AgentRun> = {}): AgentRun {
  return { ...run(agentName, null), status: 'Failed', model: null, outputJson: null, errorMessage: QUOTA, ...extra }
}

function run(agentName: string, output: unknown, extra: Partial<AgentRun> = {}): AgentRun {
  return {
    id: 'run-1', agentName, objective: 'test', incidentId: 'i-1', status: 'Succeeded', model: 'gemini',
    usedFallback: false, approved: false, approvedAt: null, decision: 'Pending', decisionNote: null,
    modelAttempts: 1, errorMessage: null, durationMs: 10, startedAt: new Date().toISOString(),
    completedAt: new Date().toISOString(), planJson: null, toolCallsJson: null,
    outputJson: JSON.stringify(output), ...extra,
  }
}

const incident = { id: 'i-1', status: 'Reported' } as Incident

const enrichment: EnrichmentProposal = {
  suggestions: [
    { field: 'district', current: null, proposed: 'Colombo', reason: 'Nearest town.' },
    { field: 'estimatedAffectedPeople', current: null, proposed: '160', reason: '40 families.' },
  ],
  duplicate: { incidentId: 'i-0', title: 'Kelani flood', distanceKm: 0.12, similarity: 0.5, reason: 'Same flood.' },
  summary: 'Two fixes and a duplicate.',
}

/** The body sent with the call to `path`, parsed. */
function bodyOf(path: string) {
  const call = api.mock.calls.find(([url]) => url === path)
  return JSON.parse(String(call?.[1]?.body ?? '{}')) as Record<string, unknown>
}

describe('Enrichment agent panel', () => {
  beforeEach(() => { cleanup(); api.mockReset() })

  it('shows each suggestion and the duplicate, all ticked by default', async () => {
    api.mockResolvedValue([run('IncidentEnrichmentAgent', enrichment)])
    render(<EnrichmentPanel incident={incident} onChanged={() => {}} />)

    expect(await screen.findByText('Two fixes and a duplicate.')).toBeTruthy()
    expect(screen.getByText('Colombo')).toBeTruthy()
    expect(screen.getByText(/Likely duplicate/)).toBeTruthy()
    expect(screen.getByRole('button', { name: /Apply & merge/ })).toBeTruthy()
  })

  it('approves only the fields left ticked', async () => {
    api.mockResolvedValueOnce([run('IncidentEnrichmentAgent', enrichment)]).mockResolvedValue([])
    const onChanged = vi.fn()
    render(<EnrichmentPanel incident={incident} onChanged={onChanged} />)

    await screen.findByText('Colombo')
    const [district, , duplicate] = screen.getAllByRole('checkbox')
    fireEvent.click(district)
    fireEvent.click(duplicate)
    fireEvent.click(screen.getByRole('button', { name: /Apply selected/ }))

    await waitFor(() => expect(onChanged).toHaveBeenCalled())
    expect(bodyOf('/api/agentruns/run-1/approve')).toEqual({
      fields: ['estimatedAffectedPeople'],
      mergeDuplicate: false,
    })
  })

  it('says why there is no check when the AI quota is used up, with nothing to apply', async () => {
    api.mockResolvedValue([failedRun('IncidentEnrichmentAgent')])
    render(<EnrichmentPanel incident={incident} onChanged={() => {}} />)

    expect(await screen.findByText('No AI record check.')).toBeTruthy()
    expect(screen.getByText(QUOTA, { exact: false })).toBeTruthy()
    expect(screen.queryByRole('button', { name: /Apply/ })).toBeNull()
    expect(screen.queryByText('rule engine')).toBeNull()
  })

  it('shows the reason once when trying again fails the same way', async () => {
    api.mockImplementation((path) =>
      String(path).endsWith('/enrich')
        ? Promise.reject(new ApiError(503, QUOTA))
        : Promise.resolve([failedRun('IncidentEnrichmentAgent')]),
    )
    const onChanged = vi.fn()
    render(<EnrichmentPanel incident={incident} onChanged={onChanged} />)

    fireEvent.click(await screen.findByRole('button', { name: 'Try again' }))

    // The failed run already says why, so the 503 raises no second banner.
    await waitFor(() => expect(onChanged).toHaveBeenCalled())
    expect(screen.queryByRole('alert')).toBeNull()
    expect(screen.getAllByText(QUOTA, { exact: false })).toHaveLength(1)
  })
})

const plan: ZonePlan = {
  zones: [
    {
      action: 'create', zoneId: null, name: 'Colombo flood area', status: 'Danger',
      centerLatitude: 6.94, centerLongitude: 79.87, radiusMeters: 3000, district: 'Colombo',
      expiresInHours: 48, rationale: 'Two floods.', basedOnIncidentIds: ['a', 'b'],
    },
    {
      action: 'retire', zoneId: 'z-1', name: 'Old closure', status: 'Caution',
      centerLatitude: 8.3, centerLongitude: 80.4, radiusMeters: 1000, district: null,
      expiresInHours: null, rationale: 'Nothing inside.', basedOnIncidentIds: [],
    },
  ],
  summary: 'One area zone, one to retire.',
}

describe('Zone planning agent panel', () => {
  beforeEach(() => { cleanup(); api.mockReset() })

  it('approves an untouched plan as proposed, and previews its zones', async () => {
    api.mockResolvedValueOnce([run('ZonePlanningAgent', plan, { incidentId: null })]).mockResolvedValue([])
    const onPreview = vi.fn()
    render(<ZonePlanner onPreview={onPreview} onChanged={() => {}} />)

    expect(await screen.findByText('One area zone, one to retire.')).toBeTruthy()
    await waitFor(() => expect(onPreview).toHaveBeenLastCalledWith([expect.objectContaining({ name: 'Colombo flood area' })]))

    fireEvent.click(screen.getByRole('button', { name: /Approve 2 change/ }))
    await waitFor(() => expect(bodyOf('/api/agentruns/run-1/approve')).toEqual({}))
  })

  it('sends the coordinator’s edited zone when they change it', async () => {
    api.mockResolvedValueOnce([run('ZonePlanningAgent', plan, { incidentId: null })]).mockResolvedValue([])
    render(<ZonePlanner onPreview={() => {}} onChanged={() => {}} />)

    const name = await screen.findByDisplayValue('Colombo flood area')
    fireEvent.change(name, { target: { value: 'Kelani evacuation area' } })
    fireEvent.click(screen.getByRole('button', { name: /Approve 2 change/ }))

    await waitFor(() => {
      const body = bodyOf('/api/agentruns/run-1/approve') as { zones?: { name: string }[]; retireZoneIds?: string[] }
      expect(body.zones?.[0].name).toBe('Kelani evacuation area')
      expect(body.retireZoneIds).toEqual(['z-1'])
    })
  })

  it('says why there is no plan when the AI quota is used up, with nothing to approve', async () => {
    api.mockResolvedValue([failedRun('ZonePlanningAgent', { incidentId: null })])
    const onPreview = vi.fn()
    render(<ZonePlanner onPreview={onPreview} onChanged={() => {}} />)

    expect(await screen.findByText('No AI plan.')).toBeTruthy()
    expect(screen.getByText(QUOTA, { exact: false })).toBeTruthy()
    expect(screen.queryByRole('button', { name: /Approve/ })).toBeNull()
    expect(onPreview).toHaveBeenLastCalledWith([])
  })

  it('still labels a plan recorded by the old rule engine', async () => {
    api.mockResolvedValue([run('ZonePlanningAgent', plan, { incidentId: null, usedFallback: true, model: 'rule-engine' })])
    render(<ZonePlanner onPreview={() => {}} onChanged={() => {}} />)

    expect(await screen.findByText('rule engine')).toBeTruthy()
  })
})
