using System.Text.Json;
using System.Text.Json.Serialization;
using RescueSriLanka.Api.Features.ComponentA.Models;
using RescueSriLanka.Api.Features.ComponentA.Services;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Services.Llm;

namespace RescueSriLanka.Api.Features.ComponentA.Agents.IncidentAnalysisAgent;

// The Incident Analysis workflow is run by four agents. Each one has a single
// responsibility, a typed input and output, and a fixed set of things it may
// touch — nothing is shared between them except those typed values, which the
// coordinator (IncidentAnalysisAgent) passes along and persists.
//
//   Planner    incident            -> plan             touches nothing
//   Evidence   incident            -> evidence bundle  allow-listed tools + photo store
//   Severity   incident + evidence -> proposal         the language model / rule engine
//   Validator  proposal            -> accepted result  nothing (pure function)

/// <summary>Names of the agent roles, as recorded in a run's plan.</summary>
public static class AnalysisRoles
{
    public const string Planner = "PlannerAgent";
    public const string Evidence = "EvidenceGatheringAgent";
    public const string Severity = "SeverityAnalysisAgent";
    public const string Validator = "ProposalValidationAgent";
}

/// <summary>Names of the allow-listed tools, as recorded against a run.</summary>
public static class AnalysisTools
{
    public const string CountNearby = "count_nearby_active_incidents";
    public const string Rainfall = "get_rainfall_last_48h";
    public const string LoadImages = "load_incident_images";
}

public record PlannedStep(int Number, string Agent, string Action, IReadOnlyList<string> Tools);

/// <summary>The ordered steps, plus the planner's reasons for leaving anything out.</summary>
public record AnalysisPlan(Guid IncidentId, IReadOnlyList<PlannedStep> Steps, IReadOnlyList<string> Notes)
{
    public bool Uses(string tool) => Steps.Any(step => step.Tools.Contains(tool));
}

/// <summary>What the Evidence agent hands to the Severity agent.</summary>
public record EvidenceBundle(
    int NearbyIncidents,
    double? RainfallMm,
    bool RainfallChecked,
    IReadOnlyList<LlmImage> Images,
    IReadOnlyDictionary<string, object?> ToolCalls);

/// <summary>What the Severity agent hands to the Validator.</summary>
public record SeverityProposal(
    IncidentAnalysisResult Result,
    string Model,
    AgentRunStatus Status,
    string? Error,
    int Attempts = 0);

/// <summary>The Validator's verdict on a proposal.</summary>
public record ValidationOutcome(
    bool Accepted,
    IncidentAnalysisResult? Result,
    IReadOnlyList<string> Adjustments,
    string? Rejection);

internal static class AnalysisJson
{
    /// <summary>The model emits enum names, so parsing must accept them.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true
    };
}

/// <summary>
/// Coordinator's planner. Turns an incident into a structured, ordered plan and
/// names the agent responsible for each step. Deterministic: the plan depends
/// only on the incident, never on a model, so it cannot be steered by report
/// text.
///
/// The plan adapts to the incident: rainfall is only worth fetching for a
/// weather-driven hazard, and photos are only loaded when the report has some.
/// Each omission is written down in <see cref="AnalysisPlan.Notes"/>.
/// </summary>
public class AnalysisPlannerAgent
{
    private static readonly IncidentType[] WeatherDriven =
        [IncidentType.Flood, IncidentType.Landslide, IncidentType.Storm, IncidentType.Tsunami];

    public AnalysisPlan Plan(IncidentAnalysisInput input)
    {
        if (input.Latitude is < -90 or > 90 || input.Longitude is < -180 or > 180)
        {
            throw new ArgumentException("The incident has invalid coordinates and cannot be planned.");
        }

        var tools = new List<string> { AnalysisTools.CountNearby };
        var notes = new List<string>();

        if (WeatherDriven.Contains(input.Type))
        {
            tools.Add(AnalysisTools.Rainfall);
        }
        else
        {
            notes.Add($"Rainfall lookup skipped: not relevant to a {input.Type} report.");
        }

        if (input.ImageCount > 0)
        {
            tools.Add(AnalysisTools.LoadImages);
        }
        else
        {
            notes.Add("Photo loading skipped: the report has no photos.");
        }

        return new AnalysisPlan(input.IncidentId,
        [
            new(1, AnalysisRoles.Evidence, "Gather evidence", tools),
            new(2, AnalysisRoles.Severity, "Classify severity and safety-zone status", []),
            new(3, AnalysisRoles.Validator, "Validate the proposal against schema and range rules", [])
        ], notes);
    }
}

/// <summary>
/// Evidence agent. The only role allowed to call tools; it cannot classify or
/// write anything.
/// </summary>
public class EvidenceGatheringAgent(IncidentAnalysisTools tools, IImageStorageService imageStorage)
{
    public const int NearbyRadiusKm = 5;
    public const int MaxImages = 3;
    public const long MaxImageBytes = 6 * 1024 * 1024;

    public async Task<EvidenceBundle> GatherAsync(
        IncidentAnalysisInput input, AnalysisPlan plan, CancellationToken ct)
    {
        var toolCalls = new Dictionary<string, object?>();

        // Only the tools the plan names are called; the rest are recorded as skipped.
        var nearby = 0;
        if (plan.Uses(AnalysisTools.CountNearby))
        {
            nearby = await tools.CountNearbyActiveIncidentsAsync(
                input.Latitude, input.Longitude, NearbyRadiusKm, input.IncidentId, ct);
            toolCalls[AnalysisTools.CountNearby] = new { radiusKm = NearbyRadiusKm, result = nearby };
        }
        else
        {
            toolCalls[AnalysisTools.CountNearby] = new { skipped = true };
        }

        double? rainfall = null;
        var rainfallChecked = plan.Uses(AnalysisTools.Rainfall);
        if (rainfallChecked)
        {
            rainfall = await tools.GetRainfallLast48hAsync(input.Latitude, input.Longitude, ct);
            toolCalls[AnalysisTools.Rainfall] = new { result = rainfall, available = rainfall is not null };
        }
        else
        {
            toolCalls[AnalysisTools.Rainfall] = new { skipped = true };
        }

        // Photos are evidence too: at most 3, capped at 6 MB in total so the
        // request stays well inside the provider's limits.
        var images = new List<LlmImage>();
        if (plan.Uses(AnalysisTools.LoadImages))
        {
            var photos = await imageStorage.LoadForAnalysisAsync(input.IncidentId, MaxImages, MaxImageBytes, ct);
            images = [.. photos.Select(photo => new LlmImage(photo.MimeType, photo.Data))];
            toolCalls[AnalysisTools.LoadImages] = new { requested = MaxImages, loaded = images.Count };
        }
        else
        {
            toolCalls[AnalysisTools.LoadImages] = new { skipped = true };
        }

        return new EvidenceBundle(nearby, rainfall, rainfallChecked, images, toolCalls);
    }
}

/// <summary>
/// Severity agent. Reasons over the evidence with the language model, or with
/// the deterministic rule engine when the model is unconfigured or unusable.
/// It has no tools and no database access.
/// </summary>
public class SeverityAnalysisAgent(ILlmClient llm, ILogger logger)
{
    private const string SystemInstruction =
        """
        You are the Incident Analysis Agent for a Sri Lankan disaster response
        platform. You classify how severe a reported incident is and what safety
        zone status the surrounding area should carry.

        Judge severity from: the hazard type, how many people are exposed,
        whether nearby reports suggest a larger event, any weather signal, and
        any photographs attached to the report.

        When photographs are supplied, use what is actually visible in them —
        water depth against buildings or vehicles, structural damage, blocked
        roads, smoke — and say in your rationale what you saw. Never invent
        detail that is not visible.

        Be conservative about human life: when the evidence is ambiguous but
        people are exposed, prefer the higher severity.

        Return only the JSON object described by the schema. The rationale must
        be one or two plain sentences a coordinator can act on, citing the
        specific evidence you used.
        """;

    /// <summary>Forces the provider to return exactly the shape we validate.</summary>
    private static object ResponseSchema => new
    {
        type = "object",
        properties = new
        {
            severity = new { type = "string", @enum = new[] { "Low", "Moderate", "High", "Critical" } },
            severityScore = new { type = "integer" },
            confidence = new { type = "number" },
            recommendedZoneStatus = new { type = "string", @enum = new[] { "Safe", "Caution", "Danger" } },
            recommendedRadiusMeters = new { type = "integer" },
            rationale = new { type = "string" }
        },
        required = new[]
        {
            "severity", "severityScore", "confidence",
            "recommendedZoneStatus", "recommendedRadiusMeters", "rationale"
        }
    };

    public async Task<SeverityProposal> ProposeAsync(
        IncidentAnalysisInput input, EvidenceBundle evidence, CancellationToken ct)
    {
        if (!llm.IsConfigured)
        {
            return RuleEngine(input, evidence,
                "No language model configured — deterministic rules applied.");
        }

        try
        {
            var raw = await llm.GenerateAsync(
                SystemInstruction,
                BuildPrompt(input, evidence),
                ResponseSchema,
                evidence.Images,
                ct);

            var result = JsonSerializer.Deserialize<IncidentAnalysisResult>(raw, AnalysisJson.Options)
                ?? throw new LlmUnavailableException("Model returned no parseable object.");

            return new SeverityProposal(result, llm.ModelName, AgentRunStatus.Succeeded, null, llm.LastAttempts);
        }
        catch (Exception ex) when (ex is LlmUnavailableException or JsonException)
        {
            // A safe, clearly recorded failure — never a silent one.
            logger.LogWarning(ex, "Model unusable for incident {Id}; using rule engine.", input.IncidentId);
            return RuleEngine(input, evidence, ex.Message, llm.LastAttempts);
        }
    }

    /// <summary>The deterministic floor, used when the model's answer is not accepted.</summary>
    public static SeverityProposal RuleEngine(
        IncidentAnalysisInput input, EvidenceBundle evidence, string reason, int attempts = 0) =>
        new(SeverityRules.Score(input, evidence.NearbyIncidents, evidence.RainfallMm),
            "rule-engine", AgentRunStatus.SucceededWithFallback, reason, attempts);

    private static string BuildPrompt(IncidentAnalysisInput input, EvidenceBundle evidence) =>
        $"""
        Incident report
        ---------------
        Title: {input.Title}
        Description: {input.Description}
        Reported type: {input.Type}
        District: {input.District ?? "unknown"}
        Coordinates: {input.Latitude:F4}, {input.Longitude:F4}
        Estimated people affected: {(input.EstimatedAffectedPeople?.ToString() ?? "not reported")}
        Photos attached to this message: {evidence.Images.Count}

        Tool evidence
        -------------
        Other active incidents within 5 km: {evidence.NearbyIncidents}
        Rainfall in the last 48 hours: {(!evidence.RainfallChecked ? "not applicable to this hazard" : evidence.RainfallMm is null ? "unavailable" : $"{evidence.RainfallMm:F0} mm")}

        Classify this incident.
        """;
}

/// <summary>
/// Validation agent. Deterministic — a language model is never trusted to stay
/// in range. Out-of-range numbers are clamped and reported; an unusable
/// rationale rejects the whole proposal.
/// </summary>
public class ProposalValidationAgent
{
    public ValidationOutcome Validate(IncidentAnalysisResult candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.Rationale))
        {
            return new ValidationOutcome(false, null, [], "Model returned an empty rationale.");
        }

        var adjustments = new List<string>();
        var score = Clamp(candidate.SeverityScore, 0, 100, "severityScore", adjustments);
        var confidence = Clamp(candidate.Confidence, 0, 1, "confidence", adjustments);
        var radius = Clamp(candidate.RecommendedRadiusMeters, 100, 20000, "recommendedRadiusMeters", adjustments);

        return new ValidationOutcome(true, candidate with
        {
            SeverityScore = score,
            Confidence = confidence,
            RecommendedRadiusMeters = radius,
            Rationale = candidate.Rationale.Trim()
        }, adjustments, null);
    }

    private static T Clamp<T>(T value, T min, T max, string field, List<string> adjustments)
        where T : IComparable<T>
    {
        var clamped = value.CompareTo(min) < 0 ? min : value.CompareTo(max) > 0 ? max : value;
        if (!EqualityComparer<T>.Default.Equals(value, clamped))
        {
            adjustments.Add($"{field} {value} clamped to {clamped}");
        }

        return clamped;
    }
}
