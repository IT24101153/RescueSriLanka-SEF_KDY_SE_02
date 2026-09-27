import { useState } from 'react'
import { apiFetch } from '../../../shared/api/client'
import { getSession } from '../../../shared/auth/session'
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

/**
 * Which transitions are offered from each status, in lifecycle order.
 *
 * Rejected still offers Verified: a coordinator can reconsider a mistaken
 * rejection at any time, not just within the same sitting. Resolved is the
 * only true dead end — a closed, legitimate report with nothing left to
 * decide.
 */
const NEXT_STATUSES: Record<IncidentStatus, IncidentStatus[]> = {
  Reported: ['Verified', 'Rejected'],
  Verified: ['InProgress', 'Resolved'],
  InProgress: ['Resolved'],
  Resolved: [],
  Rejected: ['Verified'],
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

  const [deleting, setDeleting] = useState(false)
  const [confirmingDelete, setConfirmingDelete] = useState(false)
  const [deleteError, setDeleteError] = useState<string | null>(null)

  const canDelete = getSession()?.user.role === 'EmergencyCoordinator'
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

  /** Permanently removes the report — a duplicate, spam, or test entry that
   * should not exist on record at all, rather than just be marked Rejected. */
  async function remove() {
    setDeleting(true)
    setDeleteError(null)
    try {
      await apiFetch(`/api/incidents/${incident.id}`, { method: 'DELETE' })
      onChanged()
    } catch (cause) {
      setDeleteError(cause instanceof Error ? cause.message : 'Could not delete the report.')
      setDeleting(false)
      setConfirmingDelete(false)
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

      {canDelete && (
        <div className="review__danger">
          {deleteError && (
            <p className="agent__error" role="alert">
              {deleteError}
            </p>
          )}

          {confirmingDelete ? (
            <div className="review__confirm">
              <p>
                Delete this report permanently? This removes it, its photos, and
                its safety zone for good — it will not appear as Rejected on
                record, it will simply be gone. This cannot be undone.
              </p>
              <div className="review__actions">
                <button
                  type="button"
                  className="btn-reject"
                  disabled={deleting}
                  onClick={() => void remove()}
                >
                  {deleting ? 'Deleting…' : 'Yes, delete permanently'}
                </button>
                <button
                  type="button"
                  className="btn-link"
                  onClick={() => setConfirmingDelete(false)}
                  disabled={deleting}
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
