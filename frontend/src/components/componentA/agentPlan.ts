export type PlanStep = {
  step: number
  agent: string
  action: string
  tools: string[]
  status: 'Pending' | 'Running' | 'Completed' | 'Failed'
  durationMs: number
  detail: string | null
}

export type AgentPlan = { steps: PlanStep[]; notes: string[] }

/** Reads a run's planJson; anything malformed is treated as "no plan". */
export function parsePlan(planJson: string | null): AgentPlan | null {
  if (!planJson) return null
  try {
    const raw = JSON.parse(planJson) as Partial<AgentPlan>
    if (!Array.isArray(raw.steps) || raw.steps.length === 0) return null
    return { steps: raw.steps, notes: Array.isArray(raw.notes) ? raw.notes : [] }
  } catch {
    return null
  }
}
