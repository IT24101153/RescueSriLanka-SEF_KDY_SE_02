using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.DTOs
{
    public record CreateDispatchDto(Guid AssignmentId, string? Notes);

    // Used to move the dispatch through the state machine
    public record TransitionDispatchStatusDto(DispatchStatus NewStatus, string? Notes);

    public record ApproveDispatchDto(string ApprovedByUserId, bool Approve, string? Notes);

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

    // Result returned by the Safety Validation Agent
    public record SafetyValidationResultDto(
        bool Passed,
        List<string> Issues,
        DateTime CheckedAt);
}
