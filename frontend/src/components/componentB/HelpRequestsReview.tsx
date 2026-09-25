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

interface AiAnalysisDto {
  reasoning: string;
  credibilitySignal: string;
  suggestedAction: string;
}

function canTransition(current: number, next: number): boolean {
  return (
    (current === 0 && (next === 1 || next === 4)) ||
    (current === 1 && (next === 2 || next === 4)) ||
    (current === 2 && (next === 3 || next === 4))
  );
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
  const [aiReview, setAiReview] = useState<AiAnalysisDto | null>(null);
  const [aiReviewing, setAiReviewing] = useState(false);
  const [aiError, setAiError] = useState<string | null>(null);
  const [query, setQuery] = useState("");
  const [statusFilter, setStatusFilter] = useState("all");
  const [typeFilter, setTypeFilter] = useState("all");
  const [sort, setSort] = useState("urgency");
  const [page, setPage] = useState(0);

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
    setAiReview(null);
    setAiError(null);
  }, [selectedId, loadHistory]);

  const filteredRequests = requests
    .filter((request) =>
      (typeFilter === "all" || request.type === Number(typeFilter)) &&
      (statusFilter === "all" || request.status === Number(statusFilter)) &&
      `${TYPE_LABELS[request.type]} ${request.description}`.toLowerCase().includes(query.trim().toLowerCase()),
    )
    .sort((a, b) => sort === "newest"
      ? new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime()
      : b.urgencyScore - a.urgencyScore);
  const pageSize = 8;
  const pageCount = Math.max(1, Math.ceil(filteredRequests.length / pageSize));
  const visibleRequests = filteredRequests.slice(page * pageSize, (page + 1) * pageSize);
  const selected = requests.find((r) => r.id === selectedId) ?? null;

  useEffect(() => setPage(0), [query, statusFilter, typeFilter, sort]);

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

  async function runAiReview() {
    if (!selected) return;
    setAiReviewing(true);
    setAiError(null);
    try {
      const res = await authFetch(`/api/HelpRequests/${selected.id}/ai-analysis`, {
        method: "POST",
      });
      if (!res.ok) throw new Error();
      setAiReview(await res.json());
    } catch {
      setAiError("AI review is unavailable. You can still verify and triage this request manually.");
    } finally {
      setAiReviewing(false);
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

      <div className="hr-filters">
        <input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Search requests" aria-label="Search requests" />
        <select value={typeFilter} onChange={(event) => setTypeFilter(event.target.value)} aria-label="Filter by type">
          <option value="all">All types</option>
          {TYPE_LABELS.map((label, index) => <option key={label} value={index}>{label}</option>)}
        </select>
        <select value={statusFilter} onChange={(event) => setStatusFilter(event.target.value)} aria-label="Filter by status">
          <option value="all">All statuses</option>
          {STATUS_LABELS.map((label, index) => <option key={label} value={index}>{label}</option>)}
        </select>
        <select value={sort} onChange={(event) => setSort(event.target.value)} aria-label="Sort requests">
          <option value="urgency">Highest urgency</option>
          <option value="newest">Newest first</option>
        </select>
      </div>

      <div className="hr-body">
        <div className="hr-list">
          {loading && <div className="hr-empty">Loading requests…</div>}
          {!loading && filteredRequests.length === 0 && (
            <div className="hr-empty">No requests match these filters.</div>
          )}
          {visibleRequests.map((r) => (
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
          {filteredRequests.length > pageSize && (
            <div className="hr-pagination">
              <button disabled={page === 0} onClick={() => setPage(page - 1)}>Previous</button>
              <span>Page {page + 1} of {pageCount}</span>
              <button disabled={page + 1 >= pageCount} onClick={() => setPage(page + 1)}>Next</button>
            </div>
          )}
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

              <div className="hr-actions">
                <span className="hr-actions-label">AI review</span>
                <button className="hr-segment" disabled={aiReviewing} onClick={runAiReview}>
                    {aiReviewing ? "Reviewing…" : "Run AI review"}
                </button>
                {aiError && <p className="hr-banner">{aiError}</p>}
              </div>

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
                      disabled={updating || !canTransition(selected.status, idx)}
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
      {aiReview && (
        <div className="hr-ai-backdrop" role="presentation" onClick={() => setAiReview(null)}>
          <section className="hr-ai-dialog" role="dialog" aria-modal="true" aria-label="AI request review" onClick={(event) => event.stopPropagation()}>
            <div className="hr-ai-dialog-head">
              <div>
                <span className="hr-actions-label">AI review</span>
                <p>Advisory support only — the coordinator makes the final decision.</p>
              </div>
              <button className="hr-ai-close" onClick={() => setAiReview(null)} aria-label="Close AI review">×</button>
            </div>
            <div className="hr-ai-result"><span>Assessment</span><p>{aiReview.reasoning}</p></div>
            <div className="hr-ai-result"><span>Credibility signal</span><p>{aiReview.credibilitySignal}</p></div>
            <div className="hr-ai-result"><span>Suggested action</span><p>{aiReview.suggestedAction}</p></div>
          </section>
        </div>
      )}
    </div>
  );
}
