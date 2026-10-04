using System.ComponentModel.DataAnnotations;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Features.ComponentA.Models;

namespace RescueSriLanka.Api.Features.ComponentA.DTOs;
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
    public required string Decision { get; init; }
    public string? DecisionNote { get; init; }
    public required int ModelAttempts { get; init; }
    public string? ErrorMessage { get; init; }
    public required int DurationMs { get; init; }
    public required DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? PlanJson { get; init; }
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
        Decision = run.Decision.ToString(),
        DecisionNote = run.DecisionNote,
        ModelAttempts = run.ModelAttempts,
        ErrorMessage = run.ErrorMessage,
        DurationMs = run.DurationMs,
        StartedAt = run.StartedAt,
        CompletedAt = run.CompletedAt,
        PlanJson = run.PlanJson,
        ToolCallsJson = run.ToolCallsJson,
        OutputJson = run.OutputJson
    };
}

/// <summary>
/// The coordinator's approval. Which fields apply depends on the agent that
/// made the run; leaving every optional field null means "accept the proposal
/// as-is", and supplying one revises it.
/// </summary>
public record ApproveAgentRunRequest
{
    /// <summary>Incident Analysis Agent: substitute this severity for the proposed one.</summary>
    public IncidentSeverity? Severity { get; init; }

    [MaxLength(500)]
    public string? Note { get; init; }

    /// <summary>Enrichment Agent: the suggested fields to apply. Null applies them all.</summary>
    [MaxLength(10)]
    public IReadOnlyList<string>? Fields { get; init; }

    /// <summary>Enrichment Agent: whether to merge into the proposed duplicate. Null follows the proposal.</summary>
    public bool? MergeDuplicate { get; init; }

    /// <summary>
    /// Zone Planning Agent: the zones to create, as the coordinator edited them.
    /// Null creates the proposed zones unchanged; an empty list creates none.
    /// </summary>
    [MaxLength(20)]
    public IReadOnlyList<SafetyZoneRequest>? Zones { get; init; }

    /// <summary>Zone Planning Agent: the proposed retirements to carry out. Null carries out all.</summary>
    [MaxLength(20)]
    public IReadOnlyList<Guid>? RetireZoneIds { get; init; }
}

public record RejectAgentRunRequest
{
    [Required, MaxLength(500)]
    public required string Reason { get; init; }
}
