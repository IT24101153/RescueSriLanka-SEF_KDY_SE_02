using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentA.DTOs;
using RescueSriLanka.Api.Features.ComponentA.Models;
using RescueSriLanka.Api.Features.ComponentA.Services.Notifications;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Features.ComponentA.Services;
public interface IAgentRunService
{
    Task<IReadOnlyList<AgentRunDto>> QueryAsync(Guid? incidentId, int take, CancellationToken ct = default);
    Task<AgentRunDto?> ApproveAsync(Guid runId, IncidentSeverity? severity, Guid userId, string? note = null, CancellationToken ct = default);
    Task<AgentRunDto?> RejectAsync(Guid runId, string reason, Guid userId, CancellationToken ct = default);
}

/// <summary>
/// The human approval gate. An agent proposal changes nothing on its own — the
/// severity in force and the affected radius only move when a coordinator
/// approves here, and the decision is recorded against the run.
/// </summary>
public class AgentRunService(
    AppDbContext db,
    ISafetyZoneService zoneService,
    INotificationQueue notificationQueue,
    ILogger<AgentRunService> logger) : IAgentRunService
{
    public async Task<IReadOnlyList<AgentRunDto>> QueryAsync(
        Guid? incidentId, int take, CancellationToken ct = default)
    {
        var query = db.AgentRuns.AsNoTracking().AsQueryable();

        if (incidentId is not null)
        {
            query = query.Where(run => run.IncidentId == incidentId);
        }

        var runs = await query
            .OrderByDescending(run => run.StartedAt)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(ct);

        return [.. runs.Select(AgentRunDto.FromRun)];
    }

    public async Task<AgentRunDto?> ApproveAsync(
        Guid runId, IncidentSeverity? severity, Guid userId, string? note = null, CancellationToken ct = default)
    {
        var run = await db.AgentRuns.FirstOrDefaultAsync(entity => entity.Id == runId, ct);
        if (run is null) return null;
        await EnsureAwaitingDecisionAsync(run, ct);

        // The decision, the incident change and the zone recompute stand or
        // fall together: an approval must never be saved without its zone.
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(ct)
            : null;

        run.Approved = true;
        run.ApprovedByUserId = userId;
        run.ApprovedAt = DateTime.UtcNow;
        run.DecisionNote = Clean(note);
        run.Decision = AgentRunDecision.Approved;

        if (run.IncidentId is Guid incidentId)
        {
            var incident = await db.Incidents.FirstOrDefaultAsync(e => e.Id == incidentId, ct);

            if (incident is not null)
            {
                // Null severity means "accept the proposal"; a value means revise.
                var applied = severity ?? incident.AiSeverity;

                if (applied is IncidentSeverity value)
                {
                    incident.Severity = value;

                    // Only a substituted severity counts as a human override.
                    if (severity is not null && severity != incident.AiSeverity)
                    {
                        incident.SeverityOverriddenBy = userId;
                        incident.SeverityOverriddenAt = DateTime.UtcNow;
                        run.Decision = AgentRunDecision.Revised;
                    }
                }

                // Adopt the proposed radius so the zone matches the assessment.
                if (TryReadRadius(run.OutputJson) is int radius)
                {
                    incident.AffectedRadiusMeters = radius;
                }

                incident.UpdatedAt = DateTime.UtcNow;
            }
        }

        await SaveDecisionAsync(ct);
        await zoneService.RecomputeAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);

        // Approval is the other moment a human stands behind the risk level, so
        // it warns the district too — queued only once the approval is durable.
        // An incident that was already verified has been warned about once and
        // will not be warned about again.
        if (run.IncidentId is Guid warnedIncidentId)
        {
            notificationQueue.Enqueue(
                new NotificationJob(NotificationKind.DistrictWarning, warnedIncidentId));
        }

        logger.LogInformation(
            "Agent run {RunId} approved by {UserId}{Revised}",
            runId, userId, severity is null ? string.Empty : $" (revised to {severity})");

        return AgentRunDto.FromRun(run);
    }

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
    /// A proposal takes exactly one decision, on the newest run of its incident.
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

        if (run.IncidentId is Guid incidentId &&
            await db.AgentRuns.AnyAsync(other =>
                other.IncidentId == incidentId &&
                other.Id != run.Id &&
                other.StartedAt > run.StartedAt &&
                other.Status != AgentRunStatus.Failed, ct))
        {
            throw new InvalidOperationException(
                "A newer analysis exists for this incident; review that proposal instead.");
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
