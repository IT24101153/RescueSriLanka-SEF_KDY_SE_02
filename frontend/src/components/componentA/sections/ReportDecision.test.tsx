import { beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import ReportDecision from './ReportDecision'
import { ApiError, apiFetch } from '../../../shared/api/client'
import type { AgentRun } from '../../../shared/types'
import type { AnalysisProposal, Incident } from '../types'

vi.mock('../../../shared/api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../shared/api/client')>()),
  apiFetch: vi.fn(),
}))
const api = vi.mocked(apiFetch)

const QUOTA = "Google AI's daily quota for this key is used up, so AI recommendations are paused until it resets."

const proposal: AnalysisProposal = {
  severity: 'High', severityScore: 75, confidence: 0.8,
  recommendedZoneStatus: 'Danger', recommendedRadiusMeters: 3000,
  rationale: 'Water is waist-deep against houses.',
}

function run(extra: Partial<AgentRun> = {}): AgentRun {
  return {
    id: 'run-1', agentName: 'IncidentAnalysisAgent', objective: 'Classify', incidentId: 'i-1',
    status: 'Succeeded', model: 'gemini', usedFallback: false, approved: false, approvedAt: null,
    decision: 'Pending', decisionNote: null, modelAttempts: 1, errorMessage: null, durationMs: 10,
    startedAt: new Date().toISOString(), completedAt: new Date().toISOString(),
    planJson: null, toolCallsJson: null, outputJson: JSON.stringify(proposal), ...extra,
  }
}

/** The run the agent records when the model could not be used: no grade, and why. */
const outOfQuota = run({ status: 'Failed', model: null, outputJson: null, errorMessage: QUOTA })

const incident = { id: 'i-1', status: 'Reported', severity: 'Moderate', severityOverridden: false } as Incident

/** The call made to `path`, if any. */
const callTo = (path: string) => api.mock.calls.find(([url]) => url === path)

describe('Report decision — the AI condition', () => {
  beforeEach(() => { cleanup(); api.mockReset() })

  it('shows the AI grade for the coordinator to approve', async () => {
    api.mockResolvedValue([run()])
    render(<ReportDecision incident={incident} onChanged={() => {}} />)

    expect(await screen.findByRole('button', { name: /Approve High/ })).toBeTruthy()
    expect(screen.getByText(proposal.rationale)).toBeTruthy()
    expect(screen.queryByText('No AI grade.')).toBeNull()
  })

  it('says the AI quota is used up instead of grading by rules', async () => {
    api.mockResolvedValue([outOfQuota])
    render(<ReportDecision incident={incident} onChanged={() => {}} />)

    expect(await screen.findByText('No AI grade.')).toBeTruthy()
    expect(screen.getByText(QUOTA, { exact: false })).toBeTruthy()
    // No level is proposed in the model's place.
    expect(screen.queryByRole('button', { name: /Approve (Low|Moderate|High|Critical)/ })).toBeNull()
  })

  it('lets the coordinator set the level by hand when there is no AI grade', async () => {
    api.mockResolvedValue([outOfQuota])
    const onChanged = vi.fn()
    render(<ReportDecision incident={incident} onChanged={onChanged} />)

    fireEvent.click(await screen.findByRole('button', { name: 'Set the level myself' }))
    fireEvent.click(screen.getByRole('button', { name: 'High' }))

    await waitFor(() => expect(onChanged).toHaveBeenCalled())
    const call = callTo('/api/incidents/i-1/severity')
    expect(call?.[1]?.method).toBe('PATCH')
    expect(JSON.parse(String(call?.[1]?.body))).toEqual({ severity: 'High' })
  })

  it('shows the level in force once the coordinator has set one', async () => {
    api.mockResolvedValue([outOfQuota])
    render(
      <ReportDecision
        incident={{ ...incident, severity: 'High', severityOverridden: true }}
        onChanged={() => {}}
      />,
    )

    expect(await screen.findByText(/Set by you/)).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Change level' })).toBeTruthy()
  })

  it('shows the reason once when trying again fails the same way', async () => {
    api.mockImplementation((path) =>
      String(path).endsWith('/analyse') ? Promise.reject(new ApiError(503, QUOTA)) : Promise.resolve([outOfQuota]),
    )
    const onChanged = vi.fn()
    render(<ReportDecision incident={incident} onChanged={onChanged} />)

    fireEvent.click(await screen.findByRole('button', { name: 'Try again' }))

    // The failed run already says why, so the 503 raises no second banner.
    await waitFor(() => expect(onChanged).toHaveBeenCalled())
    expect(callTo('/api/incidents/i-1/analyse')).toBeTruthy()
    expect(screen.queryByRole('alert')).toBeNull()
    expect(screen.getAllByText(QUOTA, { exact: false })).toHaveLength(1)
  })

  it('still reports a failure that is not about the model', async () => {
    api.mockImplementation((path) =>
      String(path).endsWith('/analyse')
        ? Promise.reject(new ApiError(502, 'Request failed (HTTP 502).'))
        : Promise.resolve([outOfQuota]),
    )
    render(<ReportDecision incident={incident} onChanged={() => {}} />)

    fireEvent.click(await screen.findByRole('button', { name: 'Try again' }))

    expect((await screen.findByRole('alert')).textContent).toBe('Request failed (HTTP 502).')
  })
})
