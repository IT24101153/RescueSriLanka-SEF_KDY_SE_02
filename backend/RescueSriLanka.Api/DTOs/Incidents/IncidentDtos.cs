using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.DTOs.Incidents;

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
        Images = incident.Images
            .OrderBy(image => image.UploadedAt)
            .Select(IncidentImageDto.FromImage)
            .ToList(),
        DistanceKm = distanceKm
    };
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
