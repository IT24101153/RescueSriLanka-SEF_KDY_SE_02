using System.ComponentModel.DataAnnotations;
using RescueSriLanka.Api.Features.ComponentD.Models;

namespace RescueSriLanka.Api.Features.ComponentD.DTOs;

// Deliberately excludes citizen identity, contact details, images and verification notes.
public record RescueHelpRequestDto(Guid Id, string Type, string Description,
    double? Latitude, double? Longitude, int UrgencyScore, string Status,
    string VerificationStatus, DateTime CreatedAt, int? EstimatedPeopleCount = null);

public record RecommendRescueTeamRequest(
    [param: Required, EnumDataType(typeof(SkillType))] SkillType? RequiredSkill,
    [param: Required, Range(1, int.MaxValue)] int? RequiredCapacity);

public record RescueCandidateDto(Guid TeamId, string TeamName, Guid VehicleId,
    VehicleType VehicleType, double DistanceKm, SkillType MatchingSkill, int VehicleCapacity,
    TeamStatus TeamAvailability, VehicleStatus VehicleAvailability,
    double BaseLatitude, double BaseLongitude);

public record RescueRecommendationDto(IReadOnlyList<RescueCandidateDto> Candidates,
    RescueCandidateDto? RecommendedCandidate, bool AiAvailable, string Explanation);
