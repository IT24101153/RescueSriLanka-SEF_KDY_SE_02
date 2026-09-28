import type { Incident } from '../types'
import { SEVERITY_TOKEN, STATUS_LABEL, timeAgo } from '../severity'

/** Columns the backend knows how to order by (`IncidentService.QueryAsync`). */
export type IncidentSortKey =
  | 'severity'
  | 'type'
  | 'district'
  | 'status'
  | 'affectedpeople'
  | 'reportedat'
export type SortDirection = 'asc' | 'desc'

type IncidentTableProps = {
  incidents: Incident[]
  selectedId: string | null
  onSelect: (incident: Incident) => void
  sortBy: IncidentSortKey
  sortDir: SortDirection
  onSort: (key: IncidentSortKey) => void
}

const COLUMNS: { key: IncidentSortKey; label: string; numeric?: boolean }[] = [
  { key: 'severity', label: 'Severity' },
  { key: 'type', label: 'Type' },
  { key: 'district', label: 'District' },
  { key: 'status', label: 'Status' },
  { key: 'affectedpeople', label: 'Affected', numeric: true },
  { key: 'reportedat', label: 'Reported' },
]

export default function IncidentTable({
  incidents,
  selectedId,
  onSelect,
  sortBy,
  sortDir,
  onSort,
}: IncidentTableProps) {
  if (incidents.length === 0) {
    return <p className="empty">No incidents match the current filters.</p>
  }

  return (
    <div className="table-wrap">
      <table className="table">
        <thead>
          <tr>
            {COLUMNS.map((column) => (
              <th key={column.key} className={column.numeric ? 'num' : undefined}>
                <button
                  type="button"
                  className={`th-sort${sortBy === column.key ? ' is-active' : ''}`}
                  onClick={() => onSort(column.key)}
                  aria-sort={
                    sortBy === column.key
                      ? sortDir === 'asc'
                        ? 'ascending'
                        : 'descending'
                      : 'none'
                  }
                >
                  {column.label}
                  {sortBy === column.key && (
                    <span aria-hidden="true">{sortDir === 'asc' ? ' ▲' : ' ▼'}</span>
                  )}
                </button>
              </th>
            ))}
            <th>Incident</th>
            <th className="num">AI score</th>
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
              <td>{incident.type}</td>
              <td>{incident.district ?? '—'}</td>
              <td>
                <span className="state">{STATUS_LABEL[incident.status] ?? incident.status}</span>
              </td>
              <td className="num">
                {incident.estimatedAffectedPeople?.toLocaleString() ?? '—'}
              </td>
              <td>{timeAgo(incident.reportedAt)}</td>
              <td className="cell-title">
                {incident.title}
                {incident.severityOverridden && (
                  <span className="tag" title="A coordinator overrode the AI severity">
                    overridden
                  </span>
                )}
              </td>
              <td className="num">
                {incident.aiSeverityScore ?? <span className="muted">pending</span>}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
