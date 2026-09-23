using System.ComponentModel.DataAnnotations;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Features.ComponentD.Models;

namespace RescueSriLanka.Api.Features.ComponentD.DTOs
{
    public enum CoordinatorDecision { APPROVE, REVISE, REJECT }

    public record CoordinatorDecisionDto(
        Guid WorkflowId,
        int PlanVersion,
        CoordinatorDecision Decision,
        [param: MaxLength(500)] string? Notes);

    public record CoordinatorDecisionResultDto(
        bool Success,
        bool Idempotent,
        string? Error,
        DispatchDto? Dispatch,
        AssignmentStatus AssignmentStatus,
        TeamStatus? TeamStatus,
        VehicleStatus? VehicleStatus);

    public record CreateDispatchDto(
        Guid AssignmentId,
        [param: MaxLength(500)] string? Notes);

    public record TransitionDispatchStatusDto(
        DispatchStatus NewStatus,
        [param: MaxLength(500)] string? Notes);

    public record ApproveDispatchDto(
        bool Approve,
        [param: MaxLength(500)] string? Notes);

    public record DispatchDto(
        Guid Id,
        Guid AssignmentId,
        DispatchStatus Status,
        ApprovalStatus ApprovalStatus,
        string? ApprovedByUserId,
        DateTime? ApprovedAt,
        DateTime? DispatchedAt,
        DateTime? EnRouteAt,
        DateTime? OnSceneAt,
        DateTime? ResolvedAt,
        DateTime? CancelledAt,
        string? Notes);

    public record SafetyValidationResultDto(
        bool Passed,
        List<string> Issues,
        DateTime CheckedAt);
}
