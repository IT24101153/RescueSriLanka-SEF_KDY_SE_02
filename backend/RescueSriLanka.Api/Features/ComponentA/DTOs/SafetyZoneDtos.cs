using System.ComponentModel.DataAnnotations;
using RescueSriLanka.Api.DTOs;
using RescueSriLanka.Api.Features.ComponentA.Models;

namespace RescueSriLanka.Api.Features.ComponentA.DTOs;

/// <summary>
/// A zone a coordinator declares or edits by hand — or a Zone Planning Agent
/// draft they adjusted before approving. Only manual zones take this shape;
/// derived zones follow their incident.
/// </summary>
public record SafetyZoneRequest
{
    [Required, StringLength(200, MinimumLength = 3)]
    public required string Name { get; init; }

    [Required]
    public required ZoneStatus Status { get; init; }

    [Range(-90, 90)]
    public required double CenterLatitude { get; init; }

    [Range(-180, 180)]
    public required double CenterLongitude { get; init; }

    [Range(100, 20000)]
    public required int RadiusMeters { get; init; }

    [MaxLength(100), SriLankaDistrict]
    public string? District { get; init; }

    [MaxLength(500)]
    public string? Rationale { get; init; }

    /// <summary>When the zone lapses by itself. Null keeps it until retired.</summary>
    public DateTime? ExpiresAt { get; init; }
}
public record SafetyZoneDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Status { get; init; }
    public required string Source { get; init; }
    public required double CenterLatitude { get; init; }
    public required double CenterLongitude { get; init; }
    public required int RadiusMeters { get; init; }
    public string? District { get; init; }
    public string? Rationale { get; init; }
    public Guid? SourceIncidentId { get; init; }
    public required DateTime ComputedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public Guid? SourceAgentRunId { get; init; }

    public static SafetyZoneDto FromZone(SafetyZone zone) => new()
    {
        Id = zone.Id,
        Name = zone.Name,
        Status = zone.Status.ToString(),
        Source = zone.Source.ToString(),
        CenterLatitude = zone.CenterLatitude,
        CenterLongitude = zone.CenterLongitude,
        RadiusMeters = zone.RadiusMeters,
        District = zone.District,
        Rationale = zone.Rationale,
        SourceIncidentId = zone.SourceIncidentId,
        ComputedAt = zone.ComputedAt,
        ExpiresAt = zone.ExpiresAt,
        SourceAgentRunId = zone.SourceAgentRunId
    };
}

/// <summary>Answer to "is this point safe?" — used by the tourist app and Student B's advisory.</summary>
public record ZoneCheckResultDto
{
    public required string Status { get; init; }
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    public required IReadOnlyList<SafetyZoneDto> MatchingZones { get; init; }
    public required int NearbyIncidentCount { get; init; }
    public required string Message { get; init; }
}
