using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.DTOs
{
    // Request to find the best team(s) for a piece of work — the
    // skill/availability-based matching operation.
    public record MatchRequestDto(SkillType RequiredSkill, double? Latitude, double? Longitude, int? MinCapacity);

    public record TeamMatchResultDto(
        Guid RescueTeamId,
        string TeamName,
        int MatchingAvailableMembers,
        double? DistanceKm,
        int Score);

    public record CreateAssignmentDto(
        Guid? IncidentId,
        Guid? HelpRequestId,
        Guid RescueTeamId,
        SkillType RequiredSkill,
        string? Notes);

    public record AssignmentDto(
        Guid Id,
        Guid? IncidentId,
        Guid? HelpRequestId,
        Guid RescueTeamId,
        string RescueTeamName,
        SkillType RequiredSkill,
        DateTime AssignedAt,
        string? Notes,
        Guid? DispatchId);
}
