/**
 * Types shared by every component. Component-specific types live next to
 * their component (e.g. components/componentA/types.ts).
 */

export type AgentRunStatus =
  | 'Running'
  | 'Succeeded'
  | 'SucceededWithFallback'
  | 'Failed'

/** One run of any AI agent, as returned by /api/agentruns. */
export type AgentRun = {
  id: string
  agentName: string
  objective: string
  incidentId: string | null
  status: AgentRunStatus
  model: string | null
  usedFallback: boolean
  approved: boolean
  approvedAt: string | null
  errorMessage: string | null
  durationMs: number
  startedAt: string
  completedAt: string | null
  toolCallsJson: string | null
  outputJson: string | null
}
