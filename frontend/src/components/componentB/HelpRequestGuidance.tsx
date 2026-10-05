import { useCallback, useEffect, useState } from "react";
import { authFetch } from "./api";
import "./HelpRequestGuidance.css";

interface MessageDto {
  id: string;
  message: string | null;
  doItems: string[];
  dontItems: string[];
  isCritical: boolean;
  createdAt: string;
}

// Ready-made advice, so a manager does not write it under pressure. Picking one
// fills the lists, which can then be edited before sending.
const TEMPLATES: { label: string; doItems: string[]; dontItems: string[] }[] = [
  {
    label: "Flood",
    doItems: [
      "Move to the highest floor or high ground",
      "Keep your phone charged and stay reachable",
      "Keep drinking water, medicines and a torch with you",
    ],
    dontItems: [
      "Do not walk or drive through flood water",
      "Do not touch electrical items or fuses when wet",
      "Do not go outside unless you are told to evacuate",
    ],
  },
  {
    label: "Landslide",
    doItems: [
      "Move away from the slope to stable ground",
      "Listen for cracking or rumbling sounds and move if you hear them",
      "Stay alert for further movement",
    ],
    dontItems: ["Do not stay below or near the slope", "Do not cross damaged roads or bridges"],
  },
  {
    label: "Fire",
    doItems: [
      "Leave the building now and stay low if there is smoke",
      "Wait at a safe point away from the building",
    ],
    dontItems: ["Do not use lifts", "Do not go back inside for belongings"],
  },
  {
    label: "Medical",
    doItems: [
      "Keep the person still and comfortable",
      "Press a clean cloth firmly on any bleeding",
      "Note their symptoms and any medicines they take",
    ],
    dontItems: [
      "Do not give food or drink if they are drowsy",
      "Do not move someone with a suspected head, neck or back injury",
    ],
  },
  {
    label: "Trapped or stranded",
    doItems: [
      "Stay where you are and make yourself seen or heard",
      "Signal with a torch or a bright cloth",
      "Save phone battery: lower the brightness and use messages",
    ],
    dontItems: ["Do not try to cross water or debris alone"],
  },
];

const lines = (text: string) => text.split("\n").map((line) => line.trim()).filter(Boolean);

function formatTime(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

// What the response team tells the citizen about their request. The citizen reads it
// in the Help section of the mobile app and is notified when something new arrives.
export default function HelpRequestGuidance({ requestId }: { requestId: string }) {
  const [messages, setMessages] = useState<MessageDto[]>([]);
  const [loadFailed, setLoadFailed] = useState(false);
  const [note, setNote] = useState("");
  const [doText, setDoText] = useState("");
  const [dontText, setDontText] = useState("");
  const [critical, setCritical] = useState(false);
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      const res = await authFetch(`/api/HelpRequests/${requestId}/messages`);
      if (!res.ok) throw new Error();
      const data: unknown = await res.json();
      setMessages(Array.isArray(data) ? (data as MessageDto[]) : []);
      setLoadFailed(false);
    } catch {
      setMessages([]);
      setLoadFailed(true);
    }
  }, [requestId]);

  useEffect(() => {
    load();
  }, [load]);

  function applyTemplate(label: string) {
    const template = TEMPLATES.find((candidate) => candidate.label === label);
    if (!template) return;
    setDoText(template.doItems.join("\n"));
    setDontText(template.dontItems.join("\n"));
  }

  async function send() {
    const doItems = lines(doText);
    const dontItems = lines(dontText);
    if (!note.trim() && doItems.length === 0 && dontItems.length === 0) {
      setError("Write a note or add at least one thing to do or not to do.");
      return;
    }

    setSending(true);
    setError(null);
    try {
      const res = await authFetch(`/api/HelpRequests/${requestId}/messages`, {
        method: "POST",
        body: JSON.stringify({ message: note.trim() || null, doItems, dontItems, isCritical: critical }),
      });
      if (!res.ok) {
        // The API says what is wrong (for example an item that is too long).
        const body = await res.json().catch(() => null) as { message?: string } | null;
        throw new Error(body?.message ?? "Could not send the message. Try again.");
      }
      setNote("");
      setDoText("");
      setDontText("");
      setCritical(false);
      await load();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Could not send the message. Try again.");
    } finally {
      setSending(false);
    }
  }

  async function takeBack(id: string) {
    try {
      const res = await authFetch(`/api/HelpRequests/${requestId}/messages/${id}`, { method: "DELETE" });
      if (!res.ok) throw new Error();
      await load();
    } catch {
      setError("Could not take that message back. Try again.");
    }
  }

  return (
    <section className="hr-guidance" aria-label="Guidance for the citizen">
      <span className="hr-actions-label">Guidance and messages to the citizen</span>
      <p className="hr-guidance-hint">
        The citizen reads this in the Help section of the app and gets a notification.
      </p>

      <div className="hr-guidance-form">
        <select
          aria-label="Insert a template"
          value=""
          onChange={(event) => applyTemplate(event.target.value)}
        >
          <option value="">Start from a template…</option>
          {TEMPLATES.map((template) => <option key={template.label} value={template.label}>{template.label}</option>)}
        </select>

        <label>
          <span>Note</span>
          <textarea
            value={note}
            onChange={(event) => setNote(event.target.value)}
            maxLength={1000}
            rows={2}
            placeholder="e.g. A team has been informed. Keep your phone on."
          />
        </label>
        <div className="hr-guidance-lists">
          <label>
            <span className="hr-guidance-do">Do (one per line)</span>
            <textarea value={doText} onChange={(event) => setDoText(event.target.value)} rows={4} />
          </label>
          <label>
            <span className="hr-guidance-dont">Don't (one per line)</span>
            <textarea value={dontText} onChange={(event) => setDontText(event.target.value)} rows={4} />
          </label>
        </div>

        <label className="hr-guidance-critical">
          <input type="checkbox" checked={critical} onChange={(event) => setCritical(event.target.checked)} />
          Urgent: show first and notify as important
        </label>
        {error && <p className="hr-guidance-error" role="alert">{error}</p>}
        <button type="button" className="hr-guidance-send" disabled={sending} onClick={send}>
          {sending ? "Sending…" : "Send to citizen"}
        </button>
      </div>

      <div className="hr-guidance-sent">
        <span className="hr-actions-label">Sent</span>
        {loadFailed && <p className="hr-guidance-empty">Could not load the messages sent so far.</p>}
        {!loadFailed && messages.length === 0 && <p className="hr-guidance-empty">Nothing sent yet.</p>}
        {messages.map((m) => (
          <article key={m.id} className={`hr-guidance-msg ${m.isCritical ? "hr-guidance-msg--critical" : ""}`}>
            <header>
              <span>{m.isCritical ? "Urgent · " : ""}{formatTime(m.createdAt)}</span>
              <button type="button" onClick={() => takeBack(m.id)}>Take back</button>
            </header>
            {m.message && <p>{m.message}</p>}
            {m.doItems.length > 0 && (
              <ul className="hr-guidance-do-list">{m.doItems.map((item, i) => <li key={i}>{item}</li>)}</ul>
            )}
            {m.dontItems.length > 0 && (
              <ul className="hr-guidance-dont-list">{m.dontItems.map((item, i) => <li key={i}>{item}</li>)}</ul>
            )}
          </article>
        ))}
      </div>
    </section>
  );
}
