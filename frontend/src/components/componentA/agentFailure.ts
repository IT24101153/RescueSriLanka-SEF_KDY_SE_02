import { ApiError } from '../../shared/api/client'
import type { AgentRun } from '../../shared/types'

// Component A's agents work with the AI model or not at all: no rule engine
// stands in for it. When the model cannot be used (the quota is used up, no key
// is set, the answer is unreadable) the API answers 503 and records a failed
// run that carries the reason.

/**
 * Runs an agent. A model failure resolves rather than throws: the failed run
 * the panel reloads next already says why, so it needs no second error banner.
 */
export async function runAgent(request: () => Promise<unknown>): Promise<void> {
  try {
    await request()
  } catch (cause) {
    if (!(cause instanceof ApiError && cause.status === 503)) throw cause
  }
}

/** Why a failed run has no result, as a sentence for the coordinator. */
export function failureReason(run: AgentRun | null): string {
  const reason = run?.errorMessage?.trim()
  if (!reason) return 'The AI model did not return a result.'
  return /[.!?]$/.test(reason) ? reason : `${reason}.`
}
