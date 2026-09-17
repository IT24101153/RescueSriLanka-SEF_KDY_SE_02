import { useEffect, useState, useCallback } from "react";
import "./HelpRequestsReview.css";

// Adjust this to match your project's actual API base URL config
// (e.g. import.meta.env.VITE_API_BASE_URL if you have a .env set up).
const API_BASE = "http://localhost:5093";

const TYPE_LABELS = ["Water", "Food", "Medical", "Rescue", "Shelter", "Other"] as const;
const STATUS_LABELS = ["Pending", "Assigned", "In Progress", "Resolved", "Cancelled"] as const;

type UrgencyTier = "danger" | "caution" | "safe";

interface HelpRequestDto {
  id: string;
  citizenId: string;
  type: number;
  description: string;
  latitude: number;
  longitude: number;
  urgencyScore: number;
  status: number;
  imageUrl: string | null;
  createdAt: string;
  updatedAt: string;
}

interface StatusHistoryDto {
  oldStatus: number;
  newStatus: number;
  notes: string | null;
  changedAt: string;
}

function urgencyTier(score: number): UrgencyTier {
  if (score >= 70) return "danger";
  if (score >= 40) return "caution";
  return "safe";
}

function formatTime(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleString(undefined, {
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

export default function HelpRequestsReview() {
  const [requests, setRequests] = useState<HelpRequestDto[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [history, setHistory] = useState<StatusHistoryDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [updating, setUpdating] = useState(false);

  const loadRequests = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await fetch(`${API_BASE}/api/HelpRequests`);
      if (!res.ok) throw new Error(`Server responded ${res.status}`);
      const data: HelpRequestDto[] = await res.json();
      const sorted = [...data].sort((a, b) => b.urgencyScore - a.urgencyScore);
      setRequests(sorted);
      setSelectedId((current) => current ?? (sorted.length > 0 ? sorted[0].id : null));
    } catch {
      setError("Couldn't reach the request service. Check the API is running.");
    } finally {
      setLoading(false);
    }
  }, []);

  const loadHistory = useCallback(async (id: string | null) => {
    if (!id) {
      setHistory([]);
      return;
    }
    try {
      const res = await fetch(`${API_BASE}/api/HelpRequests/${id}/history`);
      if (!res.ok) throw new Error();
      setHistory(await res.json());
    } catch {
      setHistory([]);
    }
  }, []);

  useEffect(() => {
    loadRequests();
  }, [loadRequests]);

  useEffect(() => {
    loadHistory(selectedId);
  }, [selectedId, loadHistory]);

  const selected = requests.find((r) => r.id === selectedId) ?? null;

  async function changeStatus(newStatusIndex: number) {
    if (!selected) return;
    setUpdating(true);
    try {
      const res = await fetch(`${API_BASE}/api/HelpRequests/${selected.id}/status`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          newStatus: newStatusIndex,
          notes: `Status set to ${STATUS_LABELS[newStatusIndex]} by coordinator`,
        }),
      });
      if (!res.ok) throw new Error();
      await loadRequests();
      await loadHistory(selected.id);
    } catch {
      setError("Status update failed. Try again.");
    } finally {
      setUpdating(false);
    }
  }

  return (
    <div className="hr-console">
      <header className="hr-header">
        <h1>Help Requests</h1>
        <p className="hr-subtitle">
          {requests.length} active request{requests.length === 1 ? "" : "s"}, ranked by urgency
        </p>
      </header>

      {error && <div className="hr-banner">{error}</div>}

      <div className="hr-body">
        <div className="hr-list" role="list">
          {loading && <div className="hr-empty">Loading requests…</div>}
          {!loading && requests.length === 0 && (
            <div className="hr-empty">No help requests yet. New submissions will appear here.</div>
          )}
          {requests.map((r) => (
            <button
              key={r.id}
              className={`hr-row hr-row--${urgencyTier(r.urgencyScore)} ${
                r.id === selectedId ? "hr-row--active" : ""
              }`}
              onClick={() => setSelectedId(r.id)}
            >
              <span className="hr-row-score">{r.urgencyScore}</span>
              <span className="hr-row-main">
                <span className="hr-row-type">{TYPE_LABELS[r.type]}</span>
                <span className="hr-row-desc">{r.description}</span>
              </span>
              <span className="hr-row-status">{STATUS_LABELS[r.status]}</span>
            </button>
          ))}
        </div>

        <div className="hr-detail">
          {!selected && <div className="hr-empty">Select a request to see details.</div>}

          {selected && (
            <>
              <div className={`hr-detail-urgency hr-detail-urgency--${urgencyTier(selected.urgencyScore)}`}>
                Urgency {selected.urgencyScore} · {TYPE_LABELS[selected.type]}
              </div>

              <p className="hr-detail-desc">{selected.description}</p>

              <dl className="hr-detail-meta">
                <div>
                  <dt>Location</dt>
                  <dd className="hr-mono">
                    {selected.latitude.toFixed(4)}, {selected.longitude.toFixed(4)}
                  </dd>
                </div>
                <div>
                  <dt>Submitted</dt>
                  <dd className="hr-mono">{formatTime(selected.createdAt)}</dd>
                </div>
                <div>
                  <dt>Status</dt>
                  <dd>{STATUS_LABELS[selected.status]}</dd>
                </div>
              </dl>

              <div className="hr-actions">
                <span className="hr-actions-label">Set status</span>
                <div className="hr-actions-buttons">
                  {STATUS_LABELS.map((label, idx) => (
                    <button
                      key={label}
                      className={`hr-status-btn ${selected.status === idx ? "hr-status-btn--current" : ""}`}
                      disabled={updating || selected.status === idx}
                      onClick={() => changeStatus(idx)}
                    >
                      {label}
                    </button>
                  ))}
                </div>
              </div>

              <div className="hr-history">
                <span className="hr-actions-label">History</span>
                {history.length === 0 && <p className="hr-history-empty">No changes recorded yet.</p>}
                <ul className="hr-timeline">
                  {history.map((h, i) => (
                    <li key={i}>
                      <span className="hr-mono">{formatTime(h.changedAt)}</span>
                      <span>
                        {STATUS_LABELS[h.oldStatus]} → {STATUS_LABELS[h.newStatus]}
                      </span>
                      {h.notes && <span className="hr-history-note">{h.notes}</span>}
                    </li>
                  ))}
                </ul>
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  );
}