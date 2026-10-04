using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentA.Agents.IncidentEnrichmentAgent;
using RescueSriLanka.Api.Features.ComponentA.Agents.ZonePlanningAgent;
using RescueSriLanka.Api.Features.ComponentA.DTOs;
using RescueSriLanka.Api.Features.ComponentA.Models;
using RescueSriLanka.Api.Features.ComponentA.Services.Notifications;
using RescueSriLanka.Api.Models;
using EnrichmentAgent = RescueSriLanka.Api.Features.ComponentA.Agents.IncidentEnrichmentAgent.IncidentEnrichmentAgent;
using PlanningAgent = RescueSriLanka.Api.Features.ComponentA.Agents.ZonePlanningAgent.ZonePlanningAgent;

namespace RescueSriLanka.Api.Features.ComponentA.Services;
public interface IAgentRunService
{
    Task<IReadOnlyList<AgentRunDto>> QueryAsync(
        Guid? incidentId, int take, string? agentName = null, CancellationToken ct = default);

    /// <summary>Incident Analysis shorthand: accept the proposal, or revise its severity.</summary>
    Task<AgentRunDto?> ApproveAsync(Guid runId, IncidentSeverity? severity, Guid userId, string? note = null, CancellationToken ct = default);

    /// <summary>
    /// Approves any Component A agent's proposal, applying what the coordinator
    /// accepted. Throws <see cref="InvalidOperationException"/> (409) when the
    /// run cannot be decided, <see cref="ArgumentException"/> (400) for an
    /// edited zone that fails validation.
    /// </summary>
    Task<AgentRunDto?> ApproveWithAsync(Guid runId, ApproveAgentRunRequest request, Guid userId, CancellationToken ct = default);

    Task<AgentRunDto?> RejectAsync(Guid runId, string reason, Guid userId, CancellationToken ct = default);
}

/// <summary>
/// The human approval gate for every Component A agent. A proposal changes
/// nothing on its own — the severity in force, a corrected field, a merge or a
/// new zone only happen when a coordinator approves here, and the decision is
/// recorded against the run.
/// </summary>
public class AgentRunService(
    AppDbContext db,
    ISafetyZoneService zoneService,
    INotificationQueue notificationQueue,
    ILogger<AgentRunService> logger) : IAgentRunService
{
    public async Task<IReadOnlyList<AgentRunDto>> QueryAsync(
        Guid? incidentId, int take, string? agentName = null, CancellationToken ct = default)
    {
        var query = db.AgentRuns.AsNoTracking().AsQueryable();

        if (incidentId is not null)
        {
            query = query.Where(run => run.IncidentId == incidentId);
        }

        if (!string.IsNullOrWhiteSpace(agentName))
        {
            query = query.Where(run => run.AgentName == agentName);
        }

        var runs = await query
            .OrderByDescending(run => run.StartedAt)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(ct);

        return [.. runs.Select(AgentRunDto.FromRun)];
    }

    public Task<AgentRunDto?> ApproveAsync(
        Guid runId, IncidentSeverity? severity, Guid userId, string? note = null, CancellationToken ct = default) =>
        ApproveWithAsync(runId, new ApproveAgentRunRequest { Severity = severity, Note = note }, userId, ct);

    public async Task<AgentRunDto?> ApproveWithAsync(
        Guid runId, ApproveAgentRunRequest request, Guid userId, CancellationToken ct = default)
    {
        var run = await db.AgentRuns.FirstOrDefaultAsync(entity => entity.Id == runId, ct);
        if (run is null) return null;
        await EnsureAwaitingDecisionAsync(run, ct);

        // Everything is checked before anything changes, so a refused approval
        // leaves no trace. Each handler returns the change to make, which is
        // saved in the same write as the decision itself.
        Action apply = run.AgentName switch
        {
            EnrichmentAgent.AgentName => await PrepareEnrichmentAsync(run, request, userId, ct),
            PlanningAgent.AgentName => await PrepareZonePlanAsync(run, request, userId, ct),
            _ => await PrepareAnalysisAsync(run, request, userId, ct)
        };

        // The decision, the changes and the zone recompute stand or fall
        // together: an approval must never be saved without its zone.
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(ct)
            : null;

        run.Approved = true;
        run.ApprovedByUserId = userId;
        run.ApprovedAt = DateTime.UtcNow;
        run.DecisionNote = Clean(request.Note);
        if (run.Decision == AgentRunDecision.Pending) run.Decision = AgentRunDecision.Approved;

        apply();
        await SaveDecisionAsync(ct);
        await zoneService.RecomputeAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);

        // Approval is a moment a human stands behind the record, so it may warn
        // the district — queued only once the approval is durable. The
        // notification service warns each incident's district at most once.
        if (run.IncidentId is Guid warnedIncidentId && run.AgentName != PlanningAgent.AgentName)
        {
            notificationQueue.Enqueue(
                new NotificationJob(NotificationKind.DistrictWarning, warnedIncidentId));
        }

        logger.LogInformation(
            "Agent run {RunId} ({Agent}) {Decision} by {UserId}",
            runId, run.AgentName, run.Decision, userId);

        return AgentRunDto.FromRun(run);
    }

    // ------------------------------------------------------------ analysis

    private async Task<Action> PrepareAnalysisAsync(
        AgentRun run, ApproveAgentRunRequest request, Guid userId, CancellationToken ct)
    {
        var incident = run.IncidentId is Guid incidentId
            ? await db.Incidents.FirstOrDefaultAsync(e => e.Id == incidentId, ct)
            : null;
        if (incident is null) return () => { };

        // Null severity means "accept the proposal"; a value means revise.
        // Only a substituted severity counts as a human override.
        var severity = request.Severity;
        var applied = severity ?? incident.AiSeverity;
        var revised = severity is not null && severity != incident.AiSeverity;
        if (revised) run.Decision = AgentRunDecision.Revised;

        return () =>
        {
            if (applied is IncidentSeverity value)
            {
                incident.Severity = value;
                if (revised)
                {
                    incident.SeverityOverriddenBy = userId;
                    incident.SeverityOverriddenAt = DateTime.UtcNow;
                }
            }

            // Adopt the proposed radius so the zone matches the assessment.
            if (TryReadRadius(run.OutputJson) is int radius)
            {
                incident.AffectedRadiusMeters = radius;
            }

            incident.UpdatedAt = DateTime.UtcNow;
        };
    }

    // ---------------------------------------------------------- enrichment

    private async Task<Action> PrepareEnrichmentAsync(
        AgentRun run, ApproveAgentRunRequest request, Guid userId, CancellationToken ct)
    {
        var proposal = Read<EnrichmentResult>(run.OutputJson)
            ?? throw new InvalidOperationException("This run's proposal could not be read.");

        var incident = run.IncidentId is Guid incidentId
            ? await db.Incidents.Include(e => e.Images).FirstOrDefaultAsync(e => e.Id == incidentId, ct)
            : null;
        if (incident is null)
        {
            throw new InvalidOperationException("The incident this proposal is about no longer exists.");
        }
        if (incident.Status == IncidentStatus.Merged)
        {
            throw new InvalidOperationException("This report has already been merged into another.");
        }

        var unknown = (request.Fields ?? []).Except(proposal.Suggestions.Select(s => s.Field)).ToList();
        if (unknown.Count > 0)
        {
            throw new InvalidOperationException($"Not proposed by this run: {string.Join(", ", unknown)}.");
        }

        var accepted = request.Fields is null
            ? proposal.Suggestions
            : [.. proposal.Suggestions.Where(s => request.Fields.Contains(s.Field))];
        var merge = (request.MergeDuplicate ?? proposal.Duplicate is not null) && proposal.Duplicate is not null;

        if (request.MergeDuplicate == true && proposal.Duplicate is null)
        {
            throw new InvalidOperationException("This run did not propose a duplicate to merge into.");
        }

        Incident? kept = null;
        if (merge)
        {
            kept = await db.Incidents.FirstOrDefaultAsync(e => e.Id == proposal.Duplicate!.IncidentId, ct);
            if (kept is null || !kept.IsActive || kept.Status == IncidentStatus.Merged)
            {
                throw new InvalidOperationException(
                    "The report it would merge into is no longer open. Re-run the enrichment check.");
            }
        }

        if (accepted.Count < proposal.Suggestions.Count || merge != (proposal.Duplicate is not null))
        {
            run.Decision = AgentRunDecision.Revised;
        }

        return () =>
        {
            foreach (var suggestion in accepted)
            {
                ApplyField(incident, suggestion);
            }
            incident.UpdatedAt = DateTime.UtcNow;

            if (kept is not null)
            {
                // The earliest report is kept; this one becomes a pointer to it.
                // Its photos and head-count move across, so no evidence is lost.
                foreach (var image in incident.Images)
                {
                    image.IncidentId = kept.Id;
                }

                kept.EstimatedAffectedPeople = MaxPeople(kept.EstimatedAffectedPeople, incident.EstimatedAffectedPeople);
                kept.UpdatedAt = DateTime.UtcNow;

                incident.Status = IncidentStatus.Merged;
                incident.DuplicateOfIncidentId = kept.Id;
                incident.IsActive = false;
                incident.ResolvedAt = DateTime.UtcNow;
            }

            logger.LogInformation(
                "Enrichment {RunId} applied {Count} field(s){Merge} by {UserId}",
                run.Id, accepted.Count, kept is null ? "" : $" and merged into {kept.Id}", userId);
        };
    }

    private static void ApplyField(Incident incident, FieldSuggestion suggestion)
    {
        switch (suggestion.Field)
        {
            case EnrichmentFields.Title:
                incident.Title = suggestion.Proposed;
                break;
            case EnrichmentFields.Type:
                incident.Type = Enum.Parse<IncidentType>(suggestion.Proposed, ignoreCase: true);
                break;
            case EnrichmentFields.District:
                incident.District = SriLankaDistricts.Normalise(suggestion.Proposed);
                break;
            case EnrichmentFields.People:
                incident.EstimatedAffectedPeople = int.Parse(suggestion.Proposed, CultureInfo.InvariantCulture);
                break;
            case EnrichmentFields.Address:
                incident.AddressText = suggestion.Proposed;
                break;
        }
    }

    private static int? MaxPeople(int? a, int? b) =>
        a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);

    // -------------------------------------------------------- zone planning

    private async Task<Action> PrepareZonePlanAsync(
        AgentRun run, ApproveAgentRunRequest request, Guid userId, CancellationToken ct)
    {
        var plan = Read<ZonePlanResult>(run.OutputJson)
            ?? throw new InvalidOperationException("This run's plan could not be read.");

        var proposedCreates = plan.Zones.Where(z => z.Action == ZoneActions.Create).ToList();
        var proposedRetires = plan.Zones.Where(z => z.Action == ZoneActions.Retire && z.ZoneId is not null)
            .Select(z => z.ZoneId!.Value).ToList();

        // A coordinator's edited draft goes through the same checks as a zone
        // they declare by hand; BuildManual throws before anything is written.
        var creates = request.Zones is null
            ? proposedCreates.Select(ToRequest).ToList()
            : [.. request.Zones];
        var zones = creates.Select(zone => SafetyZoneService.BuildManual(zone, userId, run.Id)).ToList();

        var unknown = (request.RetireZoneIds ?? []).Except(proposedRetires).ToList();
        if (unknown.Count > 0)
        {
            throw new InvalidOperationException("Only zones this plan proposed retiring can be retired through it.");
        }
        var retires = request.RetireZoneIds ?? proposedRetires;

        if (request.Zones is not null || (request.RetireZoneIds is not null && retires.Count < proposedRetires.Count))
        {
            run.Decision = AgentRunDecision.Revised;
        }

        var retiring = await db.SafetyZones
            .Where(zone => retires.Contains(zone.Id) && zone.Source == ZoneSource.ManualOverride)
            .ToListAsync(ct);

        return () =>
        {
            db.SafetyZones.AddRange(zones);
            foreach (var zone in retiring)
            {
                zone.IsActive = false;
            }

            logger.LogInformation(
                "Zone plan {RunId}: {Created} zone(s) created, {Retired} retired by {UserId}",
                run.Id, zones.Count, retiring.Count, userId);
        };
    }

    private static SafetyZoneRequest ToRequest(ZoneProposal zone) => new()
    {
        Name = zone.Name,
        Status = zone.Status,
        CenterLatitude = zone.CenterLatitude,
        CenterLongitude = zone.CenterLongitude,
        RadiusMeters = zone.RadiusMeters,
        District = zone.District,
        Rationale = zone.Rationale,
        ExpiresAt = zone.ExpiresInHours is int hours ? DateTime.UtcNow.AddHours(hours) : null
    };

    // ------------------------------------------------------------- reject

    public async Task<AgentRunDto?> RejectAsync(
        Guid runId, string reason, Guid userId, CancellationToken ct = default)
    {
        var run = await db.AgentRuns.FirstOrDefaultAsync(entity => entity.Id == runId, ct);
        if (run is null) return null;
        await EnsureAwaitingDecisionAsync(run, ct);

        // Rejection leaves the incident untouched and records why. The reason
        // goes in DecisionNote; ErrorMessage stays for how the run itself went.
        run.Approved = false;
        run.Decision = AgentRunDecision.Rejected;
        run.ApprovedByUserId = userId;
        run.ApprovedAt = DateTime.UtcNow;
        run.DecisionNote = Clean(reason);

        await SaveDecisionAsync(ct);
        logger.LogInformation("Agent run {RunId} rejected by {UserId}", runId, userId);

        return AgentRunDto.FromRun(run);
    }

    /// <summary>
    /// A proposal takes exactly one decision, on the newest run of the same
    /// agent for the same incident (or, for the zone planner, the newest plan).
    /// Without this a rejected run could later be approved, an old run could
    /// overwrite a newer proposal, and every repeat approval would re-send the
    /// district warning. Throws <see cref="InvalidOperationException"/>, which
    /// the controller reports as 409 Conflict.
    /// </summary>
    private async Task EnsureAwaitingDecisionAsync(AgentRun run, CancellationToken ct)
    {
        if (run.Status is not (AgentRunStatus.Succeeded or AgentRunStatus.SucceededWithFallback))
        {
            throw new InvalidOperationException(
                $"This run has no proposal to decide on (status: {run.Status}).");
        }

        if (run.Decision != AgentRunDecision.Pending || run.ApprovedAt is not null)
        {
            throw new InvalidOperationException(
                run.Decision == AgentRunDecision.Rejected
                    ? "This proposal has already been rejected."
                    : "This proposal has already been approved.");
        }

        // Runs of other agents on the same incident are separate proposals:
        // a fresh enrichment check never blocks approving the severity.
        if (await db.AgentRuns.AnyAsync(other =>
                other.AgentName == run.AgentName &&
                other.IncidentId == run.IncidentId &&
                other.Id != run.Id &&
                other.StartedAt > run.StartedAt &&
                other.Status != AgentRunStatus.Failed, ct))
        {
            throw new InvalidOperationException(
                run.IncidentId is null
                    ? "A newer plan exists; review that one instead."
                    : "A newer analysis exists for this incident; review that proposal instead.");
        }
    }

    /// <summary>
    /// Saves the decision. ApprovedAt is a concurrency token, so if another
    /// coordinator decided this run first the update matches no row and this
    /// request loses cleanly instead of deciding twice.
    /// </summary>
    private async Task SaveDecisionAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException(
                "This proposal was decided by someone else a moment ago.");
        }
    }

    private static string? Clean(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static readonly JsonSerializerOptions ReadJson = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private static T? Read<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json, ReadJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Pulls the proposed radius out of the persisted structured output.</summary>
    private static int? TryReadRadius(string? outputJson)
    {
        if (string.IsNullOrWhiteSpace(outputJson)) return null;

        try
        {
            using var document = JsonDocument.Parse(outputJson);
            if (document.RootElement.TryGetProperty("recommendedRadiusMeters", out var value) &&
                value.TryGetInt32(out var radius))
            {
                return Math.Clamp(radius, 100, 20000);
            }
        }
        catch (JsonException)
        {
            // A malformed run output must never block an approval.
        }

        return null;
    }
}
