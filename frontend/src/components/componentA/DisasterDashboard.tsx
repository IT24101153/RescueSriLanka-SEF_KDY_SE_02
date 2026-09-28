import { useEffect, useState } from 'react'
import { apiFetch, apiFetchPage, queryString } from '../../shared/api/client'
import type {
  DashboardStatistics,
  Incident,
  IncidentSeverity,
  IncidentStatus,
  IncidentType,
  SafetyZone,
} from './types'
import OverviewSection from './sections/OverviewSection'
import MapSection from './sections/MapSection'
import ZonesSection from './sections/ZonesSection'
import IncidentTable, {
  type IncidentSortKey,
  type SortDirection,
} from './sections/IncidentTable'
import AgentPanel from './sections/AgentPanel'
import AgentActivity from './sections/AgentActivity'
import ReviewPanel from './sections/ReviewPanel'
import IncidentPhotos from './sections/IncidentPhotos'
import { SEVERITY_TOKEN, STATUS_LABEL, timeAgo } from './severity'
import './DisasterDashboard.css'

const SEVERITIES: IncidentSeverity[] = ['Low', 'Moderate', 'High', 'Critical']
const STATUSES: IncidentStatus[] = ['Reported', 'Verified', 'InProgress']
const TYPES: IncidentType[] = [
  'Flood',
  'Landslide',
  'Fire',
  'Accident',
  'Storm',
  'Tsunami',
  'Other',
]

type TabId = 'overview' | 'map' | 'zones' | 'queue' | 'agent'

/** Rows per page for the "Incident queue" tab — the only view with paging. */
const QUEUE_PAGE_SIZE = 20

const TABS: { id: TabId; label: string; hint: string }[] = [
  { id: 'overview', label: 'Overview', hint: 'Figures and breakdowns' },
  { id: 'map', label: 'Disaster map', hint: 'Live incident map' },
  { id: 'zones', label: 'Safety zones', hint: 'Safe / caution / danger' },
  { id: 'queue', label: 'Incident queue', hint: 'Full incident table' },
  { id: 'agent', label: 'Agent activity', hint: 'Runs and approvals' },
]

export default function DisasterDashboard() {
  const [tab, setTab] = useState<TabId>('overview')

  const [stats, setStats] = useState<DashboardStatistics | null>(null)
  const [incidents, setIncidents] = useState<Incident[]>([])
  const [zones, setZones] = useState<SafetyZone[]>([])

  const [severity, setSeverity] = useState<IncidentSeverity | ''>('')
  const [status, setStatus] = useState<IncidentStatus | ''>('')
  const [type, setType] = useState<IncidentType | ''>('')
  const [showZones, setShowZones] = useState(true)

  const [selected, setSelected] = useState<Incident | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [loadedAt, setLoadedAt] = useState<Date | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  // The "Incident queue" tab is the only view with server-side sorting and
  // paging — the map, zones and overview keep showing every active incident,
  // as they need the whole set rather than one page of it.
  const [queueIncidents, setQueueIncidents] = useState<Incident[]>([])
  const [queueTotalCount, setQueueTotalCount] = useState<number | null>(null)
  const [queuePage, setQueuePage] = useState(1)
  const [queueSortBy, setQueueSortBy] = useState<IncidentSortKey>('severity')
  const [queueSortDir, setQueueSortDir] = useState<SortDirection>('desc')
  const [queueLoading, setQueueLoading] = useState(false)
  const [queueError, setQueueError] = useState<string | null>(null)

  useEffect(() => {
    // Aborting on change means a slow response for old filters can never
    // overwrite the results for the filters now on screen.
    const controller = new AbortController()

    async function run() {
      try {
        const query = queryString({ severity, status, type, activeOnly: true })
        const options = { signal: controller.signal }

        const [nextStats, nextIncidents, nextZones] = await Promise.all([
          apiFetch<DashboardStatistics>('/api/incidents/statistics', options),
          apiFetch<Incident[]>(`/api/incidents${query}`, options),
          apiFetch<SafetyZone[]>('/api/safetyzones', options),
        ])

        if (controller.signal.aborted) return

        setStats(nextStats)
        setIncidents(nextIncidents)
        setZones(nextZones)
        setLoadedAt(new Date())
        setError(null)
      } catch (cause) {
        if (controller.signal.aborted) return
        setError(cause instanceof Error ? cause.message : 'Failed to load dashboard.')
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    }

    void run()
    return () => controller.abort()
  }, [severity, status, type, reloadToken])

  useEffect(() => {
    if (tab !== 'queue') return

    const controller = new AbortController()

    async function run() {
      setQueueLoading(true)
      try {
        const query = queryString({
          severity,
          status,
          type,
          activeOnly: true,
          sortBy: queueSortBy,
          sortDir: queueSortDir,
          page: queuePage,
          pageSize: QUEUE_PAGE_SIZE,
        })

        const { data, totalCount } = await apiFetchPage<Incident[]>(
          `/api/incidents${query}`,
          { signal: controller.signal },
        )

        if (controller.signal.aborted) return
        setQueueIncidents(data)
        setQueueTotalCount(totalCount)
        setQueueError(null)
      } catch (cause) {
        if (controller.signal.aborted) return
        setQueueError(cause instanceof Error ? cause.message : 'Failed to load the incident queue.')
      } finally {
        if (!controller.signal.aborted) setQueueLoading(false)
      }
    }

    void run()
    return () => controller.abort()
  }, [tab, severity, status, type, queueSortBy, queueSortDir, queuePage, reloadToken])

  /** Filter changes and the refresh button own the loading flag. */
  function reload() {
    setLoading(true)
    setReloadToken((token) => token + 1)
  }

  function handleQueueSort(key: IncidentSortKey) {
    setQueuePage(1)
    if (key === queueSortBy) {
      setQueueSortDir((dir) => (dir === 'asc' ? 'desc' : 'asc'))
    } else {
      setQueueSortBy(key)
      setQueueSortDir('desc')
    }
  }

  const queueTotalPages = queueTotalCount === null
    ? null
    : Math.max(1, Math.ceil(queueTotalCount / QUEUE_PAGE_SIZE))

  /**
   * After a coordinator decides something: refresh the list, and re-fetch the
   * open incident so the drawer shows the new state instead of closing and
   * making them find their place again.
   */
  async function refreshAfterDecision(id: string) {
    reload()
    try {
      setSelected(await apiFetch<Incident>(`/api/incidents/${id}`))
    } catch {
      setSelected(null)
    }
  }

  function changeFilter<T>(setter: (value: T) => void) {
    return (value: T) => {
      setLoading(true)
      setQueuePage(1)
      setter(value)
    }
  }

  const filtersActive = severity !== '' || status !== '' || type !== ''
  const showFilters = tab === 'map' || tab === 'queue'

  return (
    <div className="dash">
      <header className="dash__head">
        <div>
          <h1 className="dash__title">Incident &amp; Disaster Map</h1>
          <p className="dash__sub">
            Live operational picture for Sri Lanka — active incidents and derived
            safety zones.
          </p>
        </div>
        <div className="dash__actions">
          {loadedAt && (
            <span className="dash__stamp">
              Updated {loadedAt.toLocaleTimeString()}
            </span>
          )}
          <button
            type="button"
            className="btn-ghost"
            onClick={reload}
            disabled={loading}
          >
            {loading ? 'Refreshing…' : 'Refresh'}
          </button>
        </div>
      </header>

      <nav className="tabs" aria-label="Dashboard sections">
        {TABS.map((entry) => (
          <button
            key={entry.id}
            type="button"
            className={`tab${tab === entry.id ? ' is-active' : ''}`}
            aria-current={tab === entry.id ? 'page' : undefined}
            onClick={() => setTab(entry.id)}
          >
            <span className="tab__label">{entry.label}</span>
            <span className="tab__hint">{entry.hint}</span>
          </button>
        ))}
      </nav>

      {error && (
        <div className="alert" role="alert">
          {error}
        </div>
      )}

      {showFilters && (
        <section className="filters" aria-label="Filters">
          <label className="filter">
            <span>Severity</span>
            <select
              value={severity}
              onChange={(event) =>
                changeFilter(setSeverity)(event.target.value as IncidentSeverity | '')
              }
            >
              <option value="">All</option>
              {SEVERITIES.map((value) => (
                <option key={value} value={value}>
                  {value}
                </option>
              ))}
            </select>
          </label>

          <label className="filter">
            <span>Status</span>
            <select
              value={status}
              onChange={(event) =>
                changeFilter(setStatus)(event.target.value as IncidentStatus | '')
              }
            >
              <option value="">All</option>
              {STATUSES.map((value) => (
                <option key={value} value={value}>
                  {STATUS_LABEL[value]}
                </option>
              ))}
            </select>
          </label>

          <label className="filter">
            <span>Type</span>
            <select
              value={type}
              onChange={(event) =>
                changeFilter(setType)(event.target.value as IncidentType | '')
              }
            >
              <option value="">All</option>
              {TYPES.map((value) => (
                <option key={value} value={value}>
                  {value}
                </option>
              ))}
            </select>
          </label>

          {filtersActive && (
            <button
              type="button"
              className="filter__clear"
              onClick={() => {
                setSeverity('')
                setStatus('')
                setType('')
              }}
            >
              Clear filters
            </button>
          )}
        </section>
      )}

      {tab === 'overview' && stats && (
        <OverviewSection
          stats={stats}
          incidents={incidents}
          zones={zones}
          onSelect={setSelected}
        />
      )}

      {tab === 'map' && (
        <MapSection
          incidents={incidents}
          zones={zones}
          showZones={showZones}
          onToggleZones={setShowZones}
          selectedId={selected?.id ?? null}
          onSelect={setSelected}
        />
      )}

      {tab === 'zones' && (
        <ZonesSection zones={zones} incidents={incidents} onSelect={setSelected} />
      )}

      {tab === 'agent' && <AgentActivity />}

      {tab === 'queue' && (
        <section className="panel" aria-label="Incident queue">
          <header className="panel__head">
            <h2 className="panel__title">Incident queue</h2>
            <span className="panel__meta">
              {queueLoading
                ? 'Loading…'
                : `${queueTotalCount ?? queueIncidents.length} match${queueTotalCount === 1 ? '' : 'es'}`}
            </span>
          </header>

          {queueError && (
            <div className="alert" role="alert">
              {queueError}
            </div>
          )}

          <IncidentTable
            incidents={queueIncidents}
            selectedId={selected?.id ?? null}
            onSelect={setSelected}
            sortBy={queueSortBy}
            sortDir={queueSortDir}
            onSort={handleQueueSort}
          />

          {queueTotalPages !== null && queueTotalPages > 1 && (
            <nav className="pagination" aria-label="Incident queue pages">
              <button
                type="button"
                className="btn-ghost"
                onClick={() => setQueuePage((page) => page - 1)}
                disabled={queuePage <= 1 || queueLoading}
              >
                Previous
              </button>
              <span className="pagination__status">
                Page {queuePage} of {queueTotalPages}
              </span>
              <button
                type="button"
                className="btn-ghost"
                onClick={() => setQueuePage((page) => page + 1)}
                disabled={queuePage >= queueTotalPages || queueLoading}
              >
                Next
              </button>
            </nav>
          )}
        </section>
      )}

      {selected && (
        <div className="drawer-overlay">
          <div
            className="drawer__scrim"
            onClick={() => setSelected(null)}
            aria-hidden="true"
          />
          <aside className="drawer" aria-label="Incident detail">
            <header className="drawer__head">
              <span className={`chip chip--${SEVERITY_TOKEN[selected.severity]}`}>
                {selected.severity}
              </span>
              <button
                type="button"
                className="drawer__close"
                onClick={() => setSelected(null)}
                aria-label="Close detail"
              >
                ×
              </button>
            </header>

            <h3 className="drawer__title">{selected.title}</h3>
            <p className="drawer__meta">
              {selected.type} · {selected.district ?? 'Unknown district'} ·{' '}
              {timeAgo(selected.reportedAt)}
            </p>
            <p className="drawer__body">{selected.description}</p>

            <dl className="facts">
              <div>
                <dt>Status</dt>
                <dd>{STATUS_LABEL[selected.status] ?? selected.status}</dd>
              </div>
              <div>
                <dt>Affected radius</dt>
                <dd>{(selected.affectedRadiusMeters / 1000).toFixed(1)} km</dd>
              </div>
              <div>
                <dt>People affected</dt>
                <dd>{selected.estimatedAffectedPeople?.toLocaleString() ?? '—'}</dd>
              </div>
              <div>
                <dt>Coordinates</dt>
                <dd>
                  {selected.latitude.toFixed(4)}, {selected.longitude.toFixed(4)}
                </dd>
              </div>
            </dl>

            <IncidentPhotos images={selected.images ?? []} />

            <ReviewPanel
              incident={selected}
              onChanged={() => void refreshAfterDecision(selected.id)}
            />

            <AgentPanel
              incident={selected}
              onChanged={() => void refreshAfterDecision(selected.id)}
            />
          </aside>
        </div>
      )}
    </div>
  )
}
