import { useCallback, useEffect, useMemo, useState } from "react";
import type { FormEvent } from "react";
import { authFetch } from "./api";
import "./TravelAdvisoryManager.css";

type Advisory = {
  id: string; areaName: string; latitude: number; longitude: number;
  radiusMeters: number; safetyLevel: number; reason: string;
  createdAt: string; updatedAt: string; expiresAt: string | null;
};

const labels = ["Safe", "Caution", "Danger"];
const initial = { areaName: "", latitude: "", longitude: "", radiusMeters: "500", safetyLevel: "1", reason: "", expiresAt: "" };

export default function TravelAdvisoryManager({ onBack }: { onBack: () => void }) {
  const [items, setItems] = useState<Advisory[]>([]);
  const [form, setForm] = useState(initial);
  const [editing, setEditing] = useState<string | null>(null);
  const [query, setQuery] = useState("");
  const [level, setLevel] = useState("all");
  const [sort, setSort] = useState("newest");
  const [page, setPage] = useState(0);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true); setError(null);
    try {
      const response = await authFetch("/api/TravelAdvisories/all");
      if (!response.ok) {
        throw new Error(response.status === 404
          ? "The advisory endpoint was not found. Restart the RescueSriLanka API so it loads the latest Component B routes, then refresh this page."
          : `The API returned HTTP ${response.status}. Check the backend terminal for database or migration errors.`);
      }
      setItems(await response.json());
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Unable to load advisories.");
    } finally { setLoading(false); }
  }, []);

  useEffect(() => { void load(); }, [load]);

  const filtered = useMemo(() => items
    .filter(item => (level === "all" || item.safetyLevel === Number(level)) &&
      `${item.areaName} ${item.reason}`.toLowerCase().includes(query.trim().toLowerCase()))
    .sort((a, b) => sort === "oldest" ? a.createdAt.localeCompare(b.createdAt) : b.createdAt.localeCompare(a.createdAt)),
  [items, level, query, sort]);
  const pages = Math.max(1, Math.ceil(filtered.length / 8));

  function startEdit(item: Advisory) {
    setEditing(item.id);
    setForm({ areaName: item.areaName, latitude: String(item.latitude), longitude: String(item.longitude), radiusMeters: String(item.radiusMeters), safetyLevel: String(item.safetyLevel), reason: item.reason, expiresAt: item.expiresAt ? item.expiresAt.slice(0, 16) : "" });
    setSuccess(null);
  }

  async function submit(event: FormEvent) {
    event.preventDefault(); setSaving(true); setError(null); setSuccess(null);
    const payload = { areaName: form.areaName.trim(), latitude: Number(form.latitude), longitude: Number(form.longitude), radiusMeters: Number(form.radiusMeters), safetyLevel: Number(form.safetyLevel), reason: form.reason.trim(), expiresAt: form.expiresAt ? new Date(form.expiresAt).toISOString() : null };
    try {
      const response = await authFetch(editing ? `/api/TravelAdvisories/${editing}` : "/api/TravelAdvisories", { method: editing ? "PUT" : "POST", body: JSON.stringify(payload) });
      if (!response.ok) throw new Error(response.status === 400 ? "Check the advisory fields and expiry time." : `Save failed (HTTP ${response.status}).`);
      setForm(initial); setEditing(null); setSuccess(editing ? "Advisory updated." : "Advisory created."); await load();
    } catch (cause) { setError(cause instanceof Error ? cause.message : "Save failed."); }
    finally { setSaving(false); }
  }

  async function remove(item: Advisory) {
    if (!window.confirm(`Delete the advisory for ${item.areaName}?`)) return;
    setError(null); setSuccess(null);
    try {
      const response = await authFetch(`/api/TravelAdvisories/${item.id}`, { method: "DELETE" });
      if (!response.ok) throw new Error(`Delete failed (HTTP ${response.status}).`);
      setSuccess("Advisory deleted."); await load();
    } catch (cause) { setError(cause instanceof Error ? cause.message : "Delete failed."); }
  }

  return <section className="b-advisories">
    <header><button type="button" onClick={onBack}>← Back to dashboard</button><div><p>COMPONENT B</p><h1>Travel advisories</h1><span>Create and maintain location safety notices.</span></div><button type="button" onClick={() => void load()} disabled={loading}>Refresh</button></header>
    {error && <div role="alert" className="b-advisory-message is-error">{error}</div>}
    {success && <div role="status" className="b-advisory-message is-success">{success}</div>}
    <div className="b-advisory-layout">
      <form className="b-advisory-form" onSubmit={submit}>
        <div className="b-advisory-section-heading"><span className="b-advisory-icon">＋</span><div><h2>{editing ? "Edit advisory" : "Create an advisory"}</h2><p>Share a clear, location specific safety notice.</p></div></div>
        <label>Area name<input required maxLength={200} value={form.areaName} onChange={e => setForm({ ...form, areaName: e.target.value })}/></label>
        <div className="b-advisory-coordinates"><label>Latitude<input required type="number" min={-90} max={90} step="any" value={form.latitude} onChange={e => setForm({ ...form, latitude: e.target.value })}/></label><label>Longitude<input required type="number" min={-180} max={180} step="any" value={form.longitude} onChange={e => setForm({ ...form, longitude: e.target.value })}/></label></div>
        <label>Radius (metres)<input required type="number" min={1} max={100000} value={form.radiusMeters} onChange={e => setForm({ ...form, radiusMeters: e.target.value })}/></label>
        <label>Safety level<select value={form.safetyLevel} onChange={e => setForm({ ...form, safetyLevel: e.target.value })}>{labels.map((name, i) => <option key={name} value={i}>{name}</option>)}</select></label>
        <label>Reason<textarea required minLength={3} maxLength={1000} rows={3} value={form.reason} onChange={e => setForm({ ...form, reason: e.target.value })}/></label>
        <label>Expires at (optional)<input type="datetime-local" value={form.expiresAt} onChange={e => setForm({ ...form, expiresAt: e.target.value })}/></label>
        <div className="b-advisory-form-actions"><button disabled={saving}>{saving ? "Saving…" : editing ? "Save changes" : "Create advisory"}</button>{editing && <button type="button" onClick={() => { setEditing(null); setForm(initial); }}>Cancel edit</button>}</div>
      </form>
      <div className="b-advisory-list">
        <div className="b-advisory-list-heading"><div><h2>Published advisories</h2><p>Review active and past safety notices.</p></div><span className="b-advisory-count">{items.length} {items.length === 1 ? "notice" : "notices"}</span></div>
        <div className="b-advisory-filters"><input aria-label="Search advisories" placeholder="Search area or reason" value={query} onChange={e => { setQuery(e.target.value); setPage(0); }}/><select aria-label="Filter safety level" value={level} onChange={e => { setLevel(e.target.value); setPage(0); }}><option value="all">All levels</option>{labels.map((name, i) => <option key={name} value={i}>{name}</option>)}</select><select aria-label="Sort advisories" value={sort} onChange={e => { setSort(e.target.value); setPage(0); }}><option value="newest">Newest first</option><option value="oldest">Oldest first</option></select></div>
        {loading ? <p className="b-advisory-empty">Loading advisories…</p> : filtered.length === 0 ? <p className="b-advisory-empty">{items.length ? "No advisories match these filters." : "No advisories have been added."}</p> : <>
          <ul>{filtered.slice(page * 8, (page + 1) * 8).map(item => <li key={item.id}><div><span className={`b-advisory-level level-${item.safetyLevel}`}>{labels[item.safetyLevel]}</span><h3>{item.areaName}</h3><p>{item.reason}</p><small>{item.latitude.toFixed(4)}, {item.longitude.toFixed(4)} · {item.radiusMeters} m · {item.expiresAt ? `Expires ${new Date(item.expiresAt).toLocaleString()}` : "No expiry"}</small></div><div className="b-advisory-actions"><button onClick={() => startEdit(item)}>Edit</button><button onClick={() => void remove(item)}>Delete</button></div></li>)}</ul>
          {filtered.length > 8 && <nav aria-label="Advisory pages"><button disabled={!page} onClick={() => setPage(page - 1)}>Previous</button><span>Page {page + 1} of {pages}</span><button disabled={page + 1 >= pages} onClick={() => setPage(page + 1)}>Next</button></nav>}
        </>}
      </div>
    </div>
  </section>;
}
