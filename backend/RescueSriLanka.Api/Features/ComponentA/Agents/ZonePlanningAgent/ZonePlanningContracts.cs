using System.Text.Json.Serialization;
using RescueSriLanka.Api.Features.ComponentA.Models;

namespace RescueSriLanka.Api.Features.ComponentA.Agents.ZonePlanningAgent;

public static class ZoneActions
{
    public const string Create = "create";
    public const string Retire = "retire";
}

/// <summary>One approved, active incident as the planner sees it.</summary>
public record PlanningIncident(
    Guid Id, string Title, IncidentType Type, IncidentSeverity Severity,
    double Latitude, double Longitude, int RadiusMeters, string? District, int? People);

/// <summary>Approved incidents close enough together to be one operational area.</summary>
public record IncidentCluster(
    int Index,
    IReadOnlyList<Guid> IncidentIds,
    double CenterLatitude,
    double CenterLongitude,
    int SpanRadiusMeters,
    IncidentSeverity WorstSeverity,
    IncidentType DominantType,
    string? District,
    int People,
    double? RainfallMm);

/// <summary>A coordinator-declared zone already on the map.</summary>
public record ExistingManualZone(
    Guid Id, string Name, ZoneStatus Status,
    double CenterLatitude, double CenterLongitude, int RadiusMeters,
    DateTime ComputedAt, DateTime? ExpiresAt, int ActiveIncidentsInside);

/// <summary>What the Evidence agent hands to the Reasoning agent.</summary>
public record ZonePlanningEvidence(
    IReadOnlyList<PlanningIncident> Incidents,
    IReadOnlyList<IncidentCluster> Clusters,
    IReadOnlyList<ExistingManualZone> ManualZones,
    IReadOnlyDictionary<string, object?> ToolCalls);

/// <summary>
/// One proposed change to the zone layer. A "create" draws a new manual zone;
/// a "retire" takes an existing manual zone off the map.
/// </summary>
public record ZoneProposal
{
    [JsonPropertyName("action")]
    public required string Action { get; init; }

    [JsonPropertyName("zoneId")]
    public Guid? ZoneId { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("status")]
    public required ZoneStatus Status { get; init; }

    [JsonPropertyName("centerLatitude")]
    public required double CenterLatitude { get; init; }

    [JsonPropertyName("centerLongitude")]
    public required double CenterLongitude { get; init; }

    [JsonPropertyName("radiusMeters")]
    public required int RadiusMeters { get; init; }

    [JsonPropertyName("district")]
    public string? District { get; init; }

    [JsonPropertyName("expiresInHours")]
    public int? ExpiresInHours { get; init; }

    [JsonPropertyName("rationale")]
    public required string Rationale { get; init; }

    [JsonPropertyName("basedOnIncidentIds")]
    public IReadOnlyList<Guid> BasedOnIncidentIds { get; init; } = [];
}

/// <summary>The validated plan, persisted as the run's OutputJson.</summary>
public record ZonePlanResult
{
    [JsonPropertyName("zones")]
    public required IReadOnlyList<ZoneProposal> Zones { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("usedFallback")]
    public bool UsedFallback { get; init; }
}

/// <summary>The raw shape the model is constrained to; every value is re-checked.</summary>
public record ModelZonePlan
{
    [JsonPropertyName("zones")]
    public List<ModelZone>? Zones { get; init; }

    [JsonPropertyName("summary")]
    public string? Summary { get; init; }
}

public record ModelZone
{
    [JsonPropertyName("action")]
    public string? Action { get; init; }

    [JsonPropertyName("zoneId")]
    public string? ZoneId { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("centerLatitude")]
    public double? CenterLatitude { get; init; }

    [JsonPropertyName("centerLongitude")]
    public double? CenterLongitude { get; init; }

    [JsonPropertyName("radiusMeters")]
    public double? RadiusMeters { get; init; }

    [JsonPropertyName("district")]
    public string? District { get; init; }

    [JsonPropertyName("expiresInHours")]
    public double? ExpiresInHours { get; init; }

    [JsonPropertyName("rationale")]
    public string? Rationale { get; init; }

    [JsonPropertyName("basedOnIncidentIds")]
    public List<string>? BasedOnIncidentIds { get; init; }
}
