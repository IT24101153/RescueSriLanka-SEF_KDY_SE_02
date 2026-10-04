using System.Text.Json.Serialization;
using RescueSriLanka.Api.Features.ComponentA.Models;

namespace RescueSriLanka.Api.Features.ComponentA.Agents.IncidentEnrichmentAgent;

/// <summary>The incident fields the Enrichment Agent may propose a change to.</summary>
public static class EnrichmentFields
{
    public const string Title = "title";
    public const string Type = "type";
    public const string District = "district";
    public const string People = "estimatedAffectedPeople";
    public const string Address = "addressText";

    /// <summary>
    /// The description is deliberately absent: it is the reporter's own account
    /// and stays exactly as they wrote it.
    /// </summary>
    public static readonly IReadOnlyList<string> All = [Title, Type, District, People, Address];
}

/// <summary>Everything the agent is allowed to see about the incident it is enriching.</summary>
public record EnrichmentInput
{
    public required Guid IncidentId { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required IncidentType Type { get; init; }
    public required IncidentStatus Status { get; init; }
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    public string? District { get; init; }
    public string? AddressText { get; init; }
    public int? EstimatedAffectedPeople { get; init; }
    public required DateTime ReportedAt { get; init; }
}

/// <summary>Another open report that may describe the same event.</summary>
public record DuplicateCandidate(
    Guid IncidentId,
    string Title,
    IncidentType Type,
    IncidentStatus Status,
    string? District,
    double DistanceKm,
    double HoursApart,
    double TextSimilarity);

/// <summary>What the Evidence agent hands to the Reasoning agent.</summary>
public record EnrichmentEvidence(
    IReadOnlyList<DuplicateCandidate> Candidates,
    string? NormalisedDistrict,
    string NearestDistrict,
    double NearestDistrictKm,
    double? ReportedDistrictKm,
    int? PeopleFromText,
    string? PeopleEvidence,
    IReadOnlyDictionary<string, object?> ToolCalls);

/// <summary>One proposed change to one field, with the reason a coordinator reads.</summary>
public record FieldSuggestion
{
    [JsonPropertyName("field")]
    public required string Field { get; init; }

    [JsonPropertyName("current")]
    public string? Current { get; init; }

    [JsonPropertyName("proposed")]
    public required string Proposed { get; init; }

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }
}

/// <summary>A proposal to fold this report into another report of the same event.</summary>
public record DuplicateProposal
{
    [JsonPropertyName("incidentId")]
    public required Guid IncidentId { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("distanceKm")]
    public required double DistanceKm { get; init; }

    [JsonPropertyName("similarity")]
    public required double Similarity { get; init; }

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }
}

/// <summary>
/// The agent's validated output, persisted as the run's OutputJson. Nothing in
/// it reaches the incident until a coordinator approves — field by field.
/// </summary>
public record EnrichmentResult
{
    [JsonPropertyName("suggestions")]
    public required IReadOnlyList<FieldSuggestion> Suggestions { get; init; }

    [JsonPropertyName("duplicate")]
    public DuplicateProposal? Duplicate { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("usedFallback")]
    public bool UsedFallback { get; init; }

    [JsonIgnore]
    public bool HasProposals => Suggestions.Count > 0 || Duplicate is not null;
}

/// <summary>The raw shape the model is constrained to; validated before use.</summary>
public record ModelEnrichmentAnswer
{
    [JsonPropertyName("suggestions")]
    public List<ModelSuggestion>? Suggestions { get; init; }

    [JsonPropertyName("duplicateOfIncidentId")]
    public string? DuplicateOfIncidentId { get; init; }

    [JsonPropertyName("duplicateReason")]
    public string? DuplicateReason { get; init; }

    [JsonPropertyName("summary")]
    public string? Summary { get; init; }
}

public record ModelSuggestion
{
    [JsonPropertyName("field")]
    public string? Field { get; init; }

    [JsonPropertyName("proposed")]
    public string? Proposed { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}
