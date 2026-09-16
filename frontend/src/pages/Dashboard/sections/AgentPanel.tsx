import { useEffect, useState } from 'react'
import { apiFetch, queryString } from '../../../api/client'
import type {
  AgentRun,
  AnalysisProposal,
  Incident,
  IncidentSeverity,
} from '../../../types/incidents'
import { SEVERITY_ORDER } from '../../../types/incidents'
import { SEVERITY_TOKEN, timeAgo } from '../severity'

type AgentPanelProps = {
  incident: Incident
  onChanged: () => void
}

function parseProposal(run: AgentRun | null): AnalysisProposal | null {
  if (!run?.outputJson) return null
  try {
    return JSON.parse(run.outputJson) as AnalysisProposal
  } catch {
    return null
  }
}

/**
 * The human approval gate. The agent proposes; nothing reaches the incident
 * until a coordinator approves, revises or rejects here.
 */
export default function AgentPanel({ incident, onChanged }: AgentPanelProps) {
  const [runs, setRuns] = useState<AgentRun[]>([])
  const [busy, setBusy] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [revision, setRevision] = useState<IncidentSeverity | ''>('')
  const [rejectReason, setRejectReason] = useState('')
  const [rejecting, setRejecting] = useState(false)

  const [reloadToken, setReloadToken] = useState(0)

  useEffect(() => {
    const controller = new AbortController()

    async function load() {
      try {
        const data = await apiFetch<AgentRun[]>(
          `/api/agentruns${queryString({ incidentId: incident.id, take: 5 })}`,
          { signal: controller.signal },
        )
        if (!controller.signal.aborted) setRuns(data)
      } catch (cause) {
        if (!controller.signal.aborted) {
          setError(cause instanceof Error ? cause.message : 'Could not load agent runs.')
        }
      }
    }

    void load()
    return () => controller.abort()
  }, [incident.id, reloadToken])

  const latest = runs[0] ?? null
  const proposal = parseProposal(latest)
  const decided = latest?.approvedAt != null

  async function run(action: string, work: () => Promise<unknown>) {
    setBusy(action)
    setError(null)
    try {
      await work()
      setReloadToken((token) => token + 1)
      onChanged()
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Request failed.')
    } finally {
      setBusy(null)
    }
  }

  return (
    <section className="agent">
      <header className="agent__head">
        <h4 className="agent__title">Incident Analysis Agent</h4>
        <button
          type="button"
          className="btn-small"
          disabled={busy !== null}
          onClick={() =>
            run('analyse', () =>
              apiFetch(`/api/incidents/${incident.id}/analyse`, { method: 'POST' }),
            )
          }
        >
          {busy === 'analyse' ? 'Analysing…' : latest ? 'Re-run analysis' : 'Run analysis'}
        </button>
      </header>

      {error && (
        <p className="agent__error" role="alert">
          {error}
        </p>
      )}

      {!latest && <p className="agent__pending">No analysis has been run for this incident.</p>}

      {latest && proposal && (
        <>
          <div className="agent__meta">
            <span className={`badge badge--${latest.usedFallback ? 'warn' : 'ok'}`}>
              {latest.usedFallback ? 'Rule engine' : latest.model}
            </span>
            <span>{latest.durationMs.toLocaleString()} ms</span>
            <span>{timeAgo(latest.startedAt)}</span>
          </div>

          {latest.usedFallback && latest.errorMessage && (
            <p className="agent__fallback">
              Model unavailable — deterministic rules applied. {latest.errorMessage}
            </p>
          )}

          <div className="proposal">
            <div className="proposal__score">
              <strong>{proposal.severityScore}</strong>
              <span>/100</span>
            </div>
            <div className="proposal__facts">
              <span className={`chip chip--${SEVERITY_TOKEN[proposal.severity]}`}>
                {proposal.severity}
              </span>
              <span className="proposal__detail">
                {proposal.recommendedZoneStatus} zone ·{' '}
                {(proposal.recommendedRadiusMeters / 1000).toFixed(1)} km ·{' '}
                {Math.round(proposal.confidence * 100)}% confidence
              </span>
            </div>
          </div>

          <p className="proposal__rationale">{proposal.rationale}</p>

          {decided ? (
            <p className={`agent__decision agent__decision--${latest.approved ? 'ok' : 'no'}`}>
              {latest.approved
                ? `Approved ${timeAgo(latest.approvedAt!)} — applied to the incident.`
                : `Rejected ${timeAgo(latest.approvedAt!)} — the incident was left unchanged.`}
            </p>
          ) : (
            <div className="agent__actions">
              <div className="agent__row">
                <button
                  type="button"
                  className="btn-approve"
                  disabled={busy !== null}
                  onClick={() =>
                    run('approve', () =>
                      apiFetch(`/api/agentruns/${latest.id}/approve`, {
                        method: 'POST',
                        body: JSON.stringify({ severity: revision || null }),
                      }),
                    )
                  }
                >
                  {busy === 'approve'
                    ? 'Applying…'
                    : revision
                      ? `Approve as ${revision}`
                      : 'Approve'}
                </button>

                <label className="agent__revise">
                  <span>Revise</span>
                  <select
                    value={revision}
                    onChange={(event) =>
                      setRevision(event.target.value as IncidentSeverity | '')
                    }
                  >
                    <option value="">Accept proposal</option>
                    {SEVERITY_ORDER.map((value) => (
                      <option key={value} value={value}>
                        {value}
                      </option>
                    ))}
                  </select>
                </label>
              </div>

              {rejecting ? (
                <div className="agent__row">
                  <input
                    className="agent__reason"
                    placeholder="Why is this being rejected?"
                    value={rejectReason}
                    onChange={(event) => setRejectReason(event.target.value)}
                  />
                  <button
                    type="button"
                    className="btn-reject"
                    disabled={busy !== null || rejectReason.trim().length === 0}
                    onClick={() =>
                      run('reject', () =>
                        apiFetch(`/api/agentruns/${latest.id}/reject`, {
                          method: 'POST',
                          body: JSON.stringify({ reason: rejectReason.trim() }),
                        }),
                      )
                    }
                  >
                    Confirm reject
                  </button>
                </div>
              ) : (
                <button
                  type="button"
                  className="btn-link"
                  onClick={() => setRejecting(true)}
                >
                  Reject proposal
                </button>
              )}
            </div>
          )}
        </>
      )}
    </section>
  )
}
