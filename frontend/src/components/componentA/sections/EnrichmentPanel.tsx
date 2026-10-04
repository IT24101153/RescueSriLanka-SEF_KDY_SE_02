import { useEffect, useState } from 'react'
import { apiFetch, queryString } from '../../../shared/api/client'
import type { AgentRun } from '../../../shared/types'
import { AGENTS, type EnrichmentField, type EnrichmentProposal, type Incident } from '../types'

type EnrichmentPanelProps = {
  incident: Incident
  onChanged: () => void
}

const POLL_MS = 4000
/** The check runs after the severity grading, so allow it a little longer. */
const MAX_POLLS = 40

const FIELD_LABEL: Record<EnrichmentField, string> = {
  title: 'Title',
  type: 'Type',
  district: 'District',
  estimatedAffectedPeople: 'People affected',
  addressText: 'Landmark',
}

function parse(run: AgentRun | null): EnrichmentProposal | null {
  if (!run?.outputJson) return null
  try {
    const raw = JSON.parse(run.outputJson) as Partial<EnrichmentProposal>
    return {
      suggestions: raw.suggestions ?? [],
      duplicate: raw.duplicate ?? null,
      summary: raw.summary ?? '',
      usedFallback: raw.usedFallback ?? false,
    }
  } catch {
    return null
  }
}

/**
 * Keyed by incident in the drawer, so a different report starts from a clean
 * slate.
 *
 * The Incident Enrichment Agent's proposal for this report: fields it would
 * fill or correct, and another report it looks like a duplicate of. Nothing
 * applies until the coordinator ticks what they agree with and approves.
 */
export default function EnrichmentPanel({ incident, onChanged }: EnrichmentPanelProps) {
  const [run, setRun] = useState<AgentRun | null>(null)
  const [loaded, setLoaded] = useState(false)
  const [polls, setPolls] = useState(0)
  const [reloadToken, setReloadToken] = useState(0)

  const [chosen, setChosen] = useState<Set<EnrichmentField>>(new Set())
  const [merge, setMerge] = useState(false)
  const [busy, setBusy] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const proposal = parse(run)
  const pending = run?.decision === 'Pending' && run.status !== 'Running' && run.status !== 'Failed'
  const working = !run || run.status === 'Running'
  const merged = incident.status === 'Merged'

  useEffect(() => {
    const controller = new AbortController()
    apiFetch<AgentRun[]>(
      `/api/agentruns${queryString({ incidentId: incident.id, agentName: AGENTS.enrichment, take: 1 })}`,
      { signal: controller.signal },
    )
      .then((runs) => {
        if (controller.signal.aborted) return
        const latest = runs[0] ?? null
        setRun(latest)
        setLoaded(true)
        // Every proposal starts fully ticked: the agent's view, ready to accept.
        const next = parse(latest)
        setChosen(new Set(next?.suggestions.map((s) => s.field) ?? []))
        setMerge(next?.duplicate != null)
      })
      .catch((cause) => {
        if (controller.signal.aborted) return
        setLoaded(true)
        setError(cause instanceof Error ? cause.message : 'Could not load the enrichment check.')
      })
    return () => controller.abort()
  }, [incident.id, reloadToken])

  // The check runs in the background after a report arrives; keep looking.
  useEffect(() => {
    if (!loaded || !working || polls >= MAX_POLLS || merged) return
    const timer = window.setTimeout(() => {
      setPolls((count) => count + 1)
      setReloadToken((token) => token + 1)
    }, POLL_MS)
    return () => window.clearTimeout(timer)
  }, [loaded, working, polls, merged])

  async function act(action: string, work: () => Promise<unknown>) {
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

  const runCheck = () => {
    setPolls(0)
    return act('check', () => apiFetch(`/api/incidents/${incident.id}/enrich`, { method: 'POST' }))
  }

  const approve = () =>
    act('approve', () =>
      apiFetch(`/api/agentruns/${run!.id}/approve`, {
        method: 'POST',
        body: JSON.stringify({ fields: [...chosen], mergeDuplicate: merge }),
      }),
    )

  const dismiss = () =>
    act('dismiss', () =>
      apiFetch(`/api/agentruns/${run!.id}/reject`, {
        method: 'POST',
        body: JSON.stringify({ reason: 'Dismissed by the coordinator.' }),
      }),
    )

  function toggle(field: EnrichmentField) {
    setChosen((current) => {
      const next = new Set(current)
      if (next.has(field)) next.delete(field)
      else next.add(field)
      return next
    })
  }

  const nothingProposed = proposal !== null && proposal.suggestions.length === 0 && proposal.duplicate === null
  const nothingChosen = chosen.size === 0 && !merge

  return (
    <section className="enrich" aria-label="Record check">
      <header className="decide__head">
        <span className={`decide__num${run && !pending && !working ? ' is-done' : ''}`}>
          {run && !pending && !working ? '✓' : '3'}
        </span>
        <h4 className="decide__title">Record check</h4>
        {!working && !merged ? (
          <button
            type="button"
            className="btn-small decide__reanalyse"
            disabled={busy !== null}
            title="Ask the enrichment agent to check this report again"
            onClick={() => void runCheck()}
          >
            {busy === 'check' ? 'Checking…' : '↻ Re-check'}
          </button>
        ) : (
          <span className="decide__by">duplicates &amp; missing details</span>
        )}
      </header>

      {error && (
        <p className="agent__error" role="alert">
          {error}
        </p>
      )}

      {merged && (
        <p className="agent__decision agent__decision--no">
          Merged into another report of the same event — that report now carries its photos and head-count.
        </p>
      )}

      {!merged && working && polls < MAX_POLLS && (
        <p className="decide__working">
          <span className="decide__spinner" aria-hidden="true" />
          The enrichment agent is checking for duplicates and missing details…
        </p>
      )}

      {!merged && working && polls >= MAX_POLLS && (
        <div className="decide__actions">
          <p className="agent__pending">No record check yet.</p>
          <button type="button" className="btn-small" disabled={busy !== null} onClick={() => void runCheck()}>
            {busy === 'check' ? 'Checking…' : 'Run record check'}
          </button>
        </div>
      )}

      {!merged && run?.status === 'Failed' && (
        <div className="decide__actions">
          <p className="agent__pending">The record check failed: {run.errorMessage ?? 'no reason recorded'}.</p>
          <button type="button" className="btn-small" disabled={busy !== null} onClick={() => void runCheck()}>
            Try again
          </button>
        </div>
      )}

      {!merged && proposal && run && run.status !== 'Failed' && (
        <>
          <p className="enrich__summary">
            {proposal.summary}
            {proposal.usedFallback && <span className="tag">rule engine</span>}
          </p>

          {nothingProposed && <p className="agent__pending">Nothing to correct, and no duplicate found.</p>}

          {!pending && !nothingProposed && (
            <p className={`agent__decision agent__decision--${run.decision === 'Rejected' ? 'no' : 'ok'}`}>
              {run.decision === 'Rejected'
                ? 'Dismissed — the report was left as it was.'
                : run.decision === 'Revised'
                  ? 'Applied in part.'
                  : 'Applied.'}
            </p>
          )}

          {proposal.suggestions.length > 0 && (
            <ul className="enrich__list">
              {proposal.suggestions.map((suggestion) => (
                <li key={suggestion.field} className="enrich__item">
                  <label>
                    <input
                      type="checkbox"
                      checked={chosen.has(suggestion.field)}
                      disabled={!pending || busy !== null}
                      onChange={() => toggle(suggestion.field)}
                    />
                    <span className="enrich__field">{FIELD_LABEL[suggestion.field]}</span>
                    <span className="enrich__change">
                      <s>{suggestion.current || 'empty'}</s> → <strong>{suggestion.proposed}</strong>
                    </span>
                  </label>
                  <p className="enrich__reason">{suggestion.reason}</p>
                </li>
              ))}
            </ul>
          )}

          {proposal.duplicate && (
            <div className="enrich__duplicate">
              <label>
                <input
                  type="checkbox"
                  checked={merge}
                  disabled={!pending || busy !== null}
                  onChange={(event) => setMerge(event.target.checked)}
                />
                <span>
                  <strong>Likely duplicate</strong> of “{proposal.duplicate.title}” —{' '}
                  {proposal.duplicate.distanceKm.toFixed(2)} km away,{' '}
                  {Math.round(proposal.duplicate.similarity * 100)}% word overlap.
                </span>
              </label>
              <p className="enrich__reason">
                {proposal.duplicate.reason} Merging takes this report off the map and moves its photos and
                head-count to the earlier one.
              </p>
            </div>
          )}

          {pending && !nothingProposed && (
            <div className="decide__actions">
              <button
                type="button"
                className="btn-approve"
                disabled={busy !== null || nothingChosen}
                onClick={() => void approve()}
              >
                {busy === 'approve' ? 'Applying…' : merge ? '✓ Apply & merge' : '✓ Apply selected'}
              </button>
              <button type="button" className="btn-small" disabled={busy !== null} onClick={() => void dismiss()}>
                {busy === 'dismiss' ? 'Dismissing…' : 'Dismiss'}
              </button>
            </div>
          )}
        </>
      )}
    </section>
  )
}
