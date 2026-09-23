using System.ComponentModel.DataAnnotations;

namespace RescueSriLanka.Api.Features.ComponentA.Models;
/// <summary>
/// A reported disaster event. The central entity of Component A — its location
/// and severity feed the citizen map, the safety zones, and every downstream
/// component's resource and team matching.
/// </summary>
public class Incident
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(200)]
    public required string Title { get; set; }

    [MaxLength(4000)]
    public required string Description { get; set; }

    public IncidentType Type { get; set; }

    /// <summary>Severity in force — the coordinator's decision once overridden.</summary>
    public IncidentSeverity Severity { get; set; } = IncidentSeverity.Moderate;

    public IncidentStatus Status { get; set; } = IncidentStatus.Reported;

    // ---------- location ----------
    public double Latitude { get; set; }
    public double Longitude { get; set; }

    /// <summary>Radius of the affected area, used to draw the derived safety zone.</summary>
    public int AffectedRadiusMeters { get; set; } = 1000;

    [MaxLength(100)]
    public string? District { get; set; }

    [MaxLength(300)]
    public string? AddressText { get; set; }

    // ---------- impact ----------
    public int? EstimatedAffectedPeople { get; set; }

    // ---------- Incident Analysis Agent output ----------
    /// <summary>Severity the agent proposed, kept separate from the value in force.</summary>
    public IncidentSeverity? AiSeverity { get; set; }

    /// <summary>Agent score from 0-100. Null until the incident has been analysed.</summary>
    public int? AiSeverityScore { get; set; }

    public double? AiConfidence { get; set; }

    [MaxLength(2000)]
    public string? AiRationale { get; set; }

    public DateTime? AiAnalysedAt { get; set; }

    /// <summary>Set when a coordinator disagreed with the agent.</summary>
    public Guid? SeverityOverriddenBy { get; set; }
    public DateTime? SeverityOverriddenAt { get; set; }

    // ---------- provenance ----------
    public Guid? ReportedByUserId { get; set; }
    public DateTime ReportedAt { get; set; } = DateTime.UtcNow;

    public Guid? VerifiedByUserId { get; set; }
    public DateTime? VerifiedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }

    /// <summary>
    /// When the district-wide warning email went out. Verification and AI
    /// approval both fire the warning, and an incident is often both verified
    /// and approved — this is what stops the district being emailed twice about
    /// the same event.
    /// </summary>
    public DateTime? DistrictWarningSentAt { get; set; }

    /// <summary>False once resolved or rejected — drives the "active" map layer.</summary>
    public bool IsActive { get; set; } = true;

    // ---------- audit ----------
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public ICollection<IncidentImage> Images { get; set; } = [];
}
