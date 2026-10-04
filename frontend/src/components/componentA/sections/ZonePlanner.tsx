import { useEffect, useState } from 'react'
import { apiFetch, queryString } from '../../../shared/api/client'
import type { AgentRun } from '../../../shared/types'
import { AGENTS, type SafetyZoneInput, type ZonePlan, type ZoneProposal, type ZoneStatus } from '../types'
import { ZONE_TOKEN, timeAgo } from '../severity'
import { parsePlan } from '../agentPlan'

type ZonePlannerProps = {
  /** Draws the pending plan on the zone map, so it is judged by sight. */
  onPreview: (zones: ZoneProposal[]) => void
  onChanged: () => void
}

/** One proposed new zone, as the coordinator is editing it. */
type Draft = ZoneProposal & { include: boolean; edited: boolean }

function parse(run: AgentRun | null): ZonePlan | null {
  if (!run?.outputJson) return null
  try {
    const raw = JSON.parse(run.outputJson) as Partial<ZonePlan>
    return { zones: raw.zones ?? [], summary: raw.summary ?? '', usedFallback: raw.usedFallback ?? false }
  } catch {
    return null
  }
}

/**
 * The Zone Planning Agent. It looks above single incidents — clusters that are
 * really one event, and manual zones whose hazard has passed — and proposes a
 * plan. The coordinator edits any zone, unticks what they disagree with, and
 * approves; only then does anything reach the public map.
 */
export default function ZonePlanner({ onPreview, onChanged }: ZonePlannerProps) {
  const [run, setRun] = useState<AgentRun | null>(null)
  const [drafts, setDrafts] = useState<Draft[]>([])
  const [retire, setRetire] = useState<Set<string>>(new Set())
  const [reloadToken, setReloadToken] = useState(0)
  const [busy, setBusy] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const plan = parse(run)
  const pending = run?.decision === 'Pending' && run.status !== 'Failed' && run.status !== 'Running'
  const retires = plan?.zones.filter((zone) => zone.action === 'retire') ?? []

  useEffect(() => {
    const controller = new AbortController()
    apiFetch<AgentRun[]>(`/api/agentruns${queryString({ agentName: AGENTS.zonePlanning, take: 1 })}`, {
      signal: controller.signal,
    })
      .then((runs) => {
        if (controller.signal.aborted) return
        const latest = runs[0] ?? null
        const next = parse(latest)
        setRun(latest)
        setDrafts(
          (next?.zones ?? [])
            .filter((zone) => zone.action === 'create')
            .map((zone) => ({ ...zone, include: true, edited: false })),
        )
        setRetire(new Set((next?.zones ?? []).filter((z) => z.action === 'retire').map((z) => z.zoneId!)))
      })
      .catch((cause) => {
        if (!controller.signal.aborted) setError(cause instanceof Error ? cause.message : 'Could not load the zone plan.')
      })
    return () => controller.abort()
  }, [reloadToken])

  // Preview what would be drawn: the ticked new zones of a plan still awaiting a decision.
  useEffect(() => {
    onPreview(pending ? drafts.filter((draft) => draft.include) : [])
  }, [drafts, pending, onPreview])

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

  function edit(index: number, change: Partial<ZoneProposal>) {
    setDrafts((current) =>
      current.map((draft, i) => (i === index ? { ...draft, ...change, edited: true } : draft)),
    )
  }

  function toggleDraft(index: number) {
    setDrafts((current) => current.map((draft, i) => (i === index ? { ...draft, include: !draft.include } : draft)))
  }

  function toggleRetire(id: string) {
    setRetire((current) => {
      const next = new Set(current)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  const runPlanner = () => act('plan', () => apiFetch('/api/safetyzones/plan', { method: 'POST' }))

  function approve() {
    const unchanged =
      drafts.every((draft) => draft.include && !draft.edited) && retire.size === retires.length

    // Untouched, the plan is approved as proposed; otherwise the coordinator's
    // version is sent and the run records a revision.
    const body = unchanged
      ? {}
      : {
          zones: drafts
            .filter((draft) => draft.include)
            .map<SafetyZoneInput>((draft) => ({
              name: draft.name,
              status: draft.status,
              centerLatitude: draft.centerLatitude,
              centerLongitude: draft.centerLongitude,
              radiusMeters: draft.radiusMeters,
              district: draft.district,
              rationale: draft.rationale,
              expiresAt: draft.expiresInHours
                ? new Date(Date.now() + draft.expiresInHours * 3600000).toISOString()
                : null,
            })),
          retireZoneIds: [...retire],
        }

    return act('approve', () =>
      apiFetch(`/api/agentruns/${run!.id}/approve`, { method: 'POST', body: JSON.stringify(body) }),
    )
  }

  const reject = () =>
    act('reject', () =>
      apiFetch(`/api/agentruns/${run!.id}/reject`, {
        method: 'POST',
        body: JSON.stringify({ reason: 'Plan not adopted by the coordinator.' }),
      }),
    )

  const steps = parsePlan(run?.planJson ?? null)
  const chosenCount = drafts.filter((draft) => draft.include).length + retire.size

  return (
    <section className="panel planner" aria-label="Zone planning agent">
      <header className="panel__head">
        <div>
          <h2 className="panel__title">Zone planning agent</h2>
          <p className="planner__sub">
            Groups approved incidents into area zones and retires stale manual zones. Proposes only — you approve.
          </p>
        </div>
        <button type="button" className="btn-approve" disabled={busy !== null} onClick={() => void runPlanner()}>
          {busy === 'plan' ? 'Planning…' : run ? '↻ Plan again' : 'Plan zones'}
        </button>
      </header>

      <div className="planner__body">
        {error && (
          <p className="agent__error" role="alert">
            {error}
          </p>
        )}

        {!run && !error && <p className="empty">No plan yet. Run the agent to review the zone layer.</p>}

        {run?.status === 'Failed' && (
          <p className="agent__error">The last plan failed: {run.errorMessage ?? 'no reason recorded'}.</p>
        )}

        {run && plan && (
          <>
            <p className="planner__meta">
              {timeAgo(run.startedAt)} · {run.model ?? '—'}
              {plan.usedFallback && <span className="tag">rule engine</span>}
              {steps && ` · ${steps.steps.length} steps`}
              {!pending && (
                <span className={`badge badge--${run.decision === 'Rejected' ? 'bad' : 'ok'}`}>
                  {run.decision === 'Rejected' ? 'Not adopted' : run.decision}
                </span>
              )}
            </p>
            <p className="enrich__summary">{plan.summary}</p>
            {steps?.notes.map((note) => (
              <p key={note} className="plan__note">
                {note}
              </p>
            ))}

            {drafts.length > 0 && (
              <ul className="planner__list">
                {drafts.map((draft, index) => (
                  <li key={`${draft.name}-${index}`} className={`planner__item${draft.include ? '' : ' is-off'}`}>
                    <label className="planner__check">
                      <input
                        type="checkbox"
                        checked={draft.include}
                        disabled={!pending || busy !== null}
                        onChange={() => toggleDraft(index)}
                      />
                      <span className="tag">new</span>
                    </label>
                    <div className="planner__fields">
                      <input
                        className="planner__name"
                        value={draft.name}
                        disabled={!pending}
                        onChange={(event) => edit(index, { name: event.target.value })}
                        aria-label="Zone name"
                      />
                      <div className="planner__row">
                        <select
                          value={draft.status}
                          disabled={!pending}
                          onChange={(event) => edit(index, { status: event.target.value as ZoneStatus })}
                          aria-label="Zone status"
                        >
                          {(['Danger', 'Caution', 'Safe'] as const).map((status) => (
                            <option key={status} value={status}>
                              {status}
                            </option>
                          ))}
                        </select>
                        <label>
                          Radius
                          <input
                            type="number"
                            min={100}
                            max={20000}
                            step={100}
                            value={draft.radiusMeters}
                            disabled={!pending}
                            onChange={(event) => edit(index, { radiusMeters: Number(event.target.value) })}
                          />
                          m
                        </label>
                        <label>
                          Expires in
                          <input
                            type="number"
                            min={1}
                            max={168}
                            value={draft.expiresInHours ?? 48}
                            disabled={!pending}
                            onChange={(event) => edit(index, { expiresInHours: Number(event.target.value) })}
                          />
                          h
                        </label>
                      </div>
                      <p className="enrich__reason">
                        {draft.rationale} Based on {draft.basedOnIncidentIds.length} incident(s).
                      </p>
                    </div>
                  </li>
                ))}
              </ul>
            )}

            {retires.length > 0 && (
              <ul className="planner__list">
                {retires.map((zone) => (
                  <li key={zone.zoneId} className={`planner__item${retire.has(zone.zoneId!) ? '' : ' is-off'}`}>
                    <label className="planner__check">
                      <input
                        type="checkbox"
                        checked={retire.has(zone.zoneId!)}
                        disabled={!pending || busy !== null}
                        onChange={() => toggleRetire(zone.zoneId!)}
                      />
                      <span className="tag">retire</span>
                    </label>
                    <div className="planner__fields">
                      <p className="planner__retire">
                        <span className={`chip chip--${ZONE_TOKEN[zone.status]}`}>{zone.status}</span> {zone.name}
                      </p>
                      <p className="enrich__reason">{zone.rationale}</p>
                    </div>
                  </li>
                ))}
              </ul>
            )}

            {plan.zones.length === 0 && <p className="agent__pending">The zone layer already matches the incidents.</p>}

            {pending && plan.zones.length > 0 && (
              <div className="decide__actions">
                <button
                  type="button"
                  className="btn-approve"
                  disabled={busy !== null || chosenCount === 0}
                  onClick={() => void approve()}
                >
                  {busy === 'approve' ? 'Applying…' : `✓ Approve ${chosenCount} change(s)`}
                </button>
                <button type="button" className="btn-small" disabled={busy !== null} onClick={() => void reject()}>
                  {busy === 'reject' ? 'Rejecting…' : 'Reject plan'}
                </button>
              </div>
            )}
          </>
        )}
      </div>
    </section>
  )
}
