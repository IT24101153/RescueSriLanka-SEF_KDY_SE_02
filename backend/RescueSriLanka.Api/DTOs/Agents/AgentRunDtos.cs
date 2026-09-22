using System.ComponentModel.DataAnnotations;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.DTOs.Agents;

/// <summary>Execution summary — one row in the agent monitoring view.</summary>
public record AgentRunDto
{
    public required Guid Id { get; init; }
    public required string AgentName { get; init; }
    public required string Objective { get; init; }
    public Guid? IncidentId { get; init; }
    public required string Status { get; init; }
    public string? Model { get; init; }
    public required bool UsedFallback { get; init; }
    public required bool Approved { get; init; }
    public DateTime? ApprovedAt { get; init; }
    public string? ErrorMessage { get; init; }
    public required int DurationMs { get; init; }
    public required DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? ToolCallsJson { get; init; }
    public string? OutputJson { get; init; }

    public static AgentRunDto FromRun(AgentRun run) => new()
    {
        Id = run.Id,
        AgentName = run.AgentName,
        Objective = run.Objective,
        IncidentId = run.IncidentId,
        Status = run.Status.ToString(),
        Model = run.Model,
        UsedFallback = run.UsedFallback,
        Approved = run.Approved,
        ApprovedAt = run.ApprovedAt,
        ErrorMessage = run.ErrorMessage,
        DurationMs = run.DurationMs,
        StartedAt = run.StartedAt,
        CompletedAt = run.CompletedAt,
        ToolCallsJson = run.ToolCallsJson,
        OutputJson = run.OutputJson
    };
}

/// <summary>
/// Revise: the coordinator accepts the run but substitutes their own severity.
/// Leaving Severity null means "accept the proposal as-is".
/// </summary>
public record ApproveAgentRunRequest
{
    public IncidentSeverity? Severity { get; init; }

    [MaxLength(500)]
    public string? Note { get; init; }
}

public record RejectAgentRunRequest
{
    [Required, MaxLength(500)]
    public required string Reason { get; init; }
}
