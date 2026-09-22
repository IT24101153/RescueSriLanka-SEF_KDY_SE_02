import { useEffect, useState } from 'react'
import { apiFetch, queryString } from '../../api/client'
import type {
  DashboardStatistics,
  Incident,
  IncidentSeverity,
  IncidentStatus,
  IncidentType,
  SafetyZone,
} from '../../types/incidents'
import OverviewSection from './sections/OverviewSection'
import MapSection from './sections/MapSection'
import ZonesSection from './sections/ZonesSection'
import IncidentTable from './sections/IncidentTable'
import AgentPanel from './sections/AgentPanel'
import AgentActivity from './sections/AgentActivity'
import ReviewPanel from './sections/ReviewPanel'
import IncidentPhotos from './sections/IncidentPhotos'
import { SEVERITY_TOKEN, STATUS_LABEL, timeAgo } from './severity'
import './Dashboard.css'
import type { Role } from '../../auth/session'

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

const TABS: { id: TabId; label: string; hint: string }[] = [
  { id: 'overview', label: 'Overview', hint: 'Figures and breakdowns' },
  { id: 'map', label: 'Disaster map', hint: 'Live incident map' },
  { id: 'zones', label: 'Safety zones', hint: 'Safe / caution / danger' },
  { id: 'queue', label: 'Incident queue', hint: 'Full incident table' },
  { id: 'agent', label: 'Agent activity', hint: 'Runs and approvals' },
]

export default function Dashboard({ role: _role }: { role: Role }) {
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

  /** Filter changes and the refresh button own the loading flag. */
  function reload() {
    setLoading(true)
    setReloadToken((token) => token + 1)
  }

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
            <span className="panel__meta">{incidents.length} shown</span>
          </header>
          <IncidentTable
            incidents={incidents}
            selectedId={selected?.id ?? null}
            onSelect={setSelected}
          />
        </section>
      )}

      {selected && (
        <>
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
        </>
      )}
    </div>
  )
}
