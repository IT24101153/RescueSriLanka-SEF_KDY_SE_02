namespace RescueSriLanka.Api.Features.ComponentA.Models;
/// <summary>Kind of disaster being reported. Drives base severity weighting.</summary>
public enum IncidentType
{
    Flood,
    Landslide,
    Fire,
    Accident,
    Storm,
    Tsunami,
    Other
}

/// <summary>
/// How bad the incident is. Proposed by the Incident Analysis Agent, confirmed
/// or overridden by an Emergency Coordinator.
/// </summary>
public enum IncidentSeverity
{
    Low,
    Moderate,
    High,
    Critical
}

/// <summary>Lifecycle of an incident report.</summary>
public enum IncidentStatus
{
    Reported,
    Verified,
    InProgress,
    Resolved,
    Rejected,

    /// <summary>
    /// A duplicate of another report of the same event, folded into it on a
    /// coordinator's approval of the Enrichment Agent's proposal. Off the map;
    /// <see cref="Incident.DuplicateOfIncidentId"/> points at the one kept.
    /// </summary>
    Merged
}

/// <summary>Safety classification of an area, shown on the citizen map.</summary>
public enum ZoneStatus
{
    Safe,
    Caution,
    Danger
}

/// <summary>Whether a zone was computed from incidents or declared by a coordinator.</summary>
public enum ZoneSource
{
    DerivedFromIncident,
    ManualOverride
}
