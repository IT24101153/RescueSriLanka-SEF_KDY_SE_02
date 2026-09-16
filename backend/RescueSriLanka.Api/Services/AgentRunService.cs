using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.DTOs.Agents;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Services;

public interface IAgentRunService
{
    Task<IReadOnlyList<AgentRunDto>> QueryAsync(Guid? incidentId, int take, CancellationToken ct = default);
    Task<AgentRunDto?> ApproveAsync(Guid runId, IncidentSeverity? severity, Guid userId, CancellationToken ct = default);
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

        return runs.Select(AgentRunDto.FromRun).ToList();
    }

    public async Task<AgentRunDto?> ApproveAsync(
        Guid runId, IncidentSeverity? severity, Guid userId, CancellationToken ct = default)
    {
        var run = await db.AgentRuns.FirstOrDefaultAsync(entity => entity.Id == runId, ct);
        if (run is null) return null;

        run.Approved = true;
        run.ApprovedByUserId = userId;
        run.ApprovedAt = DateTime.UtcNow;

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

        await db.SaveChangesAsync(ct);
        await zoneService.RecomputeAsync(ct);

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

        // Rejection leaves the incident untouched and records why.
        run.Approved = false;
        run.ApprovedByUserId = userId;
        run.ApprovedAt = DateTime.UtcNow;
        run.ErrorMessage = $"Rejected by coordinator: {reason}";

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Agent run {RunId} rejected by {UserId}", runId, userId);

        return AgentRunDto.FromRun(run);
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
