import { useEffect, useState } from 'react'
import { apiFetch } from '../../../shared/api/client'
import type { AgentRun } from '../../../shared/types'
import { timeAgo } from '../severity'

/** Workflow monitoring — every agent execution, newest first. */
export default function AgentActivity() {
  const [runs, setRuns] = useState<AgentRun[]>([])
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()

    async function load() {
      try {
        const data = await apiFetch<AgentRun[]>('/api/agentruns?take=100', {
          signal: controller.signal,
        })
        if (!controller.signal.aborted) setRuns(data)
      } catch (cause) {
        if (!controller.signal.aborted) {
          setError(cause instanceof Error ? cause.message : 'Could not load agent runs.')
        }
      }
    }

    void load()
    return () => controller.abort()
  }, [])

  const succeeded = runs.filter((run) => run.status === 'Succeeded').length
  const fallback = runs.filter((run) => run.usedFallback).length
  const approved = runs.filter((run) => run.approved).length
  const avgMs = runs.length
    ? Math.round(runs.reduce((sum, run) => sum + run.durationMs, 0) / runs.length)
    : 0

  return (
    <div className="section">
      <section className="stat-row" aria-label="Agent figures">
        <article className="stat">
          <p className="stat__label">Total runs</p>
          <p className="stat__value">{runs.length}</p>
          <p className="stat__hint">across all agents</p>
        </article>
        <article className="stat">
          <p className="stat__label">Model-backed</p>
          <p className="stat__value">{succeeded}</p>
          <p className="stat__hint">completed on the LLM</p>
        </article>
        <article className="stat stat--caution">
          <p className="stat__label">Rule-engine fallback</p>
          <p className="stat__value">{fallback}</p>
          <p className="stat__hint">model unavailable</p>
        </article>
        <article className="stat">
          <p className="stat__label">Approved</p>
          <p className="stat__value">{approved}</p>
          <p className="stat__hint">passed the human gate</p>
        </article>
        <article className="stat">
          <p className="stat__label">Avg duration</p>
          <p className="stat__value">{avgMs.toLocaleString()}</p>
          <p className="stat__hint">milliseconds</p>
        </article>
      </section>

      <section className="panel" aria-label="Agent execution log">
        <header className="panel__head">
          <h2 className="panel__title">Agent execution log</h2>
          <span className="panel__meta">{runs.length} runs</span>
        </header>

        {error && <p className="empty">{error}</p>}

        {!error && runs.length === 0 ? (
          <p className="empty">No agent runs recorded yet.</p>
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Agent</th>
                  <th>Status</th>
                  <th>Model</th>
                  <th className="num">Duration</th>
                  <th>Approval</th>
                  <th>Started</th>
                  <th className="col-wide">Note</th>
                </tr>
              </thead>
              <tbody>
                {runs.map((run) => (
                  <tr key={run.id}>
                    <td className="cell-title">{run.agentName}</td>
                    <td>
                      <span
                        className={`badge badge--${
                          run.status === 'Succeeded'
                            ? 'ok'
                            : run.status === 'Failed'
                              ? 'bad'
                              : 'warn'
                        }`}
                      >
                        {run.status === 'SucceededWithFallback' ? 'Fallback' : run.status}
                      </span>
                    </td>
                    <td>
                      <span className="state">{run.model ?? '—'}</span>
                    </td>
                    <td className="num">{run.durationMs.toLocaleString()} ms</td>
                    <td>
                      <span className="state">
                        {run.approvedAt === null
                          ? 'Awaiting'
                          : run.approved
                            ? 'Approved'
                            : 'Rejected'}
                      </span>
                    </td>
                    <td>{timeAgo(run.startedAt)}</td>
                    <td className="col-wide">
                      <span className="state">{run.errorMessage ?? '—'}</span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  )
}
