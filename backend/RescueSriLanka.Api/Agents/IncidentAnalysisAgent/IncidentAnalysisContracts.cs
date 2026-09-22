using System.Text.Json.Serialization;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Agents.IncidentAnalysisAgent;

/// <summary>Everything the agent is allowed to see about an incident.</summary>
public record IncidentAnalysisInput
{
    public required Guid IncidentId { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required IncidentType Type { get; init; }
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    public string? District { get; init; }
    public int? EstimatedAffectedPeople { get; init; }
    public int ImageCount { get; init; }
}

/// <summary>
/// The agent's structured output. The provider is constrained to this shape and
/// every field is re-validated on arrival — a model is never trusted to stay in
/// range.
/// </summary>
public record IncidentAnalysisResult
{
    [JsonPropertyName("severity")]
    public required IncidentSeverity Severity { get; init; }

    [JsonPropertyName("severityScore")]
    public required int SeverityScore { get; init; }

    [JsonPropertyName("confidence")]
    public required double Confidence { get; init; }

    [JsonPropertyName("recommendedZoneStatus")]
    public required ZoneStatus RecommendedZoneStatus { get; init; }

    [JsonPropertyName("recommendedRadiusMeters")]
    public required int RecommendedRadiusMeters { get; init; }

    [JsonPropertyName("rationale")]
    public required string Rationale { get; init; }

    /// <summary>True when the rule engine produced this because the model was unusable.</summary>
    [JsonIgnore]
    public bool UsedFallback { get; init; }

    /// <summary>Evidence the agent gathered from its tools, recorded for audit.</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, object>? ToolResults { get; init; }
}
