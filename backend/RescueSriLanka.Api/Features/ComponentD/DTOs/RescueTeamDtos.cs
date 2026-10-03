using System.ComponentModel.DataAnnotations;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Features.ComponentD.Models;

using RescueSriLanka.Api.DTOs;
namespace RescueSriLanka.Api.Features.ComponentD.DTOs
{
    // ---- RescueTeam ----
    public record CreateRescueTeamDto(
        [param: Required, MaxLength(150)] string Name,
        [param: Range(-90, 90)] double? BaseLatitude,
        [param: Range(-180, 180)] double? BaseLongitude);

    public record UpdateRescueTeamDto(
        [param: Required, MaxLength(150)] string Name,
        TeamStatus Status,
        [param: Range(-90, 90)] double? BaseLatitude,
        [param: Range(-180, 180)] double? BaseLongitude);

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
        [param: Required, MaxLength(150), PersonName] string FullName,
        [param: Required, SriLankaPhone]
        string Phone,
        SkillType Skill);

    public record UpdateTeamMemberAvailabilityDto(bool IsAvailable);

    public record UpdateTeamMemberDto(
        [param: Required, MaxLength(150), PersonName] string FullName,
        [param: Required, SriLankaPhone]
        string Phone,
        SkillType Skill,
        bool IsAvailable);

    public record TeamMemberDto(Guid Id, string FullName, string Phone, SkillType Skill, bool IsAvailable);

    // ---- Vehicle ----
    public record CreateVehicleDto(
        [param: Required, MaxLength(20)] string PlateNumber,
        VehicleType Type,
        [param: Range(1, 100, ErrorMessage = "Capacity must be between 1 and 100.")] int Capacity);

    public record UpdateVehicleStatusDto(VehicleStatus Status);

    public record UpdateVehicleDto(
        [param: Required, MaxLength(20)] string PlateNumber,
        VehicleType Type,
        VehicleStatus Status,
        [param: Range(1, 100, ErrorMessage = "Capacity must be between 1 and 100.")] int Capacity);

    public record VehicleDto(Guid Id, string PlateNumber, VehicleType Type, VehicleStatus Status, int Capacity);
}
