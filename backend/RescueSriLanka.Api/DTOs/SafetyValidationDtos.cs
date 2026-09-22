using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Models.Agents;

namespace RescueSriLanka.Api.DTOs;

public enum SafetyValidationDecision
{
    APPROVE,
    REVISE,
    REJECT
}

public record AssignmentValidationContextDto(
    Guid AssignmentId,
    int PlanVersion,
    Guid RescueTeamId,
    Guid? VehicleId,
    SkillType RequiredSkill,
    int RequiredCapacity,
    AssignmentStatus AssignmentStatus,
    Guid? IncidentId,
    Guid? HelpRequestId);

public record SafetyValidationCheckDto(
    string Name,
    bool Passed,
    string Reason,
    object? Details = null);

public record SafetyValidationWorkflowResultDto(
    Guid? WorkflowId,
    Guid AssignmentId,
    int? PlanVersion,
    SafetyValidationDecision Decision,
    string Summary,
    IReadOnlyList<SafetyValidationCheckDto> Checks,
    IReadOnlyList<string> FailedChecks,
    IReadOnlyList<string> SuggestedActions,
    WorkflowStatus WorkflowStatus,
    bool IsStale);

public record GeminiSafetyToolCall(string Name, System.Text.Json.JsonElement Arguments, string? CallId);

public record GeminiSafetyAgentResponse(
    IReadOnlyList<GeminiSafetyToolCall> ToolCalls,
    SafetyValidationDecision? Decision,
    string? Summary,
    IReadOnlyList<string>? SuggestedActions,
    string? InteractionId = null);
