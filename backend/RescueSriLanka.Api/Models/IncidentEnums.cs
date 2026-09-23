namespace RescueSriLanka.Api.Models;

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
    Rejected
}


