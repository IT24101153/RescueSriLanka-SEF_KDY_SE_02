using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Text.Json;
using RescueSriLanka.Api.Agents.SafetyValidation;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.DTOs;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Services
{
    public interface IDispatchService
    {
        Task<DispatchDto?> GetByIdAsync(Guid id);
        Task<List<DispatchDto>> GetAllAsync();
        Task<(DispatchDto? Dispatch, SafetyValidationResultDto Validation, string? Error)> CreateAsync(CreateDispatchDto dto);
        Task<DispatchDto?> ApproveAsync(Guid dispatchId, string approvedByUserId, ApproveDispatchDto dto);
        Task<CoordinatorDecisionResultDto> DecideAsync(Guid assignmentId, string coordinatorId, CoordinatorDecisionDto dto);
        Task<(bool Success, string? Error, DispatchDto? Dispatch)> TransitionStatusAsync(Guid dispatchId, TransitionDispatchStatusDto dto);
    }

    public class DispatchService : IDispatchService
    {
        private readonly ComponentDDbContext _db;
        private readonly ISafetyValidationAgent _safetyAgent;

        private static readonly Dictionary<DispatchStatus, DispatchStatus[]> AllowedTransitions = new()
        {
            [DispatchStatus.Pending] = new[] { DispatchStatus.Dispatched, DispatchStatus.Cancelled },
            [DispatchStatus.Dispatched] = new[] { DispatchStatus.EnRoute, DispatchStatus.Cancelled },
            [DispatchStatus.EnRoute] = new[] { DispatchStatus.OnScene, DispatchStatus.Cancelled },
            [DispatchStatus.OnScene] = new[] { DispatchStatus.Resolved, DispatchStatus.Cancelled },
            [DispatchStatus.Resolved] = Array.Empty<DispatchStatus>(),
            [DispatchStatus.Cancelled] = Array.Empty<DispatchStatus>()
        };

        public DispatchService(ComponentDDbContext db, ISafetyValidationAgent safetyAgent)
        {
            _db = db;
            _safetyAgent = safetyAgent;
        }

        public async Task<List<DispatchDto>> GetAllAsync()
        {
            var dispatches = await _db.Dispatches.ToListAsync();
            return dispatches.Select(ToDto).ToList();
        }

        public async Task<DispatchDto?> GetByIdAsync(Guid id)
        {
            var d = await _db.Dispatches.FirstOrDefaultAsync(x => x.Id == id);
            return d is null ? null : ToDto(d);
        }

        // FIX: previously, ANY existing dispatch (including a Cancelled
        // one) permanently blocked creating a new dispatch for the same
        // assignment. That meant a rejected dispatch could never be
        // retried — e.g. a team becomes unavailable, the coordinator
        // rejects, the team frees up later, but the assignment is stuck
        // forever. Now: only a non-terminal-Cancelled existing dispatch
        // blocks a new one. Resolved still blocks (the work is done).
        // If the existing dispatch is Cancelled, it's removed and
        // replaced, preserving the 1:1 invariant enforced by the unique
        // index on Dispatch.AssignmentId.
        public async Task<(DispatchDto? Dispatch, SafetyValidationResultDto Validation, string? Error)> CreateAsync(CreateDispatchDto dto)
        {
            var assignment = await _db.Assignments
                .Include(a => a.Dispatch)
                .FirstOrDefaultAsync(a => a.Id == dto.AssignmentId);

            if (assignment is null)
            {
                return (null, EmptyValidation(), "Assignment not found.");
            }

            var validation = await _safetyAgent.ValidateAsync(assignment.Id);
            if (!validation.Passed)
            {
                return (null, validation, "Safety validation failed: " + string.Join(" ", validation.Issues));
            }

            if (assignment.Dispatch is not null)
            {
                if (assignment.Dispatch.Status == DispatchStatus.Cancelled)
                {
                    _db.Dispatches.Remove(assignment.Dispatch);
                    await _db.SaveChangesAsync();
                }
                else
                {
                    var reason = assignment.Dispatch.Status == DispatchStatus.Resolved
                        ? "This assignment has already been resolved."
                        : "This assignment already has an active dispatch.";
                    return (null, EmptyValidation(), reason);
                }
            }

            if (assignment.Status is not AssignmentStatus.Proposed and not AssignmentStatus.Rejected)
            {
                return (null, EmptyValidation(), "Assignment is not in a state that can be submitted for approval.");
            }

            var dispatch = new Dispatch
            {
                AssignmentId = assignment.Id,
                Status = DispatchStatus.Pending,
                ApprovalStatus = ApprovalStatus.PendingApproval,
                Notes = dto.Notes
            };

            _db.Dispatches.Add(dispatch);
            assignment.Status = AssignmentStatus.PendingApproval;
            await _db.SaveChangesAsync();

            return (ToDto(dispatch), validation, null);
        }

        public async Task<DispatchDto?> ApproveAsync(Guid dispatchId, string approvedByUserId, ApproveDispatchDto dto)
        {
            var dispatch = await _db.Dispatches
                .Include(d => d.Assignment)
                .FirstOrDefaultAsync(d => d.Id == dispatchId);
            if (dispatch is null) return null;

            dispatch.ApprovalStatus = dto.Approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
            dispatch.ApprovedByUserId = approvedByUserId;
            dispatch.ApprovedAt = DateTime.UtcNow;
            if (dto.Notes is not null) dispatch.Notes = dto.Notes;

            if (!dto.Approve)
            {
                dispatch.Status = DispatchStatus.Cancelled;
                dispatch.CancelledAt = DateTime.UtcNow;
            }

            if (dispatch.Assignment is not null)
            {
                dispatch.Assignment.Status = dto.Approve
                    ? AssignmentStatus.Approved
                    : AssignmentStatus.Rejected;
            }

            await _db.SaveChangesAsync();
            return ToDto(dispatch);
        }

        public async Task<CoordinatorDecisionResultDto> DecideAsync(Guid assignmentId, string coordinatorId, CoordinatorDecisionDto dto)
        {
            await using var transaction = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable)
                : null;
            try
            {
                var assignment = await _db.Assignments.Include(a => a.RescueTeam).ThenInclude(t => t!.Members)
                    .Include(a => a.Vehicle).Include(a => a.Dispatch).SingleOrDefaultAsync(a => a.Id == assignmentId);
                if (assignment is null) return DecisionFailure("Assignment not found.");
                var workflow = await _db.AgentWorkflows.SingleOrDefaultAsync(w => w.Id == dto.WorkflowId);
                if (workflow is null) return DecisionFailure("Safety validation workflow not found.", assignment);

                if (!TryGetApprovedValidation(workflow, assignment, dto.PlanVersion, out var validationError))
                    return DecisionFailure(validationError, assignment);

                workflow.ApprovedByUserId = Guid.TryParse(coordinatorId, out var id) ? id : null;
                workflow.ApprovalDecisionAt = DateTime.UtcNow;
                workflow.ApprovalNotes = dto.Notes;

                if (dto.Decision == CoordinatorDecision.REJECT)
                {
                    assignment.Status = AssignmentStatus.Rejected;
                    workflow.Status = Models.Agents.WorkflowStatus.Rejected;
                    await _db.SaveChangesAsync();
                    if (transaction is not null) await transaction.CommitAsync();
                    return new(true, false, null, null, assignment.Status, assignment.RescueTeam?.Status, assignment.Vehicle?.Status);
                }
                if (dto.Decision == CoordinatorDecision.REVISE)
                {
                    assignment.Status = AssignmentStatus.Proposed;
                    workflow.Status = Models.Agents.WorkflowStatus.AwaitingApproval;
                    await _db.SaveChangesAsync();
                    if (transaction is not null) await transaction.CommitAsync();
                    return new(true, false, null, null, assignment.Status, assignment.RescueTeam?.Status, assignment.Vehicle?.Status);
                }

                if (assignment.Dispatch is not null)
                {
                    if (assignment.Dispatch.ApprovalStatus == ApprovalStatus.Approved)
                        return new(true, true, null, ToDto(assignment.Dispatch), assignment.Status, assignment.RescueTeam?.Status, assignment.Vehicle?.Status);
                    return DecisionFailure("Assignment already has a dispatch.", assignment);
                }
                if (assignment.Status is not AssignmentStatus.Proposed and not AssignmentStatus.PendingApproval)
                    return DecisionFailure("Assignment is not in an approvable state.", assignment);

                var live = await _safetyAgent.ValidateAsync(assignment.Id);
                if (!live.Passed || assignment.PlanVersion != dto.PlanVersion)
                    return DecisionFailure("Live deterministic safety validation failed; revalidation is required.", assignment);

                if (assignment.RescueTeam is null || assignment.Vehicle is null
                    || assignment.RescueTeam.Status != TeamStatus.Available || assignment.Vehicle.Status != VehicleStatus.Available)
                    return DecisionFailure("Team or vehicle is no longer available.", assignment);

                var dispatch = new Dispatch { AssignmentId = assignment.Id, Status = DispatchStatus.Dispatched,
                    ApprovalStatus = ApprovalStatus.Approved, ApprovedByUserId = coordinatorId, ApprovedAt = DateTime.UtcNow,
                    DispatchedAt = DateTime.UtcNow, Notes = dto.Notes };
                _db.Dispatches.Add(dispatch);
                assignment.Status = AssignmentStatus.Approved;
                assignment.RescueTeam.Status = TeamStatus.OnMission;
                assignment.Vehicle.Status = VehicleStatus.InUse;
                workflow.Status = Models.Agents.WorkflowStatus.Executing;
                workflow.FinalOutcomeJson = JsonSerializer.Serialize(new { assignmentId, planVersion = dto.PlanVersion, coordinatorDecision = "APPROVE", dispatchId = dispatch.Id });
                await _db.SaveChangesAsync();
                if (transaction is not null) await transaction.CommitAsync();
                return new(true, false, null, ToDto(dispatch), assignment.Status, assignment.RescueTeam.Status, assignment.Vehicle.Status);
            }
            catch (DbUpdateException)
            {
                if (transaction is not null) await transaction.RollbackAsync();
                return DecisionFailure("Concurrent or duplicate dispatch commit detected; retry safely.");
            }
            catch
            {
                if (transaction is not null) await transaction.RollbackAsync();
                throw;
            }
        }

        private static bool TryGetApprovedValidation(Models.Agents.AgentWorkflow workflow, Assignment assignment, int planVersion, out string error)
        {
            error = "Safety validation is invalid or stale.";
            if (workflow.Status != Models.Agents.WorkflowStatus.AwaitingApproval) return false;
            try
            {
                var validation = JsonSerializer.Deserialize<SafetyValidationWorkflowResultDto>(workflow.FinalOutcomeJson ?? "{}");
                if (validation is null || validation.Decision != SafetyValidationDecision.APPROVE || validation.IsStale
                    || validation.AssignmentId != assignment.Id || validation.PlanVersion != planVersion
                    || validation.Checks.Count != 10 || validation.Checks.Any(c => !c.Passed)) return false;
                return true;
            }
            catch (JsonException) { return false; }
        }

        private static CoordinatorDecisionResultDto DecisionFailure(string error, Assignment? assignment = null) => new(false, false, error, null,
            assignment?.Status ?? AssignmentStatus.Proposed, assignment?.RescueTeam?.Status, assignment?.Vehicle?.Status);

        public async Task<(bool Success, string? Error, DispatchDto? Dispatch)> TransitionStatusAsync(
            Guid dispatchId, TransitionDispatchStatusDto dto)
        {
            await using var transaction = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;
            var dispatch = await _db.Dispatches.Include(d => d.Assignment).ThenInclude(a => a!.RescueTeam)
                .Include(d => d.Assignment).ThenInclude(a => a!.Vehicle).FirstOrDefaultAsync(d => d.Id == dispatchId);
            if (dispatch is null) return (false, "Dispatch not found.", null);

            if (dispatch.Status == DispatchStatus.Pending
                && dto.NewStatus != DispatchStatus.Cancelled
                && dispatch.ApprovalStatus != ApprovalStatus.Approved)
            {
                return (false, "Dispatch has not been approved by a coordinator yet.", null);
            }

            if (!AllowedTransitions.TryGetValue(dispatch.Status, out var allowed) || !allowed.Contains(dto.NewStatus))
            {
                return (false, $"Cannot transition from '{dispatch.Status}' to '{dto.NewStatus}'.", null);
            }

            dispatch.Status = dto.NewStatus;
            if (dto.Notes is not null) dispatch.Notes = dto.Notes;

            var now = DateTime.UtcNow;
            switch (dto.NewStatus)
            {
                case DispatchStatus.Dispatched: dispatch.DispatchedAt = now; break;
                case DispatchStatus.EnRoute: dispatch.EnRouteAt = now; break;
                case DispatchStatus.OnScene: dispatch.OnSceneAt = now; break;
                case DispatchStatus.Resolved: dispatch.ResolvedAt = now; break;
                case DispatchStatus.Cancelled: dispatch.CancelledAt = now; break;
            }

            if (dto.NewStatus is DispatchStatus.Resolved or DispatchStatus.Cancelled)
            {
                if (dispatch.Assignment?.RescueTeam?.Status == TeamStatus.OnMission)
                    dispatch.Assignment.RescueTeam.Status = TeamStatus.Available;
                if (dispatch.Assignment?.Vehicle?.Status == VehicleStatus.InUse)
                    dispatch.Assignment.Vehicle.Status = VehicleStatus.Available;

                if (dto.NewStatus == DispatchStatus.Resolved)
                {
                    var matchingWorkflows = await _db.AgentWorkflows
                        .Where(w => w.Status == Models.Agents.WorkflowStatus.Executing && w.FinalOutcomeJson != null)
                        .ToListAsync();
                    var workflow = matchingWorkflows.SingleOrDefault(w => w.FinalOutcomeJson!.Contains(dispatch.Id.ToString(), StringComparison.Ordinal));
                    if (workflow is not null)
                    {
                        workflow.Status = Models.Agents.WorkflowStatus.Completed;
                        workflow.UpdatedAt = now;
                    }
                }
            }
            await _db.SaveChangesAsync();
            if (transaction is not null) await transaction.CommitAsync();
            return (true, null, ToDto(dispatch));
        }

        private static SafetyValidationResultDto EmptyValidation() =>
            new(false, new List<string>(), DateTime.UtcNow);

        private static DispatchDto ToDto(Dispatch d) => new(
            d.Id, d.AssignmentId, d.Status, d.ApprovalStatus, d.ApprovedByUserId, d.ApprovedAt,
            d.DispatchedAt, d.EnRouteAt, d.OnSceneAt, d.ResolvedAt, d.CancelledAt, d.Notes);
    }
}
