// TEMPORARY MOCK — replace this file once a teammate builds real
// authentication + api/client.ts. This exists purely so
// RescueCoordinationSection.tsx (and anything else importing apiFetch)
// can be previewed standalone, with no backend or login required.
//
// Real signature to match later: apiFetch<T>(path, init?) => Promise<T>,
// throwing on non-OK responses and attaching a bearer token. Keep this
// signature when the real version replaces it, so no calling code needs
// to change.

let idCounter = 1;
const nextId = () => `mock-${idCounter++}`;

// In-memory fixture data — enough to exercise every part of the UI.
const teams: any[] = [
  {
    id: "team-1",
    name: "Colombo Water Rescue Unit",
    status: "Available",
    members: [
      { id: "mem-1", fullName: "Nimal Perera", phone: "0771234567", skill: "WaterRescue", isAvailable: true },
      { id: "mem-2", fullName: "Kasun Silva", phone: "0777654321", skill: "FirstAid", isAvailable: false },
    ],
    vehicles: [
      { id: "veh-1", plateNumber: "WP-CAB-1234", type: "Boat", status: "Available", capacity: 6 },
    ],
  },
  {
    id: "team-2",
    name: "Kandy Structural Response",
    status: "OnMission",
    members: [
      { id: "mem-3", fullName: "Saman Kumara", phone: "0712223333", skill: "StructuralCollapse", isAvailable: true },
    ],
    vehicles: [
      { id: "veh-2", plateNumber: "CP-TRK-5678", type: "Truck", status: "InUse", capacity: 4 },
    ],
  },
];

const assignments: any[] = [
  { id: "asn-1", rescueTeamId: "team-2", rescueTeamName: "Kandy Structural Response", requiredSkill: "StructuralCollapse", dispatchId: "disp-1" },
];

const dispatches: any[] = [
  { id: "disp-1", assignmentId: "asn-1", status: "Pending", approvalStatus: "PendingApproval" },
];

function delay<T>(value: T, ms = 300): Promise<T> {
  return new Promise((resolve) => setTimeout(() => resolve(value), ms));
}

export async function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
  const method = init?.method ?? "GET";
  const body = init?.body ? JSON.parse(init.body as string) : undefined;

  // --- GET list endpoints ---
  if (method === "GET" && path === "/api/rescueteams") return delay(teams as T);
  if (method === "GET" && path === "/api/assignments") return delay(assignments as T);
  if (method === "GET" && path === "/api/dispatches") return delay(dispatches as T);

  // --- Create team ---
  if (method === "POST" && path === "/api/rescueteams") {
    const team = { id: nextId(), name: body.name, status: "Available", members: [], vehicles: [] };
    teams.push(team);
    return delay(team as T);
  }

  // --- Add member ---
  const memberMatch = path.match(/^\/api\/rescueteams\/([^/]+)\/members$/);
  if (method === "POST" && memberMatch) {
    const team = teams.find((t) => t.id === memberMatch[1]);
    const member = { id: nextId(), fullName: body.fullName, phone: body.phone, skill: body.skill, isAvailable: true };
    team?.members.push(member);
    return delay(member as T);
  }

  // --- Add vehicle ---
  const vehicleMatch = path.match(/^\/api\/rescueteams\/([^/]+)\/vehicles$/);
  if (method === "POST" && vehicleMatch) {
    const team = teams.find((t) => t.id === vehicleMatch[1]);
    const vehicle = { id: nextId(), plateNumber: body.plateNumber, type: body.type, status: "Available", capacity: body.capacity };
    team?.vehicles.push(vehicle);
    return delay(vehicle as T);
  }

  // --- Matching ---
  if (method === "POST" && path === "/api/assignments/match") {
    const matches = teams
      .filter((t) => t.status !== "OffDuty")
      .map((t) => {
        const matching = t.members.filter((m: any) => m.isAvailable && m.skill === body.requiredSkill).length;
        return matching > 0
          ? { rescueTeamId: t.id, teamName: t.name, matchingAvailableMembers: matching, distanceKm: null, score: matching * 10 }
          : null;
      })
      .filter(Boolean);
    return delay(matches as T);
  }

  // --- Create assignment ---
  if (method === "POST" && path === "/api/assignments") {
    const team = teams.find((t) => t.id === body.rescueTeamId);
    const assignment = { id: nextId(), rescueTeamId: body.rescueTeamId, rescueTeamName: team?.name ?? "Unknown", requiredSkill: body.requiredSkill, dispatchId: null };
    assignments.push(assignment);
    return delay(assignment as T);
  }

  // --- Create dispatch ---
  if (method === "POST" && path === "/api/dispatches") {
    const dispatch = { id: nextId(), assignmentId: body.assignmentId, status: "Pending", approvalStatus: "PendingApproval" };
    dispatches.push(dispatch);
    const validation = { passed: true, issues: [] as string[], checkedAt: new Date().toISOString() };
    return delay({ dispatch, validation } as T);
  }

  // --- Approve/reject ---
  const approveMatch = path.match(/^\/api\/dispatches\/([^/]+)\/approve$/);
  if (method === "POST" && approveMatch) {
    const dispatch = dispatches.find((d) => d.id === approveMatch[1]);
    if (dispatch) {
      dispatch.approvalStatus = body.approve ? "Approved" : "Rejected";
      if (!body.approve) dispatch.status = "Cancelled";
    }
    return delay(dispatch as T);
  }

  // --- Status transition ---
  const statusMatch = path.match(/^\/api\/dispatches\/([^/]+)\/status$/);
  if (method === "PATCH" && statusMatch) {
    const dispatch = dispatches.find((d) => d.id === statusMatch[1]);
    if (dispatch) dispatch.status = body.newStatus;
    return delay(dispatch as T);
  }

  throw new Error(`Mock apiFetch: no handler for ${method} ${path}`);
}
