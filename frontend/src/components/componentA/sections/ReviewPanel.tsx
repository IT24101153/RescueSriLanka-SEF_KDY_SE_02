import { useState } from 'react'
import { apiFetch } from '../../../shared/api/client'
import type { Incident, IncidentStatus } from '../types'
import { STATUS_LABEL } from '../severity'

type ReviewPanelProps = {
  incident: Incident
  onChanged: () => void
}

/**
 * Report triage — a different decision from the one in AgentPanel.
 *
 * This panel answers "is this report real, and where is it in its lifecycle?".
 * AgentPanel answers "do I accept the severity the agent proposed?". Keeping
 * them apart matters: a coordinator can verify a genuine report while still
 * rejecting the AI's grading of it.
 */

/** Which transitions are offered from each status, in lifecycle order. */
const NEXT_STATUSES: Record<IncidentStatus, IncidentStatus[]> = {
  Reported: ['Verified', 'Rejected'],
  Verified: ['InProgress', 'Resolved'],
  InProgress: ['Resolved'],
  Resolved: [],
  Rejected: [],
}

/** Rejecting or resolving takes the incident off the live map — worth a prompt. */
const CLOSING: IncidentStatus[] = ['Resolved', 'Rejected']

const INTENT: Record<string, string> = {
  Verified: 'btn-approve',
  InProgress: 'btn-small',
  Resolved: 'btn-small',
  Rejected: 'btn-reject',
}

export default function ReviewPanel({ incident, onChanged }: ReviewPanelProps) {
  const [busy, setBusy] = useState<IncidentStatus | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [confirming, setConfirming] = useState<IncidentStatus | null>(null)

  const options = NEXT_STATUSES[incident.status] ?? []

  async function move(next: IncidentStatus) {
    setBusy(next)
    setError(null)
    try {
      await apiFetch(`/api/incidents/${incident.id}/status`, {
        method: 'PATCH',
        body: JSON.stringify({ status: next }),
      })
      onChanged()
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Could not update the report.')
    } finally {
      setBusy(null)
      setConfirming(null)
    }
  }

  return (
    <section className="review">
      <header className="review__head">
        <h4 className="review__title">Report triage</h4>
        <span className="review__state">
          {STATUS_LABEL[incident.status] ?? incident.status}
        </span>
      </header>

      {error && (
        <p className="agent__error" role="alert">
          {error}
        </p>
      )}

      {options.length === 0 ? (
        <p className="agent__pending">
          This report is closed. Nothing further to decide.
        </p>
      ) : confirming ? (
        <div className="review__confirm">
          <p>
            Mark this report <strong>{STATUS_LABEL[confirming] ?? confirming}</strong>?
            It will be removed from the live map and its safety zone dropped.
          </p>
          <div className="review__actions">
            <button
              type="button"
              className={INTENT[confirming] ?? 'btn-small'}
              disabled={busy !== null}
              onClick={() => move(confirming)}
            >
              {busy ? 'Applying…' : 'Yes, confirm'}
            </button>
            <button
              type="button"
              className="btn-link"
              onClick={() => setConfirming(null)}
              disabled={busy !== null}
            >
              Cancel
            </button>
          </div>
        </div>
      ) : (
        <div className="review__actions">
          {options.map((next) => (
            <button
              key={next}
              type="button"
              className={INTENT[next] ?? 'btn-small'}
              disabled={busy !== null}
              onClick={() =>
                CLOSING.includes(next) ? setConfirming(next) : move(next)
              }
            >
              {busy === next
                ? 'Applying…'
                : `Mark ${STATUS_LABEL[next]?.toLowerCase() ?? next}`}
            </button>
          ))}
        </div>
      )}
    </section>
  )
}
