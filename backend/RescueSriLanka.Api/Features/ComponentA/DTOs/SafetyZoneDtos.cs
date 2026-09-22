using RescueSriLanka.Api.Features.ComponentA.Models;


namespace RescueSriLanka.Api.Features.ComponentA.DTOs;
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
        ComputedAt = zone.ComputedAt
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
