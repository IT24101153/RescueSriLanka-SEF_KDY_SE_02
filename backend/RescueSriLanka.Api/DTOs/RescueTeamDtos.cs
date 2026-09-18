using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.DTOs
{
    // ---- RescueTeam ----
    public record CreateRescueTeamDto(string Name, double? BaseLatitude, double? BaseLongitude);

    public record UpdateRescueTeamDto(string Name, TeamStatus Status, double? BaseLatitude, double? BaseLongitude);

    public record RescueTeamDto(
        Guid Id,
        string Name,
        TeamStatus Status,
        double? BaseLatitude,
        double? BaseLongitude,
        List<TeamMemberDto> Members,
        List<VehicleDto> Vehicles);

    // ---- TeamMember ----
    public record CreateTeamMemberDto(string FullName, string Phone, SkillType Skill);

    public record UpdateTeamMemberAvailabilityDto(bool IsAvailable);

    public record TeamMemberDto(Guid Id, string FullName, string Phone, SkillType Skill, bool IsAvailable);

    // ---- Vehicle ----
    public record CreateVehicleDto(string PlateNumber, VehicleType Type, int Capacity);

    public record UpdateVehicleStatusDto(VehicleStatus Status);

    public record VehicleDto(Guid Id, string PlateNumber, VehicleType Type, VehicleStatus Status, int Capacity);
}
