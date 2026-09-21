using System.ComponentModel.DataAnnotations;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.DTOs
{
    // ---- RescueTeam ----
    public record CreateRescueTeamDto(
        [property: Required, MaxLength(150)] string Name,
        double? BaseLatitude,
        double? BaseLongitude);

    public record UpdateRescueTeamDto(
        [property: Required, MaxLength(150)] string Name,
        TeamStatus Status,
        double? BaseLatitude,
        double? BaseLongitude);

    public record RescueTeamDto(
        Guid Id,
        string Name,
        TeamStatus Status,
        double? BaseLatitude,
        double? BaseLongitude,
        List<TeamMemberDto> Members,
        List<VehicleDto> Vehicles);

    // ---- TeamMember ----
    public record CreateTeamMemberDto(
        [property: Required, MaxLength(150)] string FullName,
        // Lenient on purpose — digits, spaces, +, - only, 7 to 15 chars.
        // Deliberately not locked to one country's format.
        [property: Required, RegularExpression(@"^[0-9+\-\s]{7,15}$",
            ErrorMessage = "Phone must be 7-15 characters: digits, spaces, + or - only.")]
        string Phone,
        SkillType Skill);

    public record UpdateTeamMemberAvailabilityDto(bool IsAvailable);

    public record TeamMemberDto(Guid Id, string FullName, string Phone, SkillType Skill, bool IsAvailable);

    // ---- Vehicle ----
    public record CreateVehicleDto(
        [property: Required, MaxLength(20)] string PlateNumber,
        VehicleType Type,
        [property: Range(1, 100, ErrorMessage = "Capacity must be between 1 and 100.")] int Capacity);

    public record UpdateVehicleStatusDto(VehicleStatus Status);

    public record VehicleDto(Guid Id, string PlateNumber, VehicleType Type, VehicleStatus Status, int Capacity);
}
