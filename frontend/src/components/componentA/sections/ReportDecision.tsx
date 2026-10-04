import { useEffect, useState } from 'react'
import { apiFetch, queryString } from '../../../shared/api/client'
import { getSession } from '../../../shared/auth/session'
import type { AgentRun } from '../../../shared/types'
import type { AnalysisProposal, Incident, IncidentSeverity } from '../types'
import { AGENTS, SEVERITY_ORDER } from '../types'
import { SEVERITY_TOKEN } from '../severity'

type ReportDecisionProps = {
  incident: Incident
  onChanged: () => void
}

/** How often to look for the agent's result while it is still working. */
const POLL_MS = 4000

/** Give up polling after about two minutes; a manual run is offered then. */
const MAX_POLLS = 30

function parseProposal(run: AgentRun | null): AnalysisProposal | null {
  if (!run?.outputJson) return null
  try {
    return JSON.parse(run.outputJson) as AnalysisProposal
  } catch {
    return null
  }
}

/**
 * The coordinator's whole job on a report, in two questions:
 *
 *   1. Is the report true?  → approve (Verified) or reject (Rejected).
 *   2. Is the AI's condition right?  The Incident Analysis Agent runs by
 *      itself when a report arrives; approve its level, or say which level
 *      it really is.
 *
 * The two stay separate on purpose — a report can be true while the AI has
 * graded it wrong.
 */
export default function ReportDecision({ incident, onChanged }: ReportDecisionProps) {
  const [runs, setRuns] = useState<AgentRun[]>([])
  const [runsLoaded, setRunsLoaded] = useState(false)
  const [polls, setPolls] = useState(0)
  const [reloadToken, setReloadToken] = useState(0)

  const [busy, setBusy] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [confirmingReject, setConfirmingReject] = useState(false)
  const [confirmingResolve, setConfirmingResolve] = useState(false)
  const [confirmingDelete, setConfirmingDelete] = useState(false)
  const [pickingLevel, setPickingLevel] = useState(false)

  const canDelete = getSession()?.user.role === 'EmergencyCoordinator'

  const latest = runs[0] ?? null
  const proposal = parseProposal(latest)
  const agentWorking = !latest || latest.status === 'Running'
  // Decided once the proposal is approved, or — for one turned down under the
  // old flow — once a coordinator has set the level themselves.
  const conditionDecided =
    latest?.approvedAt != null && (latest.approved || incident.severityOverridden)

  // A level already stands when an earlier AI result was approved or a
  // coordinator set one — a re-analysis then proposes a change to it.
  const levelInForce =
    runs.slice(1).some((run) => run.approved) || incident.severityOverridden
      ? incident.severity
      : null

  // Until the coordinator decides, show what the AI proposes; afterwards,
  // the level actually applied to the report and its safety zone.
  const shownLevel = conditionDecided ? incident.severity : (proposal?.severity ?? incident.severity)

  // A different report starts from a clean slate.
  useEffect(() => {
    setRuns([])
    setRunsLoaded(false)
    setPolls(0)
    setError(null)
    setConfirmingReject(false)
    setConfirmingResolve(false)
    setConfirmingDelete(false)
    setPickingLevel(false)
  }, [incident.id])

  useEffect(() => {
    const controller = new AbortController()

    async function load() {
      try {
        const data = await apiFetch<AgentRun[]>(
          `/api/agentruns${queryString({ incidentId: incident.id, agentName: AGENTS.analysis, take: 5 })}`,
          { signal: controller.signal },
        )
        if (controller.signal.aborted) return
        setRuns(data)
        setRunsLoaded(true)
      } catch (cause) {
        if (controller.signal.aborted) return
        setRunsLoaded(true)
        setError(cause instanceof Error ? cause.message : 'Could not load the AI result.')
      }
    }

    void load()
    return () => controller.abort()
  }, [incident.id, reloadToken])

  // The agent runs in the background after a report arrives, so keep looking
  // for its result until it lands rather than asking the coordinator to.
  useEffect(() => {
    if (!runsLoaded || !agentWorking || polls >= MAX_POLLS) return
    if (incident.status === 'Rejected') return

    const timer = window.setTimeout(() => {
      setPolls((count) => count + 1)
      setReloadToken((token) => token + 1)
    }, POLL_MS)
    return () => window.clearTimeout(timer)
  }, [runsLoaded, agentWorking, polls, incident.status])

  async function act(action: string, work: () => Promise<unknown>) {
    setBusy(action)
    setError(null)
    try {
      await work()
      setConfirmingReject(false)
      setConfirmingResolve(false)
      setPickingLevel(false)
      setReloadToken((token) => token + 1)
      onChanged()
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Request failed.')
    } finally {
      setBusy(null)
    }
  }

  function setStatus(status: 'Verified' | 'Rejected' | 'Resolved') {
    return act(status, () =>
      apiFetch(`/api/incidents/${incident.id}/status`, {
        method: 'PATCH',
        body: JSON.stringify({ status }),
      }),
    )
  }

  /** Accepts the AI's level, or — with [level] — applies the coordinator's. */
  function setCondition(level: IncidentSeverity | null) {
    // Before the proposal is decided, approving it (with or without a new
    // level) also adopts its zone radius. Afterwards, only the level changes.
    if (latest && proposal && latest.approvedAt == null) {
      return act(level ?? 'approve', () =>
        apiFetch(`/api/agentruns/${latest.id}/approve`, {
          method: 'POST',
          body: JSON.stringify({ severity: level }),
        }),
      )
    }
    // A proposal turned down under the old review flow can still be adopted.
    const chosen = level ?? proposal?.severity
    if (!chosen) return
    return act(level ?? 'approve', () =>
      apiFetch(`/api/incidents/${incident.id}/severity`, {
        method: 'PATCH',
        body: JSON.stringify({ severity: chosen }),
      }),
    )
  }

  function runAnalysis() {
    setPolls(0)
    return act('analyse', () =>
      apiFetch(`/api/incidents/${incident.id}/analyse`, { method: 'POST' }),
    )
  }

  async function remove() {
    setBusy('delete')
    setError(null)
    try {
      await apiFetch(`/api/incidents/${incident.id}`, { method: 'DELETE' })
      onChanged()
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Could not delete the report.')
      setBusy(null)
      setConfirmingDelete(false)
    }
  }

  const status = incident.status
  const approved = status === 'Verified' || status === 'InProgress' || status === 'Resolved'
  const rejected = status === 'Rejected'
  const merged = status === 'Merged'

  return (
    <section className="decide" aria-label="Report decision">
      {error && (
        <p className="agent__error" role="alert">
          {error}
        </p>
      )}

      {/* ---------------------------------------------- 1. true or false */}
      <div className="decide__step">
        <header className="decide__head">
          <span className={`decide__num${approved || rejected || merged ? ' is-done' : ''}`}>
            {approved || rejected || merged ? '✓' : '1'}
          </span>
          <h4 className="decide__title">Is this report true?</h4>
        </header>

        {status === 'Reported' && !confirmingReject && (
          <div className="decide__actions">
            <button
              type="button"
              className="btn-approve"
              disabled={busy !== null}
              onClick={() => void setStatus('Verified')}
            >
              {busy === 'Verified' ? 'Approving…' : '✓ True — approve'}
            </button>
            <button
              type="button"
              className="btn-reject"
              disabled={busy !== null}
              onClick={() => setConfirmingReject(true)}
            >
              ✗ False — reject
            </button>
          </div>
        )}

        {status === 'Reported' && confirmingReject && (
          <div className="decide__confirm">
            <p>Reject this report as false? It comes off the live map and the reporter sees it was not confirmed.</p>
            <div className="decide__actions">
              <button
                type="button"
                className="btn-reject"
                disabled={busy !== null}
                onClick={() => void setStatus('Rejected')}
              >
                {busy === 'Rejected' ? 'Rejecting…' : 'Yes, reject'}
              </button>
              <button
                type="button"
                className="btn-link"
                disabled={busy !== null}
                onClick={() => setConfirmingReject(false)}
              >
                Cancel
              </button>
            </div>
          </div>
        )}

        {approved && (
          <>
            <p className="agent__decision agent__decision--ok">
              {status === 'Resolved'
                ? 'Approved as true · now resolved and off the live map.'
                : 'Approved as true · on the live map.'}
            </p>
            {status !== 'Resolved' &&
              (confirmingResolve ? (
                <div className="decide__confirm">
                  <p>Mark it resolved? It comes off the live map and its safety zone is dropped.</p>
                  <div className="decide__actions">
                    <button
                      type="button"
                      className="btn-small"
                      disabled={busy !== null}
                      onClick={() => void setStatus('Resolved')}
                    >
                      {busy === 'Resolved' ? 'Resolving…' : 'Yes, resolved'}
                    </button>
                    <button
                      type="button"
                      className="btn-link"
                      onClick={() => setConfirmingResolve(false)}
                    >
                      Cancel
                    </button>
                  </div>
                </div>
              ) : (
                <button
                  type="button"
                  className="btn-link decide__minor"
                  onClick={() => setConfirmingResolve(true)}
                >
                  Situation over? Mark resolved
                </button>
              ))}
          </>
        )}

        {rejected && (
          <>
            <p className="agent__decision agent__decision--no">
              Rejected as false · not on the live map.
            </p>
            {/* Rejection is never final: new evidence can turn up later, and
                approving brings the report back to the map with its zone. */}
            <div className="decide__confirm">
              <p>Rejected by mistake, or new evidence came in?</p>
              <div className="decide__actions">
                <button
                  type="button"
                  className="btn-approve"
                  disabled={busy !== null}
                  onClick={() => void setStatus('Verified')}
                >
                  {busy === 'Verified' ? 'Approving…' : '↺ Undo — approve as true'}
                </button>
              </div>
            </div>
          </>
        )}
      </div>

      {merged && (
        <p className="agent__decision agent__decision--no">
          Merged as a duplicate · off the live map. The report it was merged into carries the decision.
        </p>
      )}

      {/* ---------------------------------------------- 2. condition */}
      {!rejected && !merged && (
        <div className="decide__step">
          <header className="decide__head">
            <span className={`decide__num${conditionDecided ? ' is-done' : ''}`}>
              {conditionDecided ? '✓' : '2'}
            </span>
            <h4 className="decide__title">Condition</h4>
            {!agentWorking && proposal ? (
              // Worth running again when the situation changes or the reporter
              // adds more: the new result is a proposal, not applied until
              // approved, so the map never moves on the AI's word alone.
              <button
                type="button"
                className="btn-small decide__reanalyse"
                disabled={busy !== null}
                title="Ask the AI agent to look at this report again"
                onClick={() => void runAnalysis()}
              >
                {busy === 'analyse' ? 'Re-analysing…' : '↻ Re-analyse'}
              </button>
            ) : (
              <span className="decide__by">set by the AI agent</span>
            )}
          </header>

          {agentWorking && polls < MAX_POLLS && (
            <p className="decide__working">
              <span className="decide__spinner" aria-hidden="true" />
              The AI agent is checking this report’s condition…
            </p>
          )}

          {agentWorking && polls >= MAX_POLLS && (
            <div className="decide__actions">
              <p className="agent__pending">The AI result has not arrived.</p>
              <button
                type="button"
                className="btn-small"
                disabled={busy !== null}
                onClick={() => void runAnalysis()}
              >
                {busy === 'analyse' ? 'Analysing…' : 'Run AI check'}
              </button>
            </div>
          )}

          {!agentWorking && !proposal && (
            <div className="decide__actions">
              <p className="agent__pending">The AI could not grade this report.</p>
              <button
                type="button"
                className="btn-small"
                disabled={busy !== null}
                onClick={() => void runAnalysis()}
              >
                {busy === 'analyse' ? 'Analysing…' : 'Try again'}
              </button>
            </div>
          )}

          {!agentWorking && proposal && (
            <>
              <div className="decide__condition">
                <span className={`chip chip--${SEVERITY_TOKEN[shownLevel]} decide__level`}>
                  {shownLevel}
                </span>
                <span className="decide__detail">
                  {conditionDecided
                    ? incident.severityOverridden
                      ? `Set by you · the AI said ${proposal.severity}`
                      : 'Approved · safety zone set to match'
                    : `AI score ${proposal.severityScore}/100 · ${Math.round(proposal.confidence * 100)}% sure`}
                </span>
              </div>

              {!conditionDecided && levelInForce && levelInForce !== proposal.severity && (
                <p className="agent__fallback">
                  New AI result. The map still shows <strong>{levelInForce}</strong> until
                  you approve or correct this.
                </p>
              )}

              {!conditionDecided && (
                <p className="proposal__rationale">{proposal.rationale}</p>
              )}

              {pickingLevel ? (
                <div className="decide__confirm">
                  <p>What condition level is it?</p>
                  <div className="decide__levels">
                    {SEVERITY_ORDER.map((level) => (
                      <button
                        key={level}
                        type="button"
                        className={`level-btn level-btn--${SEVERITY_TOKEN[level]}`}
                        disabled={busy !== null || (conditionDecided && level === incident.severity)}
                        onClick={() => void setCondition(level)}
                      >
                        {busy === level ? 'Saving…' : level}
                      </button>
                    ))}
                  </div>
                  <button
                    type="button"
                    className="btn-link decide__minor"
                    onClick={() => setPickingLevel(false)}
                  >
                    Cancel
                  </button>
                </div>
              ) : conditionDecided ? (
                <button
                  type="button"
                  className="btn-link decide__minor"
                  onClick={() => setPickingLevel(true)}
                >
                  Change level
                </button>
              ) : (
                <div className="decide__actions">
                  <button
                    type="button"
                    className="btn-approve"
                    disabled={busy !== null}
                    onClick={() => void setCondition(null)}
                  >
                    {busy === 'approve' ? 'Approving…' : `✓ Approve ${proposal.severity}`}
                  </button>
                  <button
                    type="button"
                    className="btn-small"
                    disabled={busy !== null}
                    onClick={() => setPickingLevel(true)}
                  >
                    Wrong level
                  </button>
                </div>
              )}
            </>
          )}
        </div>
      )}

      {canDelete && (
        <div className="review__danger">
          {confirmingDelete ? (
            <div className="decide__confirm">
              <p>
                Delete this report permanently? Its photos and safety zone go
                too. Use this only for spam, duplicates or tests.
              </p>
              <div className="decide__actions">
                <button
                  type="button"
                  className="btn-reject"
                  disabled={busy !== null}
                  onClick={() => void remove()}
                >
                  {busy === 'delete' ? 'Deleting…' : 'Yes, delete'}
                </button>
                <button
                  type="button"
                  className="btn-link"
                  onClick={() => setConfirmingDelete(false)}
                >
                  Cancel
                </button>
              </div>
            </div>
          ) : (
            <button
              type="button"
              className="btn-link review__delete"
              onClick={() => setConfirmingDelete(true)}
            >
              Delete report
            </button>
          )}
        </div>
      )}
    </section>
  )
}
