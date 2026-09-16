using System.ComponentModel.DataAnnotations;

namespace RescueSriLanka.Api.Models;

public enum AgentRunStatus
{
    Running,
    Succeeded,
    /// <summary>Model unusable; the deterministic rule engine produced the result.</summary>
    SucceededWithFallback,
    /// <summary>A safe, clearly recorded failure — nothing was applied.</summary>
    Failed
}

/// <summary>
/// One execution of one agent, persisted for audit. Required by the spec:
/// workflow state must be stored and the outcome must be either an auditable
/// success or a safe, clearly recorded failure.
///
/// SHARED across components — Student B's planner writes here too. Agree any
/// schema change with the group before migrating.
/// </summary>
public class AgentRun
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(80)]
    public required string AgentName { get; set; }

    /// <summary>What the agent was asked to achieve.</summary>
    [MaxLength(500)]
    public required string Objective { get; set; }

    public Guid? IncidentId { get; set; }

    public AgentRunStatus Status { get; set; } = AgentRunStatus.Running;

    /// <summary>Model identifier, or "rule-engine" when the fallback ran.</summary>
    [MaxLength(120)]
    public string? Model { get; set; }

    /// <summary>Validated input handed to the agent.</summary>
    public string? InputJson { get; set; }

    /// <summary>Allow-listed tool calls and their results.</summary>
    public string? ToolCallsJson { get; set; }

    /// <summary>The structured output, after validation.</summary>
    public string? OutputJson { get; set; }

    /// <summary>Why a run failed or fell back — never silently swallowed.</summary>
    [MaxLength(1000)]
    public string? ErrorMessage { get; set; }

    public bool UsedFallback { get; set; }

    /// <summary>False until a coordinator accepts the proposal.</summary>
    public bool Approved { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }

    public int DurationMs { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
