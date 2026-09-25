import { useCallback, useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { MapContainer, Marker, Popup, TileLayer, useMap } from "react-leaflet";
import L from "leaflet";
import "leaflet/dist/leaflet.css";
import { authFetch } from "./api";
import TravelAdvisoryManager from "./TravelAdvisoryManager";
import "./HelpRequestDashboard.css";

const TYPE_LABELS = ["Water", "Food", "Medical", "Rescue", "Shelter", "Other"] as const;
const STATUS_LABELS = ["Pending", "Assigned", "In Progress", "Resolved", "Cancelled"] as const;
const TYPE_COLORS = ["#1687d3", "#ed8a22", "#df4552", "#8258d5", "#20a56a", "#7b8190"] as const;

interface HelpRequestDto {
  id: string;
  type: number;
  description: string;
  latitude: number;
  longitude: number;
  urgencyScore: number;
  status: number;
  verificationStatus: number;
  imageUrl: string | null;
  createdAt: string;
}

function requestIcon(type: number, urgency: number) {
  const color = TYPE_COLORS[type] ?? TYPE_COLORS[5];
  const critical = urgency >= 70 ? " dashboard-map-pin--critical" : "";
  return L.divIcon({
    className: "dashboard-map-marker",
    html: `<span class="dashboard-map-pin${critical}" style="--pin-color:${color}"><i></i></span>`,
    iconSize: [30, 38],
    iconAnchor: [15, 36],
    popupAnchor: [0, -35],
  });
}

function FitRequests({ requests }: { requests: HelpRequestDto[] }) {
  const map = useMap();
  useEffect(() => {
    if (requests.length === 1) {
      map.setView([requests[0].latitude, requests[0].longitude], 12);
    } else if (requests.length > 1) {
      map.fitBounds(requests.map((r) => [r.latitude, r.longitude] as [number, number]), { padding: [42, 42] });
    }
  }, [map, requests]);
  return null;
}

function timeAgo(iso: string) {
  const minutes = Math.max(1, Math.round((Date.now() - new Date(iso).getTime()) / 60_000));
  if (minutes < 60) return `${minutes}m ago`;
  if (minutes < 1440) return `${Math.floor(minutes / 60)}h ago`;
  return `${Math.floor(minutes / 1440)}d ago`;
}

export default function Dashboard() {
  const [requests, setRequests] = useState<HelpRequestDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [showAdvisories, setShowAdvisories] = useState(false);

  const loadRequests = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const response = await authFetch("/api/HelpRequests");
      if (!response.ok) {
        throw new Error(
          response.status === 401 || response.status === 403
              ? "Your dashboard session is not authorized. Please sign out and sign in again as the coordinator."
              : "Unable to load live request data. Make sure the API is running.",
        );
      }
      setRequests(await response.json());
    } catch (error) {
      setError(error instanceof Error ? error.message : "Unable to load live request data. Make sure the API is running.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { loadRequests(); }, [loadRequests]);

  if (showAdvisories) return <TravelAdvisoryManager onBack={() => setShowAdvisories(false)} />;

  const metrics = useMemo(() => ({
    total: requests.length,
    pendingVerification: requests.filter((r) => r.verificationStatus === 0).length,
    highUrgency: requests.filter((r) => r.urgencyScore >= 70 && r.status !== 3 && r.status !== 4).length,
    active: requests.filter((r) => r.status === 1 || r.status === 2).length,
  }), [requests]);
  const urgentRequests = useMemo(() => [...requests]
    .filter((r) => r.status !== 3 && r.status !== 4)
    .sort((a, b) => b.urgencyScore - a.urgencyScore || +new Date(b.createdAt) - +new Date(a.createdAt))
    .slice(0, 5), [requests]);

  return (
    <div className="dashboard-page">
      <header className="dashboard-header">
        <div>
          <p className="dashboard-kicker">Operations overview</p>
          <h1>Response dashboard</h1>
          <p className="dashboard-subtitle">Monitor incoming citizen requests and prioritise the people who need help first.</p>
        </div>
        <div className="dashboard-header-actions"><button className="dashboard-refresh" onClick={() => setShowAdvisories(true)}>Manage travel advisories</button><button className="dashboard-refresh" onClick={loadRequests} disabled={loading}>{loading ? "Updating…" : "Refresh data"}</button></div>
      </header>

      <section className="dashboard-metrics" aria-label="Request summary">
        <MetricCard label="Total requests" value={metrics.total} detail="All submitted reports" tone="blue" />
        <MetricCard label="Needs verification" value={metrics.pendingVerification} detail="Reports awaiting review" tone="amber" />
        <MetricCard label="High urgency" value={metrics.highUrgency} detail="Score of 70 or above" tone="red" />
        <MetricCard label="Response in progress" value={metrics.active} detail="Assigned or active requests" tone="green" />
      </section>

      {error && <div className="dashboard-error">{error}</div>}

      <section className="dashboard-grid">
        <div className="dashboard-map-card">
          <div className="dashboard-card-heading">
            <div><h2>Live request map</h2><p>Each pin is a citizen help request.</p></div>
            <span className="dashboard-live"><i /> Live</span>
          </div>
          <div className="dashboard-map-wrap">
            <MapContainer center={[7.8731, 80.7718]} zoom={7} scrollWheelZoom className="dashboard-map">
              <TileLayer attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors' url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png" />
              {requests.length > 0 && <FitRequests requests={requests} />}
              {requests.map((request) => (
                <Marker key={request.id} position={[request.latitude, request.longitude]} icon={requestIcon(request.type, request.urgencyScore)}>
                  <Popup>
                    <div className="dashboard-popup">
                      <strong>{TYPE_LABELS[request.type] ?? "Other"} request</strong>
                      <span>Urgency {request.urgencyScore} · {STATUS_LABELS[request.status]}</span>
                      <p>{request.description}</p>
                      <Link to="/dashboard/help-requests">Open help requests</Link>
                    </div>
                  </Popup>
                </Marker>
              ))}
            </MapContainer>
            {!loading && requests.length === 0 && <div className="dashboard-map-empty">No request locations to show yet.</div>}
          </div>
          <div className="dashboard-legend">
            {TYPE_LABELS.map((label, index) => <span key={label}><i style={{ backgroundColor: TYPE_COLORS[index] }} />{label}</span>)}
          </div>
        </div>

        <aside className="dashboard-urgent-card">
          <div className="dashboard-card-heading"><div><h2>Priority queue</h2><p>Open requests ranked by urgency.</p></div><Link to="/dashboard/help-requests">View all</Link></div>
          <div className="dashboard-urgent-list">
            {!loading && urgentRequests.length === 0 && <p className="dashboard-no-items">No active requests.</p>}
            {urgentRequests.map((request) => (
              <Link className="dashboard-urgent-item" to="/dashboard/help-requests" key={request.id}>
                <span className="dashboard-type-dot" style={{ backgroundColor: TYPE_COLORS[request.type] ?? TYPE_COLORS[5] }} />
                <span className="dashboard-urgent-text"><strong>{TYPE_LABELS[request.type] ?? "Other"}</strong><small>{request.description}</small></span>
                <span className={`dashboard-score${request.urgencyScore >= 70 ? " dashboard-score--critical" : ""}`}>{request.urgencyScore}<small>{timeAgo(request.createdAt)}</small></span>
              </Link>
            ))}
          </div>
        </aside>
      </section>
    </div>
  );
}

function MetricCard({ label, value, detail, tone }: { label: string; value: number; detail: string; tone: string }) {
  return <article className={`dashboard-metric dashboard-metric--${tone}`}><span className="dashboard-metric-label">{label}</span><strong>{value}</strong><span className="dashboard-metric-detail">{detail}</span></article>;
}
