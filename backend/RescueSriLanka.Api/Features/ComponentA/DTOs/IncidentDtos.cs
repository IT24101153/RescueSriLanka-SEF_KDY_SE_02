using System.ComponentModel.DataAnnotations;
using RescueSriLanka.Api.Features.ComponentA.Models;

namespace RescueSriLanka.Api.Features.ComponentA.DTOs;
/// <summary>Row shape for the incident table and map markers.</summary>
public record IncidentDto
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Type { get; init; }
    public required string Severity { get; init; }
    public required string Status { get; init; }
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    public required int AffectedRadiusMeters { get; init; }
    public string? District { get; init; }
    public string? AddressText { get; init; }
    public int? EstimatedAffectedPeople { get; init; }
    public string? AiSeverity { get; init; }
    public int? AiSeverityScore { get; init; }
    public double? AiConfidence { get; init; }
    public string? AiRationale { get; init; }
    public DateTime? AiAnalysedAt { get; init; }
    public bool SeverityOverridden { get; init; }
    public required bool IsActive { get; init; }
    public required DateTime ReportedAt { get; init; }
    public DateTime? ResolvedAt { get; init; }
    public int ImageCount { get; init; }

    /// <summary>
    /// The attached photos, so a coordinator can see what was reported without
    /// a second round trip. Empty unless the incident was loaded with Images.
    /// </summary>
    public IReadOnlyList<IncidentImageDto> Images { get; init; } = [];

    /// <summary>Distance from the caller, in km. Only set by the nearby query.</summary>
    public double? DistanceKm { get; init; }

    public static IncidentDto FromIncident(Incident incident, double? distanceKm = null) => new()
    {
        Id = incident.Id,
        Title = incident.Title,
        Description = incident.Description,
        Type = incident.Type.ToString(),
        Severity = incident.Severity.ToString(),
        Status = incident.Status.ToString(),
        Latitude = incident.Latitude,
        Longitude = incident.Longitude,
        AffectedRadiusMeters = incident.AffectedRadiusMeters,
        District = incident.District,
        AddressText = incident.AddressText,
        EstimatedAffectedPeople = incident.EstimatedAffectedPeople,
        AiSeverity = incident.AiSeverity?.ToString(),
        AiSeverityScore = incident.AiSeverityScore,
        AiConfidence = incident.AiConfidence,
        AiRationale = incident.AiRationale,
        AiAnalysedAt = incident.AiAnalysedAt,
        SeverityOverridden = incident.SeverityOverriddenAt is not null,
        IsActive = incident.IsActive,
        ReportedAt = incident.ReportedAt,
        ResolvedAt = incident.ResolvedAt,
        ImageCount = incident.Images.Count,
        Images = [.. incident.Images
            .OrderBy(image => image.UploadedAt)
            .Select(IncidentImageDto.FromImage)],
        DistanceKm = distanceKm
    };
}

public record CreateIncidentRequest
{
    [Required, MaxLength(200)]
    public required string Title { get; init; }

    [Required, MaxLength(4000)]
    public required string Description { get; init; }

    [Required]
    public required IncidentType Type { get; init; }

    public IncidentSeverity? Severity { get; init; }

    [Range(-90, 90)]
    public required double Latitude { get; init; }

    [Range(-180, 180)]
    public required double Longitude { get; init; }

    [Range(50, 50000)]
    public int AffectedRadiusMeters { get; init; } = 1000;

    [MaxLength(100)]
    public string? District { get; init; }

    [MaxLength(300)]
    public string? AddressText { get; init; }

    [Range(0, 1000000)]
    public int? EstimatedAffectedPeople { get; init; }
}

/// <summary>
/// A report filed together with its photo. The report can succeed while the
/// photo does not — <see cref="PhotoError"/> says why, so the app can tell the
/// citizen their report is safe but the picture needs sending again.
/// </summary>
public record CreateIncidentResponse
{
    public required IncidentDto Incident { get; init; }

    public string? PhotoError { get; init; }
}

public record UpdateIncidentStatusRequest
{
    [Required]
    public required IncidentStatus Status { get; init; }
}

public record UpdateIncidentSeverityRequest
{
    [Required]
    public required IncidentSeverity Severity { get; init; }

    [MaxLength(500)]
    public string? Reason { get; init; }
}

/// <summary>Everything the dashboard header needs, in one round trip.</summary>
public record DashboardStatisticsDto
{
    public required int ActiveIncidents { get; init; }
    public required int CriticalIncidents { get; init; }
    public required int AwaitingVerification { get; init; }
    public required int ReportedLast24Hours { get; init; }
    public required int PeopleAffected { get; init; }
    public required int ActiveDangerZones { get; init; }
    public required int AwaitingAiAnalysis { get; init; }
    public required Dictionary<string, int> BySeverity { get; init; }
    public required Dictionary<string, int> ByType { get; init; }
    public required Dictionary<string, int> ByStatus { get; init; }
    public required Dictionary<string, int> ByDistrict { get; init; }
}

public record IncidentImageDto
{
    public required Guid Id { get; init; }
    public required Guid IncidentId { get; init; }
    public required string Url { get; init; }
    public string? FileName { get; init; }
    public string? ContentType { get; init; }
    public required long SizeBytes { get; init; }
    public string? Caption { get; init; }
    public required DateTime UploadedAt { get; init; }

    public static IncidentImageDto FromImage(IncidentImage image) => new()
    {
        Id = image.Id,
        IncidentId = image.IncidentId,
        Url = image.StoragePath,
        FileName = image.FileName,
        ContentType = image.ContentType,
        SizeBytes = image.SizeBytes,
        Caption = image.Caption,
        UploadedAt = image.UploadedAt
    };
}
