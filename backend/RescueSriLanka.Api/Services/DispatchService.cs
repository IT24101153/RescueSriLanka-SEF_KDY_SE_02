using Microsoft.EntityFrameworkCore;
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
        Task<(bool Success, string? Error, DispatchDto? Dispatch)> TransitionStatusAsync(Guid dispatchId, TransitionDispatchStatusDto dto);
    }

    public class DispatchService : IDispatchService
    {
        private readonly ApplicationDbContext _db;
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

        public DispatchService(ApplicationDbContext db, ISafetyValidationAgent safetyAgent)
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

            var dispatch = new Dispatch
            {
                AssignmentId = assignment.Id,
                Status = DispatchStatus.Pending,
                ApprovalStatus = ApprovalStatus.PendingApproval,
                Notes = dto.Notes
            };

            _db.Dispatches.Add(dispatch);
            await _db.SaveChangesAsync();

            return (ToDto(dispatch), validation, null);
        }

        public async Task<DispatchDto?> ApproveAsync(Guid dispatchId, string approvedByUserId, ApproveDispatchDto dto)
        {
            var dispatch = await _db.Dispatches.FirstOrDefaultAsync(d => d.Id == dispatchId);
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

            await _db.SaveChangesAsync();
            return ToDto(dispatch);
        }

        public async Task<(bool Success, string? Error, DispatchDto? Dispatch)> TransitionStatusAsync(
            Guid dispatchId, TransitionDispatchStatusDto dto)
        {
            var dispatch = await _db.Dispatches.FirstOrDefaultAsync(d => d.Id == dispatchId);
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

            await _db.SaveChangesAsync();
            return (true, null, ToDto(dispatch));
        }

        private static SafetyValidationResultDto EmptyValidation() =>
            new(false, new List<string>(), DateTime.UtcNow);

        private static DispatchDto ToDto(Dispatch d) => new(
            d.Id, d.AssignmentId, d.Status, d.ApprovalStatus, d.ApprovedByUserId, d.ApprovedAt,
            d.DispatchedAt, d.EnRouteAt, d.OnSceneAt, d.ResolvedAt, d.CancelledAt, d.Notes);
    }
}
