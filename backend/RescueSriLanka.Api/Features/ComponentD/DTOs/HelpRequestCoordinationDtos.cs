using System.ComponentModel.DataAnnotations;
using RescueSriLanka.Api.Features.ComponentD.Models;

namespace RescueSriLanka.Api.Features.ComponentD.DTOs;

// Includes the citizen's name and phone number so the Rescue Coordinator can
// reach the person they're dispatching a team to. Still excludes images and
// verification notes — those stay Component B's concern, not D's.
public record RescueHelpRequestDto(Guid Id, string Type, string Description,
    double? Latitude, double? Longitude, int UrgencyScore, string Status,
    string VerificationStatus, DateTime CreatedAt, int? EstimatedPeopleCount = null,
    string? CitizenName = null, string? CitizenPhoneNumber = null);

public record RecommendRescueTeamRequest(
    [param: Required, EnumDataType(typeof(SkillType))] SkillType? RequiredSkill,
    [param: Required, Range(1, int.MaxValue)] int? RequiredCapacity);

public record RescueCandidateDto(Guid TeamId, string TeamName, Guid VehicleId,
    VehicleType VehicleType, double DistanceKm, SkillType MatchingSkill, int VehicleCapacity,
    TeamStatus TeamAvailability, VehicleStatus VehicleAvailability,
    double BaseLatitude, double BaseLongitude);

public record RescueRecommendationDto(IReadOnlyList<RescueCandidateDto> Candidates,
    RescueCandidateDto? RecommendedCandidate, bool AiAvailable, string Explanation);
