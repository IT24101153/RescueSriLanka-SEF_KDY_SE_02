import type { Incident } from '../types'
import { SEVERITY_TOKEN, STATUS_LABEL, timeAgo } from '../severity'

type ActivityFeedProps = {
  incidents: Incident[]
  onSelect: (incident: Incident) => void
}

/** Newest reports first — the "what just happened" column. */
export default function ActivityFeed({ incidents, onSelect }: ActivityFeedProps) {
  const recent = [...incidents]
    .sort(
      (a, b) =>
        new Date(b.reportedAt).getTime() - new Date(a.reportedAt).getTime(),
    )
    .slice(0, 6)

  return (
    <section className="panel" aria-label="Recent activity">
      <header className="panel__head">
        <h2 className="panel__title">Recent activity</h2>
        <span className="panel__meta">latest {recent.length}</span>
      </header>

      {recent.length === 0 ? (
        <p className="empty">Nothing reported yet.</p>
      ) : (
        <ul className="feed">
          {recent.map((incident) => (
            <li key={incident.id}>
              <button
                type="button"
                className="feed__item"
                onClick={() => onSelect(incident)}
              >
                <span className={`chip chip--${SEVERITY_TOKEN[incident.severity]}`}>
                  {incident.severity}
                </span>
                <span className="feed__body">
                  <span className="feed__title">{incident.title}</span>
                  <span className="feed__meta">
                    {incident.district ?? 'Unknown'} ·{' '}
                    {STATUS_LABEL[incident.status] ?? incident.status} ·{' '}
                    {timeAgo(incident.reportedAt)}
                  </span>
                </span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
