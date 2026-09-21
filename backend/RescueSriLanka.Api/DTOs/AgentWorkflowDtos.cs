using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Models.Agents;

namespace RescueSriLanka.Api.DTOs
{
    public record StartWorkflowDto(
        WorkflowObjectiveType ObjectiveType,
        Guid ObjectiveId,
        SkillType RequiredSkill);

    public record AgentStepDto(
        int StepNumber,
        AgentType TargetAgent,
        string Action,
        StepStatus Status,
        string InputParamsJson,
        string? ToolResultJson,
        string? ValidationResultJson,
        DateTime CreatedAt,
        DateTime? CompletedAt);

    public record AgentWorkflowDto(
        Guid Id,
        WorkflowObjectiveType ObjectiveType,
        Guid ObjectiveId,
        string ObjectiveSnapshotJson,
        string PlanJson,
        WorkflowStatus Status,
        Guid? ApprovedByUserId,
        DateTime? ApprovalDecisionAt,
        string? ApprovalNotes,
        string? FinalOutcomeJson,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        List<AgentStepDto> Steps);
}
