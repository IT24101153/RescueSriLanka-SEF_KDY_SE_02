using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Services.Llm;
using RescueSriLanka.Api.Features.ComponentA.Services;

namespace RescueSriLanka.Api.Features.ComponentA.Agents.IncidentAnalysisAgent;
public interface IIncidentAnalysisAgent
{
    /// <summary>Analyses an incident and persists the run. Never throws for model failure.</summary>
    Task<IncidentAnalysisResult> AnalyseAsync(Guid incidentId, CancellationToken ct = default);
}

/// <summary>
/// Component A's agent. Classifies severity and safety-zone status from an
/// incident's text, location and context.
///
/// Contract with the Coordinator/Planner Agent (Student B): call
/// <see cref="AnalyseAsync"/> with an incident id; receive a validated
/// <see cref="IncidentAnalysisResult"/>. The agent proposes only — it never
/// writes the incident's severity in force. A coordinator approves that.
/// </summary>
public class IncidentAnalysisAgent(
    AppDbContext db,
    ILlmClient llm,
    IncidentAnalysisTools tools,
    IImageStorageService imageStorage,
    ILogger<IncidentAnalysisAgent> logger) : IIncidentAnalysisAgent
{
    public const string AgentName = "IncidentAnalysisAgent";

    /// <summary>The model emits enum names, so parsing must accept them.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true
    };

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

    public async Task<IncidentAnalysisResult> AnalyseAsync(
        Guid incidentId, CancellationToken ct = default)
    {
        var incident = await db.Incidents.FirstOrDefaultAsync(e => e.Id == incidentId, ct)
            ?? throw new InvalidOperationException($"Incident {incidentId} not found.");

        var input = new IncidentAnalysisInput
        {
            IncidentId = incident.Id,
            Title = incident.Title,
            Description = incident.Description,
            Type = incident.Type,
            Latitude = incident.Latitude,
            Longitude = incident.Longitude,
            District = incident.District,
            EstimatedAffectedPeople = incident.EstimatedAffectedPeople,
            ImageCount = await db.IncidentImages.CountAsync(i => i.IncidentId == incident.Id, ct)
        };

        var run = new AgentRun
        {
            AgentName = AgentName,
            Objective = $"Classify severity and zone status for incident {incident.Id}",
            IncidentId = incident.Id,
            InputJson = JsonSerializer.Serialize(input, JsonOptions)
        };
        db.AgentRuns.Add(run);
        await db.SaveChangesAsync(ct);

        var stopwatch = Stopwatch.StartNew();

        // ---- Step 1: gather evidence from allow-listed tools ----
        var nearby = await tools.CountNearbyActiveIncidentsAsync(
            input.Latitude, input.Longitude, 5, incident.Id, ct);
        var rainfall = await tools.GetRainfallLast48hAsync(input.Latitude, input.Longitude, ct);

        // Photos are evidence too: at most 3, capped at 6 MB in total so the
        // request stays well inside the provider's limits.
        var photos = await imageStorage.LoadForAnalysisAsync(incident.Id, 3, 6 * 1024 * 1024, ct);
        var images = photos.Select(photo => new LlmImage(photo.MimeType, photo.Data)).ToList();

        var toolCalls = new Dictionary<string, object?>
        {
            ["count_nearby_active_incidents"] = new { radiusKm = 5, result = nearby },
            ["get_rainfall_last_48h"] = new { result = rainfall, available = rainfall is not null },
            ["load_incident_images"] = new { requested = 3, loaded = images.Count }
        };
        run.ToolCallsJson = JsonSerializer.Serialize(toolCalls);

        // ---- Step 2: reason, with a deterministic floor ----
        IncidentAnalysisResult result;

        if (!llm.IsConfigured)
        {
            result = SeverityRules.Score(input, nearby, rainfall);
            run.ErrorMessage = "No language model configured — deterministic rules applied.";
            run.Status = AgentRunStatus.SucceededWithFallback;
            run.Model = "rule-engine";
        }
        else
        {
            try
            {
                var raw = await llm.GenerateAsync(
                    SystemInstruction,
                    BuildPrompt(input, nearby, rainfall, images.Count),
                    ResponseSchema,
                    images,
                    ct);

                result = Validate(JsonSerializer.Deserialize<IncidentAnalysisResult>(raw, JsonOptions)
                    ?? throw new LlmUnavailableException("Model returned no parseable object."));

                run.Status = AgentRunStatus.Succeeded;
                run.Model = llm.ModelName;
            }
            catch (Exception ex) when (ex is LlmUnavailableException or JsonException)
            {
                // A safe, clearly recorded failure — never a silent one.
                logger.LogWarning(ex, "Model unusable for incident {Id}; using rule engine.", incident.Id);
                result = SeverityRules.Score(input, nearby, rainfall);
                run.ErrorMessage = ex.Message;
                run.Status = AgentRunStatus.SucceededWithFallback;
                run.Model = "rule-engine";
            }
        }

        result = result with { ToolResults = toolCalls!.ToDictionary(kv => kv.Key, kv => kv.Value!) };

        // ---- Step 3: persist the proposal (never the severity in force) ----
        stopwatch.Stop();
        run.OutputJson = JsonSerializer.Serialize(result, JsonOptions);
        run.UsedFallback = result.UsedFallback;
        run.DurationMs = (int)stopwatch.ElapsedMilliseconds;
        run.CompletedAt = DateTime.UtcNow;

        incident.AiSeverity = result.Severity;
        incident.AiSeverityScore = result.SeverityScore;
        incident.AiConfidence = result.Confidence;
        incident.AiRationale = result.Rationale;
        incident.AiAnalysedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Incident {Id} analysed: {Severity} ({Score}/100){Fallback}",
            incident.Id, result.Severity, result.SeverityScore,
            result.UsedFallback ? " via rule engine" : string.Empty);

        return result;
    }

    private static string BuildPrompt(
        IncidentAnalysisInput input, int nearby, double? rainfall, int photosAttached) =>
        $"""
        Incident report
        ---------------
        Title: {input.Title}
        Description: {input.Description}
        Reported type: {input.Type}
        District: {input.District ?? "unknown"}
        Coordinates: {input.Latitude:F4}, {input.Longitude:F4}
        Estimated people affected: {(input.EstimatedAffectedPeople?.ToString() ?? "not reported")}
        Photos attached to this message: {photosAttached}

        Tool evidence
        -------------
        Other active incidents within 5 km: {nearby}
        Rainfall in the last 48 hours: {(rainfall is null ? "unavailable" : $"{rainfall:F0} mm")}

        Classify this incident.
        """;

    /// <summary>
    /// Deterministic checks on the model's output. A language model is never
    /// trusted to stay in range — anything outside it is clamped, and an
    /// unusable rationale rejects the whole response.
    /// </summary>
    private static IncidentAnalysisResult Validate(IncidentAnalysisResult candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.Rationale))
        {
            throw new LlmUnavailableException("Model returned an empty rationale.");
        }

        return candidate with
        {
            SeverityScore = Math.Clamp(candidate.SeverityScore, 0, 100),
            Confidence = Math.Clamp(candidate.Confidence, 0, 1),
            RecommendedRadiusMeters = Math.Clamp(candidate.RecommendedRadiusMeters, 100, 20000),
            Rationale = candidate.Rationale.Trim()
        };
    }
}
