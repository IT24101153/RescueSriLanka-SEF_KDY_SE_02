import { useEffect, useState, useCallback } from "react";
import { authFetch } from "./api";
import { getSession } from "../../shared/auth/session";
import "./HelpRequestsReview.css";

const TYPE_LABELS = ["Water", "Food", "Medical", "Rescue", "Shelter", "Other"] as const;
const STATUS_LABELS = ["Pending", "Assigned", "In Progress", "Resolved", "Cancelled"] as const;
const VERIFICATION_LABELS = ["Pending Verification", "Verified", "Rejected (Fake)"] as const;

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
  verificationStatus: number;
  verificationNotes: string | null;
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

function CheckIcon() {
  return (
    <svg viewBox="0 0 16 16" width="13" height="13" fill="none" xmlns="http://www.w3.org/2000/svg">
      <path d="M3.5 8.3 6.2 11 12.5 4.7" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

function XIcon() {
  return (
    <svg viewBox="0 0 16 16" width="13" height="13" fill="none" xmlns="http://www.w3.org/2000/svg">
      <path d="M4 4l8 8M12 4l-8 8" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" />
    </svg>
  );
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
  const user = getSession()?.user;
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
      const res = await authFetch(`/api/HelpRequests`);
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
      const res = await authFetch(`/api/HelpRequests/${id}/history`);
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
      const res = await authFetch(`/api/HelpRequests/${selected.id}/status`, {
        method: "PATCH",
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

  async function verify(isReal: boolean) {
    if (!selected) return;
    setUpdating(true);
    try {
      const res = await authFetch(`/api/HelpRequests/${selected.id}/verify`, {
        method: "PATCH",
        body: JSON.stringify({
          isReal,
          notes: isReal ? "Verified by coordinator" : "Marked as fake by coordinator",
        }),
      });
      if (!res.ok) throw new Error();
      await loadRequests();
    } catch {
      setError("Verification update failed. Try again.");
    } finally {
      setUpdating(false);
    }
  }

  return (
    <div className="hr-console">
      <div className="hr-topbar">
        <div className="hr-breadcrumb">
          Dashboard <span>/</span> <strong>Help Requests</strong>
        </div>
        <div className="hr-topbar-avatar" title={user?.fullName ?? "Admin"}>
          {(user?.fullName ?? "A").charAt(0)}
        </div>
      </div>

      <div className="hr-page-head">
        <h1>Help Requests</h1>
        <p className="hr-subtitle">
          {requests.length} active request{requests.length === 1 ? "" : "s"}, ranked by urgency
        </p>
      </div>

      {error && <div className="hr-banner">{error}</div>}

      <div className="hr-body">
        <div className="hr-list">
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
              <span className="hr-row-accent" />
              <span className="hr-row-score">{r.urgencyScore}</span>
              <span className="hr-row-main">
                <span className="hr-row-type">{TYPE_LABELS[r.type]}</span>
                <span className="hr-row-desc">{r.description}</span>
              </span>
              <span className={`hr-row-status hr-row-status--${r.status}`}>{STATUS_LABELS[r.status]}</span>
            </button>
          ))}
        </div>

        <div className="hr-detail">
          {!selected && <div className="hr-empty">Select a request to see details.</div>}

          {selected && (
            <>
              <div className="hr-detail-head">
                <div className="hr-detail-badges">
                  <span className={`hr-pill hr-pill--${urgencyTier(selected.urgencyScore)}`}>
                    Urgency {selected.urgencyScore}
                  </span>
                  <span className="hr-pill hr-pill--neutral">{TYPE_LABELS[selected.type]}</span>
                  <span className={`hr-pill hr-pill--verify-${selected.verificationStatus}`}>
                    {VERIFICATION_LABELS[selected.verificationStatus]}
                  </span>
                </div>
              </div>

              <p className="hr-detail-desc">{selected.description}</p>

              {selected.imageUrl && (
                <a className="hr-request-image-link" href={selected.imageUrl} target="_blank" rel="noreferrer">
                  <img className="hr-request-image" src={selected.imageUrl} alt="Photo submitted with this help request" />
                  <span>Open full-size photo</span>
                </a>
              )}

              {selected.verificationStatus === 0 && (
                <div className="hr-verify-prompt">
                  <span className="hr-verify-prompt-text">This report needs verification</span>
                  <div className="hr-verify-prompt-actions">
                    <button
                      className="hr-verify-btn hr-verify-btn--real"
                      disabled={updating}
                      onClick={() => verify(true)}
                    >
                      <CheckIcon /> Verify
                    </button>
                    <button
                      className="hr-verify-btn hr-verify-btn--fake"
                      disabled={updating}
                      onClick={() => verify(false)}
                    >
                      <XIcon /> Reject
                    </button>
                  </div>
                </div>
              )}
              <div className="hr-detail-meta">
                <div className="hr-meta-item">
                  <span className="hr-meta-label">Location</span>
                  <span className="hr-mono">
                    {selected.latitude.toFixed(4)}, {selected.longitude.toFixed(4)}
                  </span>
                </div>
                <div className="hr-meta-item">
                  <span className="hr-meta-label">Submitted</span>
                  <span className="hr-mono">{formatTime(selected.createdAt)}</span>
                </div>
                <div className="hr-meta-item">
                  <span className="hr-meta-label">Status</span>
                  <span>{STATUS_LABELS[selected.status]}</span>
                </div>
              </div>

              <div className="hr-actions">
                <span className="hr-actions-label">Set status</span>
                <div className="hr-segmented">
                  {STATUS_LABELS.map((label, idx) => (
                    <button
                      key={label}
                      className={`hr-segment ${selected.status === idx ? "hr-segment--current" : ""}`}
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
                      <span className="hr-timeline-dot" />
                      <div className="hr-timeline-content">
                        <div className="hr-timeline-top">
                          <span>
                            {STATUS_LABELS[h.oldStatus]} → {STATUS_LABELS[h.newStatus]}
                          </span>
                          <span className="hr-mono hr-timeline-time">{formatTime(h.changedAt)}</span>
                        </div>
                        {h.notes && <div className="hr-history-note">{h.notes}</div>}
                      </div>
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
