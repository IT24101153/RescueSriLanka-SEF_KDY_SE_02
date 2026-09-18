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
        Task<(DispatchDto? Dispatch, SafetyValidationResultDto Validation)> CreateAsync(CreateDispatchDto dto);
        Task<DispatchDto?> ApproveAsync(Guid dispatchId, ApproveDispatchDto dto);
        Task<(bool Success, string? Error, DispatchDto? Dispatch)> TransitionStatusAsync(Guid dispatchId, TransitionDispatchStatusDto dto);
    }

    public class DispatchService : IDispatchService
    {
        private readonly ApplicationDbContext _db;
        private readonly ISafetyValidationAgent _safetyAgent;

        // Allowed forward transitions for the dispatch status workflow.
        // Cancellation is allowed from any non-terminal state.
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

        // Creating a dispatch always runs the Safety Validation Agent first.
        // The dispatch record is created either way (for auditability), but
        // it starts PendingApproval and the validation result is returned
        // so the Coordinator sees exactly why it did/didn't pass.
        public async Task<(DispatchDto? Dispatch, SafetyValidationResultDto Validation)> CreateAsync(CreateDispatchDto dto)
        {
            var assignment = await _db.Assignments
                .Include(a => a.Dispatch)
                .FirstOrDefaultAsync(a => a.Id == dto.AssignmentId);

            if (assignment is null)
            {
                return (null, new SafetyValidationResultDto(false,
                    new List<string> { "Assignment not found." }, DateTime.UtcNow));
            }

            if (assignment.Dispatch is not null)
            {
                return (null, new SafetyValidationResultDto(false,
                    new List<string> { "This assignment already has a dispatch." }, DateTime.UtcNow));
            }

            var validation = await _safetyAgent.ValidateAsync(assignment.Id);

            var dispatch = new Dispatch
            {
                AssignmentId = assignment.Id,
                Status = DispatchStatus.Pending,
                ApprovalStatus = ApprovalStatus.PendingApproval,
                Notes = dto.Notes
            };

            _db.Dispatches.Add(dispatch);
            await _db.SaveChangesAsync();

            return (ToDto(dispatch), validation);
        }

        // Human-approval step — an Emergency Coordinator approves or
        // rejects after seeing the Safety Validation Agent's result.
        public async Task<DispatchDto?> ApproveAsync(Guid dispatchId, ApproveDispatchDto dto)
        {
            var dispatch = await _db.Dispatches.FirstOrDefaultAsync(d => d.Id == dispatchId);
            if (dispatch is null) return null;

            dispatch.ApprovalStatus = dto.Approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
            dispatch.ApprovedByUserId = dto.ApprovedByUserId;
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

            // Can't move a dispatch out of Pending into an active state
            // without coordinator approval — enforces the human-in-the-loop
            // requirement at the workflow level, not just the UI.
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

        private static DispatchDto ToDto(Dispatch d) => new(
            d.Id, d.AssignmentId, d.Status, d.ApprovalStatus, d.ApprovedByUserId, d.ApprovedAt,
            d.DispatchedAt, d.EnRouteAt, d.OnSceneAt, d.ResolvedAt, d.CancelledAt, d.Notes);
    }
}
