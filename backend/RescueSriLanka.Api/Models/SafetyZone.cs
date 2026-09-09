using System.ComponentModel.DataAnnotations;

namespace RescueSriLanka.Api.Models;

/// <summary>
/// A circular area with a safety classification. Most zones are recomputed from
/// active incidents; coordinators can also declare one by hand, and those are
/// never overwritten by the recompute.
/// </summary>
public class SafetyZone
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(200)]
    public required string Name { get; set; }

    public ZoneStatus Status { get; set; } = ZoneStatus.Caution;

    public ZoneSource Source { get; set; } = ZoneSource.DerivedFromIncident;

    public double CenterLatitude { get; set; }
    public double CenterLongitude { get; set; }
    public int RadiusMeters { get; set; }

    [MaxLength(100)]
    public string? District { get; set; }

    [MaxLength(500)]
    public string? Rationale { get; set; }

    /// <summary>The incident this zone was derived from, when it was not declared manually.</summary>
    public Guid? SourceIncidentId { get; set; }
    public Incident? SourceIncident { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime ComputedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
}
