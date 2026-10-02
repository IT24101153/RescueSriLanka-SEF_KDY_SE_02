import { beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import AgentActivity from './AgentActivity'
import { apiFetch } from '../../../shared/api/client'
import { parsePlan } from '../agentPlan'
import type { AgentRun } from '../../../shared/types'

vi.mock('../../../shared/api/client', () => ({ apiFetch: vi.fn() }))
const api = vi.mocked(apiFetch)

const planJson = JSON.stringify({
  steps: [
    { step: 1, agent: 'EvidenceGatheringAgent', action: 'Gather evidence', tools: ['count_nearby_active_incidents'], status: 'Completed', durationMs: 12, detail: '0 nearby' },
    { step: 2, agent: 'SeverityAnalysisAgent', action: 'Classify severity', tools: [], status: 'Completed', durationMs: 900, detail: 'gemini: High, 2 attempts' },
    { step: 3, agent: 'ProposalValidationAgent', action: 'Validate', tools: [], status: 'Failed', durationMs: 1, detail: 'boom' },
  ],
  notes: ['Rainfall lookup skipped: not relevant to a Fire report.'],
})

const run: AgentRun = {
  id: 'r-1', agentName: 'IncidentAnalysisAgent', objective: 'Classify', incidentId: 'i-1',
  status: 'Succeeded', model: 'gemini', usedFallback: false, approved: false, approvedAt: null,
  decision: 'Pending', decisionNote: null, modelAttempts: 2, errorMessage: null, durationMs: 913,
  startedAt: '2026-09-30T08:00:00Z', completedAt: '2026-09-30T08:00:01Z',
  planJson, toolCallsJson: null, outputJson: null,
}

describe('Component A agent monitoring', () => {
  beforeEach(() => { cleanup(); api.mockReset() })

  it('shows the plan, the agent each step was delegated to, and skipped tools', async () => {
    api.mockResolvedValue([run])
    render(<AgentActivity />)

    expect(await screen.findByText('3 steps')).toBeTruthy()
    expect(screen.getByText('EvidenceGatheringAgent')).toBeTruthy()
    expect(screen.getByText('SeverityAnalysisAgent')).toBeTruthy()
    expect(screen.getByText('ProposalValidationAgent')).toBeTruthy()
    expect(screen.getByText(/Rainfall lookup skipped/)).toBeTruthy()
    expect(screen.getByText('Model needed 2 attempts.')).toBeTruthy()
  })

  it('shows a rejection as Rejected with the coordinator\'s reason, not as an error', async () => {
    api.mockResolvedValue([{ ...run, decision: 'Rejected', approvedAt: '2026-09-30T09:00:00Z', decisionNote: 'Duplicate report.' }])
    render(<AgentActivity />)

    expect(await screen.findByText('Rejected')).toBeTruthy()
    expect(screen.getByText('Duplicate report.')).toBeTruthy()
  })

  it('shows an empty state when there are no runs, and an error when loading fails', async () => {
    api.mockResolvedValueOnce([])
    render(<AgentActivity />)
    expect(await screen.findByText('No agent runs recorded yet.')).toBeTruthy()

    cleanup()
    api.mockRejectedValueOnce(new Error('API down'))
    render(<AgentActivity />)
    expect(await screen.findByText('API down')).toBeTruthy()
  })

  it('treats a missing or malformed plan as no plan', () => {
    expect(parsePlan(null)).toBeNull()
    expect(parsePlan('not json')).toBeNull()
    expect(parsePlan('{"steps":[]}')).toBeNull()
  })
})
