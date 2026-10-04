using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentA.Agents.IncidentAnalysisAgent;
using RescueSriLanka.Api.Features.ComponentA.Agents.Shared;
using RescueSriLanka.Api.Features.ComponentA.Models;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Services;
using RescueSriLanka.Api.Services.Llm;

namespace RescueSriLanka.Api.Features.ComponentA.Agents.ZonePlanningAgent;

public interface IZonePlanningAgent
{
    /// <summary>
    /// Reviews the whole zone layer against the approved incidents and persists
    /// a plan — new area zones and zones to retire — as an agent run. Changes
    /// nothing on the map until a coordinator approves.
    /// </summary>
    Task<ZonePlanResult> PlanAsync(CancellationToken ct = default);
}

//   Planner    snapshot            -> plan             touches nothing
//   Evidence   snapshot            -> evidence         read-only tools: incidents, clusters, rain, zones
//   Reasoning  evidence            -> raw plan         the language model / rule engine
//   Validator  raw plan            -> safe plan        nothing (pure function)

public static class ZonePlanningRoles
{
    public const string Evidence = "ZoneEvidenceAgent";
    public const string Reasoning = "ZoneReasoningAgent";
    public const string Validator = "ZoneValidationAgent";
}

public static class ZonePlanningTools
{
    public const string ListIncidents = "list_approved_active_incidents";
    public const string Cluster = "cluster_incidents";
    public const string Rainfall = "get_rainfall_last_48h";
    public const string ListZones = "list_manual_zones";
}

/// <summary>
/// Component A's third agent. Looks above single incidents at the operational
/// picture: groups of approved incidents that are really one event and need one
/// area zone, and coordinator-declared zones that have outlived their hazard.
/// </summary>
public class ZonePlanningAgent(
    AppDbContext db,
    ILlmClient llm,
    IncidentAnalysisTools tools,
    ILogger<ZonePlanningAgent> logger) : IZonePlanningAgent
{
    public const string AgentName = "ZonePlanningAgent";

    public async Task<ZonePlanResult> PlanAsync(CancellationToken ct = default)
    {
        var tracer = new AgentRunTracer(db, new AgentRun
        {
            AgentName = AgentName,
            Objective = "Plan area safety zones from approved incidents and retire stale manual zones",
            InputJson = JsonSerializer.Serialize(new { requestedAt = DateTime.UtcNow })
        });
        await tracer.StartAsync(ct);

        try
        {
            var evidenceAgent = new ZoneEvidenceAgent(db, tools);

            // The planner needs to know what exists before it decides which
            // tools are worth running, so the snapshot is read first.
            var snapshot = await evidenceAgent.SnapshotAsync(ct);
            var plan = ZonePlanner.Plan(snapshot);
            await tracer.SetPlanAsync(plan.Steps, plan.Notes, ct);

            var evidence = await tracer.StepAsync(0, async () =>
            {
                var bundle = await evidenceAgent.GatherAsync(snapshot, plan, ct);
                return (bundle, $"{bundle.Incidents.Count} incident(s), {bundle.Clusters.Count} cluster(s), "
                                + $"{bundle.ManualZones.Count} manual zone(s)");
            }, ct);
            tracer.Run.ToolCallsJson = JsonSerializer.Serialize(evidence.ToolCalls);

            var raw = await tracer.StepAsync(1, async () =>
            {
                var proposal = await new ZoneReasoningAgent(llm, logger).ProposeAsync(evidence, ct);
                return (proposal, $"{proposal.Model}: {proposal.Plan.Zones?.Count ?? 0} proposal(s)");
            }, ct);

            var validated = await tracer.StepAsync(2, () =>
            {
                var outcome = ZonePlanValidator.Validate(evidence, raw.Plan, raw.UsedFallback);
                return Task.FromResult((outcome.Result, outcome.Adjustments.Count == 0
                    ? "accepted"
                    : "accepted with changes: " + string.Join("; ", outcome.Adjustments)));
            }, ct);

            await tracer.CompleteAsync(
                raw.UsedFallback ? AgentRunStatus.SucceededWithFallback : AgentRunStatus.Succeeded,
                raw.Model, raw.Attempts, raw.Error,
                JsonSerializer.Serialize(validated, AnalysisJson.Options),
                raw.UsedFallback, ct);

            logger.LogInformation("Zone plan proposed {Count} change(s).", validated.Zones.Count);
            return validated;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Zone planning failed.");
            await tracer.FailAsync(ex, logger);
            throw;
        }
    }
}

/// <summary>What exists right now, read before planning.</summary>
public record ZoneSnapshot(
    IReadOnlyList<PlanningIncident> Incidents,
    IReadOnlyList<IncidentCluster> Clusters,
    int ManualZoneCount);

/// <summary>Deterministic: the plan depends only on the data, never on a model.</summary>
public static class ZonePlanner
{
    public static AnalysisPlan Plan(ZoneSnapshot snapshot)
    {
        var tools = new List<string> { ZonePlanningTools.ListIncidents, ZonePlanningTools.ListZones };
        var notes = new List<string>();

        if (snapshot.Incidents.Count < 2)
        {
            notes.Add("Clustering skipped: fewer than two approved active incidents.");
        }
        else
        {
            tools.Add(ZonePlanningTools.Cluster);
        }

        if (snapshot.Clusters.Any(cluster => ZonePlanningRules.IsWeatherDriven(cluster.DominantType)))
        {
            tools.Add(ZonePlanningTools.Rainfall);
        }
        else
        {
            notes.Add("Rainfall lookup skipped: no weather-driven cluster.");
        }

        if (snapshot.ManualZoneCount == 0)
        {
            notes.Add("No manual zones exist, so none can be retired.");
        }

        return new AnalysisPlan(Guid.Empty,
        [
            new(1, ZonePlanningRoles.Evidence, "Gather incidents, clusters, rainfall and existing zones", tools),
            new(2, ZonePlanningRoles.Reasoning, "Propose area zones to create and zones to retire", []),
            new(3, ZonePlanningRoles.Validator, "Validate every zone against bounds, evidence and existing zones", [])
        ], notes);
    }
}

/// <summary>Read-only tools. The only role that touches the database or the network.</summary>
public class ZoneEvidenceAgent(AppDbContext db, IncidentAnalysisTools tools)
{
    /// <summary>Caps external weather calls per run.</summary>
    public const int MaxRainfallLookups = 5;

    public async Task<ZoneSnapshot> SnapshotAsync(CancellationToken ct)
    {
        // Only reports a coordinator has approved may shape the public map.
        var incidents = await db.Incidents.AsNoTracking()
            .Where(incident => incident.IsActive &&
                               incident.Status != IncidentStatus.Reported &&
                               incident.Status != IncidentStatus.Merged)
            .Select(incident => new PlanningIncident(
                incident.Id, incident.Title, incident.Type, incident.Severity,
                incident.Latitude, incident.Longitude, incident.AffectedRadiusMeters,
                incident.District, incident.EstimatedAffectedPeople))
            .ToListAsync(ct);

        var manual = await db.SafetyZones.AsNoTracking()
            .CountAsync(zone => zone.IsActive && zone.Source == ZoneSource.ManualOverride, ct);

        return new ZoneSnapshot(incidents, ZonePlanningRules.Cluster(incidents), manual);
    }

    public async Task<ZonePlanningEvidence> GatherAsync(
        ZoneSnapshot snapshot, AnalysisPlan plan, CancellationToken ct)
    {
        var toolCalls = new Dictionary<string, object?>
        {
            [ZonePlanningTools.ListIncidents] = new { result = snapshot.Incidents.Count }
        };

        var clusters = plan.Uses(ZonePlanningTools.Cluster) ? snapshot.Clusters : [];
        toolCalls[ZonePlanningTools.Cluster] = plan.Uses(ZonePlanningTools.Cluster)
            ? new { linkKm = ZonePlanningRules.LinkDistanceKm, result = clusters.Select(c => c.IncidentIds.Count) }
            : new { skipped = true };

        if (plan.Uses(ZonePlanningTools.Rainfall))
        {
            var lookups = 0;
            var withRain = new List<IncidentCluster>();
            foreach (var cluster in clusters)
            {
                double? rain = null;
                if (ZonePlanningRules.IsWeatherDriven(cluster.DominantType) && lookups < MaxRainfallLookups)
                {
                    lookups++;
                    rain = await tools.GetRainfallLast48hAsync(cluster.CenterLatitude, cluster.CenterLongitude, ct);
                }
                withRain.Add(cluster with { RainfallMm = rain });
            }
            clusters = withRain;
            toolCalls[ZonePlanningTools.Rainfall] = new
            {
                lookups,
                result = clusters.Select(c => c.RainfallMm)
            };
        }
        else
        {
            toolCalls[ZonePlanningTools.Rainfall] = new { skipped = true };
        }

        var zones = await db.SafetyZones.AsNoTracking()
            .Where(zone => zone.IsActive && zone.Source == ZoneSource.ManualOverride)
            .ToListAsync(ct);

        var manual = zones.Select(zone => new ExistingManualZone(
            zone.Id, zone.Name, zone.Status, zone.CenterLatitude, zone.CenterLongitude,
            zone.RadiusMeters, zone.ComputedAt, zone.ExpiresAt,
            snapshot.Incidents.Count(incident =>
                GeoService.DistanceKm(zone.CenterLatitude, zone.CenterLongitude,
                    incident.Latitude, incident.Longitude) * 1000 <= zone.RadiusMeters + 1000)))
            .ToList();
        toolCalls[ZonePlanningTools.ListZones] = new { result = manual.Count };

        return new ZonePlanningEvidence(snapshot.Incidents, clusters, manual, toolCalls);
    }
}

public record RawZonePlan(ModelZonePlan Plan, string Model, bool UsedFallback, string? Error, int Attempts);

/// <summary>The model, with the rule engine as its floor. No tools, no database.</summary>
public class ZoneReasoningAgent(ILlmClient llm, ILogger logger)
{
    private const string SystemInstruction =
        """
        You are the Zone Planning Agent for a Sri Lankan disaster response
        platform. Each approved incident already has its own small safety zone.
        You plan the layer above that for an emergency coordinator:

        - "create": one area zone covering a cluster of incidents that are really
          one event, so the public sees a single clear boundary. Centre it on the
          cluster, make the radius cover every incident's affected area plus a
          safety margin, and set status Danger when any incident is High or
          Critical, otherwise Caution. Give it a short plain name, an expiry in
          hours, and list the incident ids it is based on (only ids you were given).
        - "retire": an existing manual zone (by its id) that is expired or no
          longer has any active incident inside it.

        Skip a cluster already covered by an existing manual zone. Never invent
        incidents, ids or coordinates outside Sri Lanka. If nothing needs to
        change, return an empty list. Return only the JSON object described by
        the schema; each rationale is one sentence citing the evidence.
        """;

    private static object ResponseSchema => new
    {
        type = "object",
        properties = new
        {
            zones = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        action = new { type = "string", @enum = new[] { ZoneActions.Create, ZoneActions.Retire } },
                        zoneId = new { type = "string" },
                        name = new { type = "string" },
                        status = new { type = "string", @enum = new[] { "Safe", "Caution", "Danger" } },
                        centerLatitude = new { type = "number" },
                        centerLongitude = new { type = "number" },
                        radiusMeters = new { type = "integer" },
                        district = new { type = "string" },
                        expiresInHours = new { type = "integer" },
                        rationale = new { type = "string" },
                        basedOnIncidentIds = new { type = "array", items = new { type = "string" } }
                    },
                    required = new[] { "action", "rationale" }
                }
            },
            summary = new { type = "string" }
        },
        required = new[] { "zones", "summary" }
    };

    public async Task<RawZonePlan> ProposeAsync(ZonePlanningEvidence evidence, CancellationToken ct)
    {
        if (!llm.IsConfigured)
        {
            return Fallback(evidence, "No language model configured — deterministic rules applied.", 0);
        }

        // Nothing to plan from: asking the model would only invite invention.
        if (evidence.Clusters.Count == 0 && evidence.ManualZones.Count == 0)
        {
            return Fallback(evidence, null, 0);
        }

        try
        {
            var raw = await llm.GenerateAsync(SystemInstruction, BuildPrompt(evidence), ResponseSchema, null, ct);
            var plan = JsonSerializer.Deserialize<ModelZonePlan>(raw, AnalysisJson.Options)
                ?? throw new LlmUnavailableException("Model returned no parseable object.");
            return new RawZonePlan(plan, llm.ModelName, false, null, llm.LastAttempts);
        }
        catch (Exception ex) when (ex is LlmUnavailableException or JsonException)
        {
            logger.LogWarning(ex, "Model unusable for zone planning; using rule engine.");
            return Fallback(evidence, ex.Message, llm.LastAttempts);
        }
    }

    public static RawZonePlan Fallback(ZonePlanningEvidence evidence, string? reason, int attempts)
    {
        var rules = ZonePlanningRules.Propose(evidence);
        var plan = new ModelZonePlan
        {
            Summary = rules.Summary,
            Zones = [.. rules.Zones.Select(zone => new ModelZone
            {
                Action = zone.Action,
                ZoneId = zone.ZoneId?.ToString(),
                Name = zone.Name,
                Status = zone.Status.ToString(),
                CenterLatitude = zone.CenterLatitude,
                CenterLongitude = zone.CenterLongitude,
                RadiusMeters = zone.RadiusMeters,
                District = zone.District,
                ExpiresInHours = zone.ExpiresInHours,
                Rationale = zone.Rationale,
                BasedOnIncidentIds = [.. zone.BasedOnIncidentIds.Select(id => id.ToString())]
            })]
        };
        return new RawZonePlan(plan, "rule-engine", true, reason, attempts);
    }

    private static string BuildPrompt(ZonePlanningEvidence evidence)
    {
        var incidents = evidence.Incidents.ToDictionary(i => i.Id);

        var clusters = evidence.Clusters.Count == 0
            ? "  (none)"
            : string.Join("\n", evidence.Clusters.Select(c =>
                FormattableString.Invariant(
                    $"  Cluster {c.Index + 1}: centre {c.CenterLatitude:F4}, {c.CenterLongitude:F4}; covers {c.SpanRadiusMeters} m; worst {c.WorstSeverity}; mostly {c.DominantType}; district {c.District ?? "mixed"}; people {c.People}; rain 48h {(c.RainfallMm is double r ? $"{r:F0} mm" : "n/a")}")
                + "\n"
                + string.Join("\n", c.IncidentIds.Select(id =>
                    $"    - id {id}: \"{incidents[id].Title}\" ({incidents[id].Severity} {incidents[id].Type}, radius {incidents[id].RadiusMeters} m)"))));

        var zones = evidence.ManualZones.Count == 0
            ? "  (none)"
            : string.Join("\n", evidence.ManualZones.Select(z =>
                FormattableString.Invariant(
                    $"  - id {z.Id}: \"{z.Name}\" {z.Status}, centre {z.CenterLatitude:F4}, {z.CenterLongitude:F4}, radius {z.RadiusMeters} m, expires {(z.ExpiresAt is DateTime e ? e.ToString("u") : "never")}, active incidents inside {z.ActiveIncidentsInside}")));

        return $"""
            Now (UTC): {DateTime.UtcNow:u}

            Incident clusters (approved, active, within {ZonePlanningRules.LinkDistanceKm} km of each other)
            ----------------------------------------------------------------
            {clusters}

            Existing manual zones
            ---------------------
            {zones}

            Propose the zone changes.
            """;
    }
}

public record ZonePlanValidation(ZonePlanResult Result, IReadOnlyList<string> Adjustments);

/// <summary>
/// Deterministic, and has the last word. Coordinates must be on the island, a
/// radius and expiry within range, every incident id one the Evidence agent
/// found, and a retire must name a manual zone that exists. The same checks
/// guard a coordinator's edited draft when it is approved.
/// </summary>
public static class ZonePlanValidator
{
    public const int MaxProposals = 10;

    // The island with a small margin, matching the console's map bounds.
    public const double MinLatitude = 5.7, MaxLatitude = 10.0, MinLongitude = 79.4, MaxLongitude = 82.1;

    public static bool OnIsland(double latitude, double longitude) =>
        latitude is >= MinLatitude and <= MaxLatitude && longitude is >= MinLongitude and <= MaxLongitude;

    public static ZonePlanValidation Validate(
        ZonePlanningEvidence evidence, ModelZonePlan plan, bool usedFallback)
    {
        var adjustments = new List<string>();
        var accepted = new List<ZoneProposal>();
        var incidentIds = evidence.Incidents.Select(i => i.Id).ToHashSet();
        var manual = evidence.ManualZones.ToDictionary(z => z.Id);

        foreach (var (zone, index) in (plan.Zones ?? []).Select((zone, index) => (zone, index + 1)))
        {
            if (accepted.Count == MaxProposals)
            {
                adjustments.Add($"kept only the first {MaxProposals} proposals");
                break;
            }

            var action = zone.Action?.Trim().ToLowerInvariant();

            if (action == ZoneActions.Retire)
            {
                if (!Guid.TryParse(zone.ZoneId, out var id) || !manual.TryGetValue(id, out var existing))
                {
                    adjustments.Add($"#{index}: dropped retire — not an existing manual zone");
                    continue;
                }
                if (accepted.Any(a => a.ZoneId == id))
                {
                    adjustments.Add($"#{index}: dropped a repeated retire");
                    continue;
                }

                accepted.Add(new ZoneProposal
                {
                    Action = ZoneActions.Retire,
                    ZoneId = id,
                    Name = existing.Name,
                    Status = existing.Status,
                    CenterLatitude = existing.CenterLatitude,
                    CenterLongitude = existing.CenterLongitude,
                    RadiusMeters = existing.RadiusMeters,
                    Rationale = Rationale(zone.Rationale, "Proposed for retirement.")
                });
                continue;
            }

            if (action != ZoneActions.Create)
            {
                adjustments.Add($"#{index}: dropped unknown action '{zone.Action}'");
                continue;
            }

            if (zone.CenterLatitude is not double lat || zone.CenterLongitude is not double lng || !OnIsland(lat, lng))
            {
                adjustments.Add($"#{index}: dropped — centre missing or outside Sri Lanka");
                continue;
            }

            if (!Enum.TryParse<ZoneStatus>(zone.Status, true, out var status) || !Enum.IsDefined(status))
            {
                adjustments.Add($"#{index}: dropped — status '{zone.Status}' unknown");
                continue;
            }

            var based = (zone.BasedOnIncidentIds ?? [])
                .Select(text => Guid.TryParse(text, out var id) ? id : Guid.Empty)
                .Where(incidentIds.Contains)
                .Distinct()
                .ToList();
            if (based.Count < (zone.BasedOnIncidentIds?.Count ?? 0))
            {
                adjustments.Add($"#{index}: removed incident ids that were not in the evidence");
            }
            if (status != ZoneStatus.Safe && based.Count == 0)
            {
                adjustments.Add($"#{index}: dropped — a {status} zone must rest on an approved incident");
                continue;
            }

            var radius = ZonePlanningRules.RoundRadius(zone.RadiusMeters ?? 0);
            if (zone.RadiusMeters is not double requested || Math.Abs(requested - radius) >= 100)
            {
                adjustments.Add($"#{index}: radius set to {radius} m");
            }

            var hours = (int)Math.Clamp(Math.Round(zone.ExpiresInHours ?? 48), 1, ZonePlanningRules.MaxExpiryHours);

            accepted.Add(new ZoneProposal
            {
                Action = ZoneActions.Create,
                Name = AgentRunTracer.Truncate(
                    string.IsNullOrWhiteSpace(zone.Name) ? $"{status} area" : zone.Name.Trim(), 200),
                Status = status,
                CenterLatitude = Math.Round(lat, 5),
                CenterLongitude = Math.Round(lng, 5),
                RadiusMeters = radius,
                District = SriLankaDistricts.Normalise(zone.District),
                ExpiresInHours = hours,
                Rationale = Rationale(zone.Rationale, "Proposed by the zone planning agent."),
                BasedOnIncidentIds = based
            });
        }

        var creates = accepted.Count(z => z.Action == ZoneActions.Create);
        return new ZonePlanValidation(new ZonePlanResult
        {
            Zones = accepted,
            Summary = string.IsNullOrWhiteSpace(plan.Summary)
                ? $"{creates} zone(s) to create, {accepted.Count - creates} to retire."
                : AgentRunTracer.Truncate(plan.Summary.Trim(), 500),
            UsedFallback = usedFallback
        }, adjustments);
    }

    private static string Rationale(string? text, string fallback) =>
        string.IsNullOrWhiteSpace(text) ? fallback : AgentRunTracer.Truncate(text.Trim(), 500);
}
