import type { Incident } from '../types'
import { SEVERITY_TOKEN, STATUS_LABEL, timeAgo } from '../severity'

type IncidentTableProps = {
  incidents: Incident[]
  selectedId: string | null
  onSelect: (incident: Incident) => void
}

export default function IncidentTable({
  incidents,
  selectedId,
  onSelect,
}: IncidentTableProps) {
  if (incidents.length === 0) {
    return <p className="empty">No incidents match the current filters.</p>
  }

  return (
    <div className="table-wrap">
      <table className="table">
        <thead>
          <tr>
            <th>Severity</th>
            <th>Incident</th>
            <th>Type</th>
            <th>District</th>
            <th>Status</th>
            <th className="num">AI score</th>
            <th className="num">Affected</th>
            <th>Reported</th>
          </tr>
        </thead>
        <tbody>
          {incidents.map((incident) => (
            <tr
              key={incident.id}
              className={incident.id === selectedId ? 'is-selected' : undefined}
              onClick={() => onSelect(incident)}
            >
              <td>
                <span className={`chip chip--${SEVERITY_TOKEN[incident.severity]}`}>
                  {incident.severity}
                </span>
              </td>
              <td className="cell-title">
                {incident.title}
                {incident.severityOverridden && (
                  <span className="tag" title="A coordinator overrode the AI severity">
                    overridden
                  </span>
                )}
              </td>
              <td>{incident.type}</td>
              <td>{incident.district ?? '—'}</td>
              <td>
                <span className="state">{STATUS_LABEL[incident.status] ?? incident.status}</span>
              </td>
              <td className="num">
                {incident.aiSeverityScore ?? <span className="muted">pending</span>}
              </td>
              <td className="num">
                {incident.estimatedAffectedPeople?.toLocaleString() ?? '—'}
              </td>
              <td>{timeAgo(incident.reportedAt)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
