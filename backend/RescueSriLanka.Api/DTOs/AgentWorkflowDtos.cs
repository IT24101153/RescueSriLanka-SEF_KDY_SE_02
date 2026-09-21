using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Models.Agents;

namespace RescueSriLanka.Api.DTOs
{
    public record StartWorkflowDto(
        WorkflowObjectiveType ObjectiveType,
        Guid ObjectiveId,
        SkillType RequiredSkill);

    public record AgentStepDto(
        int StepOrder,
        string AgentName,
        AgentStepStatus Status,
        string? InputJson,
        string? OutputJson,
        string? ErrorMessage,
        DateTime? StartedAt,
        DateTime? CompletedAt);

    public record AgentWorkflowDto(
        Guid Id,
        WorkflowObjectiveType ObjectiveType,
        Guid ObjectiveId,
        SkillType RequiredSkill,
        WorkflowStatus Status,
        Guid? DispatchId,
        string? FinalOutcome,
        DateTime CreatedAt,
        DateTime? CompletedAt,
        List<AgentStepDto> Steps);
}
