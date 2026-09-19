// Rescue Coordination tab (Component D: Rescue Team & Emergency Response
// Coordination). Self-contained: fetches its own data rather than
// receiving it as props from Dashboard.tsx, since this domain (teams /
// assignments / dispatches) is separate from the incidents data Dashboard
// already loads for the other tabs.
//
// ASSUMPTION: apiFetch<T>(path, init?) — adds the bearer token, throws on
// a non-OK response, and returns the parsed JSON body as T. If the real
// api/client.ts has a different signature, adjust the calls below —
// nothing else in this file depends on its internals.

import { useEffect, useState, useCallback } from "react";
import { apiFetch } from "../../../api/client";
import {
  RescueTeamDto,
  DispatchDto,
  AssignmentDto,
  TeamMatchResultDto,
  SafetyValidationResultDto,
  SkillType,
  VehicleType,
  MatchRequest,
  CreateRescueTeamRequest,
  CreateTeamMemberRequest,
  CreateVehicleRequest,
  CreateAssignmentRequest,
  CreateDispatchRequest,
  ApproveDispatchRequest,
  TransitionDispatchStatusRequest,
  DispatchStatus,
  DISPATCH_STATUS_LABEL,
  DISPATCH_STATUS_BADGE,
  APPROVAL_STATUS_LABEL,
  APPROVAL_STATUS_BADGE,
  TEAM_STATUS_BADGE,
  VEHICLE_STATUS_BADGE,
  SKILL_LABEL,
} from "../../../types/rescueCoordination";
import "./RescueCoordinationSection.css";

const SKILLS: SkillType[] = [
  "WaterRescue",
  "FirstAid",
  "Paramedic",
  "StructuralCollapse",
  "FireResponse",
  "Logistics",
  "Driving",
];

const VEHICLE_TYPES: VehicleType[] = ["Ambulance", "Boat", "FireTruck", "FourByFour", "Truck"];

// Forward transitions offered as buttons for a given current status.
// Mirrors DispatchService.AllowedTransitions on the backend.
const NEXT_STATUS: Partial<Record<DispatchStatus, DispatchStatus[]>> = {
  Pending: ["Dispatched", "Cancelled"],
  Dispatched: ["EnRoute", "Cancelled"],
  EnRoute: ["OnScene", "Cancelled"],
  OnScene: ["Resolved", "Cancelled"],
};

export default function RescueCoordinationSection() {
  const [teams, setTeams] = useState<RescueTeamDto[]>([]);
  const [assignments, setAssignments] = useState<AssignmentDto[]>([]);
  const [dispatches, setDispatches] = useState<DispatchDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const loadAll = useCallback(async (signal?: AbortSignal) => {
    setLoading(true);
    setError(null);
    try {
      const [teamsData, assignmentsData, dispatchesData] = await Promise.all([
        apiFetch<RescueTeamDto[]>("/api/rescueteams", { signal }),
        apiFetch<AssignmentDto[]>("/api/assignments", { signal }),
        apiFetch<DispatchDto[]>("/api/dispatches", { signal }),
      ]);
      if (signal?.aborted) return;
      setTeams(teamsData);
      setAssignments(assignmentsData);
      setDispatches(dispatchesData);
    } catch (err) {
      if (signal?.aborted) return;
      setError(err instanceof Error ? err.message : "Failed to load rescue coordination data.");
    } finally {
      if (!signal?.aborted) setLoading(false);
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    loadAll(controller.signal);
    return () => controller.abort();
  }, [loadAll]);

  return (
    <div className="rescue-coord">
      {error && <div className="alert">{error}</div>}

      <div className="rescue-coord__stat-row stat-row">
        <div className="stat">
          <div className="stat__value">{teams.length}</div>
          <div className="stat__label">Rescue teams</div>
        </div>
        <div className="stat">
          <div className="stat__value">{teams.filter((t) => t.status === "Available").length}</div>
          <div className="stat__label">Teams available</div>
        </div>
        <div className="stat stat--caution">
          <div className="stat__value">
            {dispatches.filter((d) => d.approvalStatus === "PendingApproval").length}
          </div>
          <div className="stat__label">Awaiting approval</div>
        </div>
        <div className="stat">
          <div className="stat__value">
            {teams.reduce((sum, t) => sum + t.vehicles.filter((v) => v.status === "Available").length, 0)}
          </div>
          <div className="stat__label">Vehicles available</div>
        </div>
      </div>

      <div className="rescue-coord__grid">
        <TeamsPanel teams={teams} loading={loading} onChanged={() => loadAll()} />
        <MatchFinderPanel onCreated={() => loadAll()} teams={teams} />
      </div>

      <DispatchQueuePanel
        dispatches={dispatches}
        assignments={assignments}
        loading={loading}
        onChanged={() => loadAll()}
      />
    </div>
  );
}

// ---------------------------------------------------------------------
// Teams roster panel
// ---------------------------------------------------------------------

function TeamsPanel({
  teams,
  loading,
  onChanged,
}: {
  teams: RescueTeamDto[];
  loading: boolean;
  onChanged: () => void;
}) {
  const [showNewTeam, setShowNewTeam] = useState(false);
  const [newTeamName, setNewTeamName] = useState("");
  const [busy, setBusy] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  const createTeam = async () => {
    if (!newTeamName.trim()) return;
    setBusy(true);
    setFormError(null);
    try {
      const body: CreateRescueTeamRequest = { name: newTeamName.trim() };
      await apiFetch<RescueTeamDto>("/api/rescueteams", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
      setNewTeamName("");
      setShowNewTeam(false);
      onChanged();
    } catch (err) {
      setFormError(err instanceof Error ? err.message : "Failed to create team.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="panel rescue-coord__teams">
      <div className="panel__head">
        <h3 className="panel__title">Rescue teams</h3>
        <button className="btn-small btn-ghost" onClick={() => setShowNewTeam((s) => !s)}>
          {showNewTeam ? "Cancel" : "+ New team"}
        </button>
      </div>

      {showNewTeam && (
        <div className="rescue-coord__inline-form">
          <input
            type="text"
            placeholder="Team name"
            value={newTeamName}
            onChange={(e) => setNewTeamName(e.target.value)}
          />
          <button className="btn-small" onClick={createTeam} disabled={busy || !newTeamName.trim()}>
            {busy ? "Creating…" : "Create"}
          </button>
          {formError && <div className="alert">{formError}</div>}
        </div>
      )}

      {loading && teams.length === 0 ? (
        <p className="empty">Loading teams…</p>
      ) : teams.length === 0 ? (
        <p className="empty">No rescue teams yet. Create one to get started.</p>
      ) : (
        <ul className="rescue-coord__team-list">
          {teams.map((team) => (
            <TeamCard key={team.id} team={team} onChanged={onChanged} />
          ))}
        </ul>
      )}
    </section>
  );
}

function TeamCard({ team, onChanged }: { team: RescueTeamDto; onChanged: () => void }) {
  const [expanded, setExpanded] = useState(false);
  const [addingMember, setAddingMember] = useState(false);
  const [addingVehicle, setAddingVehicle] = useState(false);

  return (
    <li className="rescue-coord__team-card">
      <button className="rescue-coord__team-card-head" onClick={() => setExpanded((e) => !e)}>
        <span className="rescue-coord__team-name">{team.name}</span>
        <span className={`badge badge--${TEAM_STATUS_BADGE[team.status]}`}>{team.status}</span>
        <span className="rescue-coord__team-meta">
          {team.members.length} members · {team.vehicles.length} vehicles
        </span>
      </button>

      {expanded && (
        <div className="rescue-coord__team-detail">
          <div className="rescue-coord__team-section-head">
            <h4>Members</h4>
            <button className="btn-small btn-ghost" onClick={() => setAddingMember((s) => !s)}>
              {addingMember ? "Cancel" : "+ Add member"}
            </button>
          </div>
          {addingMember && <AddMemberForm teamId={team.id} onDone={() => { setAddingMember(false); onChanged(); }} />}
          {team.members.length === 0 ? (
            <p className="empty">No members yet.</p>
          ) : (
            <ul className="rescue-coord__member-list">
              {team.members.map((m) => (
                <li key={m.id}>
                  <span className="rescue-coord__member-name">{m.fullName}</span>
                  <span className="chip chip--safe">{SKILL_LABEL[m.skill]}</span>
                  <span className={`badge badge--${m.isAvailable ? "ok" : "warn"}`}>
                    {m.isAvailable ? "Available" : "Unavailable"}
                  </span>
                </li>
              ))}
            </ul>
          )}

          <div className="rescue-coord__team-section-head">
            <h4>Vehicles</h4>
            <button className="btn-small btn-ghost" onClick={() => setAddingVehicle((s) => !s)}>
              {addingVehicle ? "Cancel" : "+ Add vehicle"}
            </button>
          </div>
          {addingVehicle && <AddVehicleForm teamId={team.id} onDone={() => { setAddingVehicle(false); onChanged(); }} />}
          {team.vehicles.length === 0 ? (
            <p className="empty">No vehicles yet.</p>
          ) : (
            <ul className="rescue-coord__vehicle-list">
              {team.vehicles.map((v) => (
                <li key={v.id}>
                  <span>{v.plateNumber}</span>
                  <span className="rescue-coord__vehicle-type">{v.type}</span>
                  <span className={`badge badge--${VEHICLE_STATUS_BADGE[v.status]}`}>{v.status}</span>
                  <span className="rescue-coord__vehicle-capacity">Cap. {v.capacity}</span>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </li>
  );
}

function AddMemberForm({ teamId, onDone }: { teamId: string; onDone: () => void }) {
  const [fullName, setFullName] = useState("");
  const [phone, setPhone] = useState("");
  const [skill, setSkill] = useState<SkillType>("FirstAid");
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const submit = async () => {
    if (!fullName.trim() || !phone.trim()) return;
    setBusy(true);
    setErr(null);
    try {
      const body: CreateTeamMemberRequest = { fullName: fullName.trim(), phone: phone.trim(), skill };
      await apiFetch(`/api/rescueteams/${teamId}/members`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
      onDone();
    } catch (e) {
      setErr(e instanceof Error ? e.message : "Failed to add member.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="rescue-coord__inline-form">
      <input type="text" placeholder="Full name" value={fullName} onChange={(e) => setFullName(e.target.value)} />
      <input type="text" placeholder="Phone" value={phone} onChange={(e) => setPhone(e.target.value)} />
      <select value={skill} onChange={(e) => setSkill(e.target.value as SkillType)}>
        {SKILLS.map((s) => (
          <option key={s} value={s}>{SKILL_LABEL[s]}</option>
        ))}
      </select>
      <button className="btn-small" onClick={submit} disabled={busy || !fullName.trim() || !phone.trim()}>
        {busy ? "Adding…" : "Add"}
      </button>
      {err && <div className="alert">{err}</div>}
    </div>
  );
}

function AddVehicleForm({ teamId, onDone }: { teamId: string; onDone: () => void }) {
  const [plateNumber, setPlateNumber] = useState("");
  const [type, setType] = useState<VehicleType>("Ambulance");
  const [capacity, setCapacity] = useState(4);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const submit = async () => {
    if (!plateNumber.trim()) return;
    setBusy(true);
    setErr(null);
    try {
      const body: CreateVehicleRequest = { plateNumber: plateNumber.trim(), type, capacity };
      await apiFetch(`/api/rescueteams/${teamId}/vehicles`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
      onDone();
    } catch (e) {
      setErr(e instanceof Error ? e.message : "Failed to add vehicle.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="rescue-coord__inline-form">
      <input type="text" placeholder="Plate number" value={plateNumber} onChange={(e) => setPlateNumber(e.target.value)} />
      <select value={type} onChange={(e) => setType(e.target.value as VehicleType)}>
        {VEHICLE_TYPES.map((t) => (
          <option key={t} value={t}>{t}</option>
        ))}
      </select>
      <input
        type="number"
        min={1}
        placeholder="Capacity"
        value={capacity}
        onChange={(e) => setCapacity(Number(e.target.value))}
      />
      <button className="btn-small" onClick={submit} disabled={busy || !plateNumber.trim()}>
        {busy ? "Adding…" : "Add"}
      </button>
      {err && <div className="alert">{err}</div>}
    </div>
  );
}

// ---------------------------------------------------------------------
// Match finder — the skill/availability matching business operation
// ---------------------------------------------------------------------

function MatchFinderPanel({
  teams,
  onCreated,
}: {
  teams: RescueTeamDto[];
  onCreated: () => void;
}) {
  const [skill, setSkill] = useState<SkillType>("WaterRescue");
  const [results, setResults] = useState<TeamMatchResultDto[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const [assigning, setAssigning] = useState<string | null>(null);

  const findMatches = async () => {
    setBusy(true);
    setErr(null);
    setResults(null);
    try {
      const body: MatchRequest = { requiredSkill: skill };
      const data = await apiFetch<TeamMatchResultDto[]>("/api/assignments/match", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
      setResults(data);
    } catch (e) {
      setErr(e instanceof Error ? e.message : "Matching failed.");
    } finally {
      setBusy(false);
    }
  };

  const assignTeam = async (rescueTeamId: string) => {
    setAssigning(rescueTeamId);
    setErr(null);
    try {
      const body: CreateAssignmentRequest = { rescueTeamId, requiredSkill: skill };
      await apiFetch<AssignmentDto>("/api/assignments", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
      onCreated();
    } catch (e) {
      setErr(e instanceof Error ? e.message : "Failed to create assignment.");
    } finally {
      setAssigning(null);
    }
  };

  return (
    <section className="panel rescue-coord__match">
      <div className="panel__head">
        <h3 className="panel__title">Find a matching team</h3>
        <span className="panel__meta">by skill &amp; availability</span>
      </div>

      <div className="rescue-coord__inline-form">
        <select value={skill} onChange={(e) => setSkill(e.target.value as SkillType)}>
          {SKILLS.map((s) => (
            <option key={s} value={s}>{SKILL_LABEL[s]}</option>
          ))}
        </select>
        <button className="btn-small" onClick={findMatches} disabled={busy || teams.length === 0}>
          {busy ? "Searching…" : "Find matches"}
        </button>
      </div>

      {err && <div className="alert">{err}</div>}

      {results !== null && (
        results.length === 0 ? (
          <p className="empty">No available team currently has this skill.</p>
        ) : (
          <ul className="rescue-coord__match-list">
            {results.map((r) => (
              <li key={r.rescueTeamId}>
                <span className="rescue-coord__match-team">{r.teamName}</span>
                <span className="panel__meta">
                  {r.matchingAvailableMembers} available · score {r.score}
                  {r.distanceKm != null ? ` · ${r.distanceKm.toFixed(1)} km` : ""}
                </span>
                <button
                  className="btn-small"
                  onClick={() => assignTeam(r.rescueTeamId)}
                  disabled={assigning === r.rescueTeamId}
                >
                  {assigning === r.rescueTeamId ? "Assigning…" : "Assign"}
                </button>
              </li>
            ))}
          </ul>
        )
      )}
    </section>
  );
}

// ---------------------------------------------------------------------
// Dispatch queue — status workflow + human approval gate
// ---------------------------------------------------------------------

function DispatchQueuePanel({
  dispatches,
  assignments,
  loading,
  onChanged,
}: {
  dispatches: DispatchDto[];
  assignments: AssignmentDto[];
  loading: boolean;
  onChanged: () => void;
}) {
  const assignmentById = new Map(assignments.map((a) => [a.id, a]));

  return (
    <section className="panel rescue-coord__dispatch-queue">
      <div className="panel__head">
        <h3 className="panel__title">Dispatch queue</h3>
        <span className="panel__meta">status workflow &amp; approval</span>
      </div>

      {loading && dispatches.length === 0 ? (
        <p className="empty">Loading dispatches…</p>
      ) : dispatches.length === 0 ? (
        <p className="empty">No dispatches yet. Assign a team above, then create a dispatch for it.</p>
      ) : (
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th>Team</th>
                <th>Status</th>
                <th>Approval</th>
                <th className="col-wide">Actions</th>
              </tr>
            </thead>
            <tbody>
              {dispatches.map((d) => (
                <DispatchRow
                  key={d.id}
                  dispatch={d}
                  assignment={assignmentById.get(d.assignmentId)}
                  onChanged={onChanged}
                />
              ))}
            </tbody>
          </table>
        </div>
      )}

      <CreateDispatchForm assignments={assignments} dispatches={dispatches} onCreated={onChanged} />
    </section>
  );
}

function DispatchRow({
  dispatch,
  assignment,
  onChanged,
}: {
  dispatch: DispatchDto;
  assignment: AssignmentDto | undefined;
  onChanged: () => void;
}) {
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const approve = async (approve: boolean) => {
    setBusy(true);
    setErr(null);
    try {
      const body: ApproveDispatchRequest = {
        approvedByUserId: "current-coordinator", // TODO: wire to real signed-in user id
        approve,
      };
      await apiFetch<DispatchDto>(`/api/dispatches/${dispatch.id}/approve`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
      onChanged();
    } catch (e) {
      setErr(e instanceof Error ? e.message : "Approval action failed.");
    } finally {
      setBusy(false);
    }
  };

  const transition = async (newStatus: DispatchStatus) => {
    setBusy(true);
    setErr(null);
    try {
      const body: TransitionDispatchStatusRequest = { newStatus };
      await apiFetch<DispatchDto>(`/api/dispatches/${dispatch.id}/status`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
      onChanged();
    } catch (e) {
      setErr(e instanceof Error ? e.message : "Status update failed.");
    } finally {
      setBusy(false);
    }
  };

  const nextOptions = NEXT_STATUS[dispatch.status] ?? [];

  return (
    <tr>
      <td className="cell-title">{assignment?.rescueTeamName ?? "—"}</td>
      <td>
        <span className={`badge badge--${DISPATCH_STATUS_BADGE[dispatch.status]}`}>
          {DISPATCH_STATUS_LABEL[dispatch.status]}
        </span>
      </td>
      <td>
        <span className={`badge badge--${APPROVAL_STATUS_BADGE[dispatch.approvalStatus]}`}>
          {APPROVAL_STATUS_LABEL[dispatch.approvalStatus]}
        </span>
      </td>
      <td>
        <div className="rescue-coord__row-actions">
          {dispatch.approvalStatus === "PendingApproval" && (
            <>
              <button className="btn-approve" onClick={() => approve(true)} disabled={busy}>
                {busy ? "…" : "Approve"}
              </button>
              <button className="btn-reject" onClick={() => approve(false)} disabled={busy}>
                Reject
              </button>
            </>
          )}
          {nextOptions.map((s) => (
            <button key={s} className="btn-small btn-ghost" onClick={() => transition(s)} disabled={busy}>
              → {DISPATCH_STATUS_LABEL[s]}
            </button>
          ))}
          {err && <div className="alert">{err}</div>}
        </div>
      </td>
    </tr>
  );
}

function CreateDispatchForm({
  assignments,
  dispatches,
  onCreated,
}: {
  assignments: AssignmentDto[];
  dispatches: DispatchDto[];
  onCreated: () => void;
}) {
  const dispatchedAssignmentIds = new Set(dispatches.map((d) => d.assignmentId));
  const available = assignments.filter((a) => !dispatchedAssignmentIds.has(a.id));

  const [selected, setSelected] = useState<string>("");
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const [validation, setValidation] = useState<SafetyValidationResultDto | null>(null);

  if (available.length === 0) return null;

  const createDispatch = async () => {
    if (!selected) return;
    setBusy(true);
    setErr(null);
    setValidation(null);
    try {
      const body: CreateDispatchRequest = { assignmentId: selected };
      const result = await apiFetch<{ dispatch: DispatchDto; validation: SafetyValidationResultDto }>(
        "/api/dispatches",
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(body),
        }
      );
      setValidation(result.validation);
      setSelected("");
      onCreated();
    } catch (e) {
      setErr(e instanceof Error ? e.message : "Failed to create dispatch.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="rescue-coord__inline-form rescue-coord__create-dispatch">
      <select value={selected} onChange={(e) => setSelected(e.target.value)}>
        <option value="">Select an assignment to dispatch…</option>
        {available.map((a) => (
          <option key={a.id} value={a.id}>
            {a.rescueTeamName} — {SKILL_LABEL[a.requiredSkill]}
          </option>
        ))}
      </select>
      <button className="btn-small" onClick={createDispatch} disabled={busy || !selected}>
        {busy ? "Validating…" : "Create dispatch"}
      </button>
      {err && <div className="alert">{err}</div>}
      {validation && !validation.passed && (
        <div className="alert">
          Safety Validation Agent flagged issues: {validation.issues.join("; ")}
        </div>
      )}
      {validation && validation.passed && (
        <div className="rescue-coord__validation-ok">Safety Validation Agent: all checks passed.</div>
      )}
    </div>
  );
}
