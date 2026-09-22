using System.ComponentModel.DataAnnotations;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.DTOs
{
    public record MatchRequestDto(
        SkillType RequiredSkill,
        double? Latitude,
        double? Longitude,
        [param: Range(1, 100, ErrorMessage = "Minimum capacity must be at least 1.")] int? MinCapacity);

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
        Guid VehicleId,
        SkillType RequiredSkill,
        [param: Range(1, int.MaxValue, ErrorMessage = "Required capacity must be at least 1.")] int RequiredCapacity,
        [param: MaxLength(500)] string? Notes);

    public record ReviseAssignmentDto(
        Guid RescueTeamId,
        Guid VehicleId,
        SkillType RequiredSkill,
        [param: Range(1, int.MaxValue, ErrorMessage = "Required capacity must be at least 1.")] int RequiredCapacity,
        [param: MaxLength(500)] string? Notes);

    public record AssignmentDto(
        Guid Id,
        Guid? IncidentId,
        Guid? HelpRequestId,
        Guid RescueTeamId,
        string RescueTeamName,
        Guid? VehicleId,
        string VehiclePlateNumber,
        SkillType RequiredSkill,
        int RequiredCapacity,
        AssignmentStatus Status,
        int PlanVersion,
        DateTime AssignedAt,
        string? Notes,
        Guid? DispatchId);
}
