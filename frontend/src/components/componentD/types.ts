// Mirrors RescueSriLanka.Api's Component D DTOs and enums.
// Enum string values must match the C# enum member names exactly —
// the API serializes enums as strings (see frontend-ui-guide.md §6).

export type SkillType =
  | "WaterRescue"
  | "FirstAid"
  | "Paramedic"
  | "StructuralCollapse"
  | "FireResponse"
  | "Logistics"
  | "Driving";

export type TeamStatus = "Available" | "OnMission" | "OffDuty";

export type VehicleType = "Ambulance" | "Boat" | "FireTruck" | "FourByFour" | "Truck";

export type VehicleStatus = "Available" | "InUse" | "UnderMaintenance";

export type DispatchStatus =
  | "Pending"
  | "Dispatched"
  | "EnRoute"
  | "OnScene"
  | "Resolved"
  | "Cancelled";

export type ApprovalStatus = "PendingApproval" | "Approved" | "Rejected" | "Revised";
export type AssignmentStatus = "Proposed" | "PendingApproval" | "Approved" | "Rejected";
export type SafetyValidationDecision = "APPROVE" | "REVISE" | "REJECT";
export type WorkflowStatus = "Planning" | "AwaitingApproval" | "Approved" | "Rejected" | "Executing" | "Completed" | "Failed";

// ---- Entities ----

export interface TeamMemberDto {
  id: string;
  fullName: string;
  phone: string;
  skill: SkillType;
  isAvailable: boolean;
}

export interface VehicleDto {
  id: string;
  plateNumber: string;
  type: VehicleType;
  status: VehicleStatus;
  capacity: number;
}

export interface RescueTeamDto {
  id: string;
  name: string;
  status: TeamStatus;
  baseLatitude: number | null;
  baseLongitude: number | null;
  members: TeamMemberDto[];
  vehicles: VehicleDto[];
}

export interface AssignmentDto {
  id: string;
  incidentId: string | null;
  helpRequestId: string | null;
  rescueTeamId: string;
  rescueTeamName: string;
  vehicleId: string | null;
  vehiclePlateNumber: string;
  requiredSkill: SkillType;
  requiredCapacity: number;
  status: AssignmentStatus;
  planVersion: number;
  assignedAt: string;
  notes: string | null;
  dispatchId: string | null;
}

export interface DispatchDto {
  id: string;
  assignmentId: string;
  status: DispatchStatus;
  approvalStatus: ApprovalStatus;
  approvedByUserId: string | null;
  approvedAt: string | null;
  dispatchedAt: string | null;
  enRouteAt: string | null;
  onSceneAt: string | null;
  resolvedAt: string | null;
  cancelledAt: string | null;
  notes: string | null;
}

export interface TeamMatchResultDto {
  rescueTeamId: string;
  teamName: string;
  matchingAvailableMembers: number;
  distanceKm: number | null;
  score: number;
}

export interface SafetyValidationResultDto {
  passed: boolean;
  issues: string[];
  checkedAt: string;
}

// ---- Request bodies ----

export interface CreateRescueTeamRequest {
  name: string;
  baseLatitude?: number | null;
  baseLongitude?: number | null;
}

export interface CreateTeamMemberRequest {
  fullName: string;
  phone: string;
  skill: SkillType;
}

export interface UpdateTeamMemberRequest extends CreateTeamMemberRequest { isAvailable: boolean }

export interface CreateVehicleRequest {
  plateNumber: string;
  type: VehicleType;
  capacity: number;
}

export interface UpdateVehicleRequest extends CreateVehicleRequest { status: VehicleStatus }

export interface MatchRequest {
  requiredSkill: SkillType;
  latitude?: number | null;
  longitude?: number | null;
  minCapacity?: number | null;
}

export interface CreateAssignmentRequest {
  incidentId?: string | null;
  helpRequestId?: string | null;
  rescueTeamId: string;
  vehicleId: string;
  requiredSkill: SkillType;
  requiredCapacity: number;
  notes?: string | null;
}

export interface ReviseAssignmentRequest {
  rescueTeamId: string;
  vehicleId: string;
  requiredSkill: SkillType;
  requiredCapacity: number;
  notes?: string | null;
}

export interface SafetyValidationCheckDto { name: string; passed: boolean; reason: string; details?: unknown }
export interface SafetyValidationWorkflowResultDto {
  workflowId: string | null; assignmentId: string; planVersion: number | null;
  decision: SafetyValidationDecision; summary: string; checks: SafetyValidationCheckDto[];
  failedChecks: string[]; suggestedActions: string[]; workflowStatus: WorkflowStatus; isStale: boolean;
}
export interface CoordinatorDecisionRequest { workflowId: string; planVersion: number; decision: SafetyValidationDecision; notes?: string | null }
export interface CoordinatorDecisionResultDto { success: boolean; idempotent: boolean; error: string | null; dispatch: DispatchDto | null; assignmentStatus: AssignmentStatus; teamStatus: TeamStatus | null; vehicleStatus: VehicleStatus | null }

export interface CreateDispatchRequest {
  assignmentId: string;
  notes?: string | null;
}

export interface ApproveDispatchRequest {
  approve: boolean;
  notes?: string | null;
}

export interface TransitionDispatchStatusRequest {
  newStatus: DispatchStatus;
  notes?: string | null;
}

// Display helpers, following the pattern in pages/Dashboard/severity.ts
export const DISPATCH_STATUS_LABEL: Record<DispatchStatus, string> = {
  Pending: "Pending",
  Dispatched: "Dispatched",
  EnRoute: "En route",
  OnScene: "On scene",
  Resolved: "Resolved",
  Cancelled: "Cancelled",
};

export const APPROVAL_STATUS_LABEL: Record<ApprovalStatus, string> = {
  PendingApproval: "Awaiting approval",
  Approved: "Approved",
  Rejected: "Rejected",
  Revised: "Revised",
};

// badge--ok | warn | bad, per the reusable class table in the UI guide
export const DISPATCH_STATUS_BADGE: Record<DispatchStatus, "ok" | "warn" | "bad"> = {
  Pending: "warn",
  Dispatched: "ok",
  EnRoute: "ok",
  OnScene: "ok",
  Resolved: "ok",
  Cancelled: "bad",
};

export const APPROVAL_STATUS_BADGE: Record<ApprovalStatus, "ok" | "warn" | "bad"> = {
  PendingApproval: "warn",
  Approved: "ok",
  Rejected: "bad",
  Revised: "warn",
};

export const TEAM_STATUS_BADGE: Record<TeamStatus, "ok" | "warn" | "bad"> = {
  Available: "ok",
  OnMission: "warn",
  OffDuty: "bad",
};

export const VEHICLE_STATUS_BADGE: Record<VehicleStatus, "ok" | "warn" | "bad"> = {
  Available: "ok",
  InUse: "warn",
  UnderMaintenance: "bad",
};

export const SKILL_LABEL: Record<SkillType, string> = {
  WaterRescue: "Water rescue",
  FirstAid: "First aid",
  Paramedic: "Paramedic",
  StructuralCollapse: "Structural collapse",
  FireResponse: "Fire response",
  Logistics: "Logistics",
  Driving: "Driving",
};
