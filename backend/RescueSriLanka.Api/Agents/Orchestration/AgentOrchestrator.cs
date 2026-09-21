using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Agents.SafetyValidation;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.DTOs;
using RescueSriLanka.Api.Models.Agents;
using RescueSriLanka.Api.Services;

namespace RescueSriLanka.Api.Agents.Orchestration;

public interface IAgentOrchestrator
{
    Task<AgentWorkflowDto> StartWorkflowAsync(StartWorkflowDto dto);
    Task<AgentWorkflowDto?> GetWorkflowAsync(Guid workflowId);
}

public class AgentOrchestrator : IAgentOrchestrator
{
    private readonly ComponentDDbContext _db;
    private readonly IIncidentAnalysisAgent _incidentAnalysisAgent;
    private readonly IDispatchRecommendationAgent _recommendationAgent;
    private readonly IAssignmentService _assignmentService;
    private readonly IDispatchService _dispatchService;
    private readonly ISafetyValidationAgent _safetyAgent;

    public AgentOrchestrator(
        ComponentDDbContext db,
        IIncidentAnalysisAgent incidentAnalysisAgent,
        IDispatchRecommendationAgent recommendationAgent,
        IAssignmentService assignmentService,
        IDispatchService dispatchService,
        ISafetyValidationAgent safetyAgent)
    {
        _db = db;
        _incidentAnalysisAgent = incidentAnalysisAgent;
        _recommendationAgent = recommendationAgent;
        _assignmentService = assignmentService;
        _dispatchService = dispatchService;
        _safetyAgent = safetyAgent;
    }

    public async Task<AgentWorkflowDto> StartWorkflowAsync(StartWorkflowDto dto)
    {
        var workflow = new AgentWorkflow
        {
            ObjectiveType = dto.ObjectiveType,
            ObjectiveId = dto.ObjectiveId,
            ObjectiveSnapshotJson = JsonSerializer.Serialize(new
            {
                requiredSkill = dto.RequiredSkill.ToString()
            }),
            PlanJson = JsonSerializer.Serialize(new
            {
                requiredSkill = dto.RequiredSkill.ToString(),
                stages = new[] { "IncidentAnalysis", "DispatchRecommendation", "SafetyValidation" }
            }),
            Status = WorkflowStatus.Executing
        };
        _db.AgentWorkflows.Add(workflow);
        await _db.SaveChangesAsync();

        var step1 = await RunStepAsync(
            workflow.Id,
            1,
            AgentType.IncidentAnalysisAgent,
            "Analyze incident",
            JsonSerializer.Serialize(new { objectiveId = dto.ObjectiveId }),
            false,
            async () => JsonSerializer.Serialize(await _incidentAnalysisAgent.AnalyzeAsync(dto.ObjectiveId)));

        if (step1.Status == StepStatus.Failed)
            return await FailWorkflowAsync(workflow, "Incident analysis step failed.");

        DispatchRecommendationResult? recommendation = null;
        var step2 = await RunStepAsync(
            workflow.Id,
            2,
            AgentType.ResourceLogisticsPlanningAgent,
            "Recommend dispatch team",
            JsonSerializer.Serialize(new { requiredSkill = dto.RequiredSkill.ToString() }),
            false,
            async () =>
            {
                recommendation = await _recommendationAgent.RecommendAsync(dto.RequiredSkill.ToString());
                return JsonSerializer.Serialize(recommendation);
            });

        if (step2.Status == StepStatus.Failed || recommendation is null || !recommendation.Success)
        {
            var reason = recommendation?.Reasoning ?? "Dispatch recommendation agent failed.";
            return await FailWorkflowAsync(workflow, reason);
        }

        var assignment = await _assignmentService.CreateAsync(new CreateAssignmentDto(
            IncidentId: dto.ObjectiveType == WorkflowObjectiveType.Incident ? dto.ObjectiveId : null,
            HelpRequestId: dto.ObjectiveType == WorkflowObjectiveType.HelpRequest ? dto.ObjectiveId : null,
            RescueTeamId: recommendation.RescueTeamId!.Value,
            RequiredSkill: dto.RequiredSkill,
            Notes: $"Agent recommendation: {recommendation.Reasoning}"));

        if (assignment is null)
            return await FailWorkflowAsync(workflow, "Failed to create assignment for the recommended team.");

        workflow.PlanJson = JsonSerializer.Serialize(new
        {
            requiredSkill = dto.RequiredSkill.ToString(),
            recommendedTeamId = recommendation.RescueTeamId,
            assignmentId = assignment.Id
        });
        workflow.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var step3 = await RunStepAsync(
            workflow.Id,
            3,
            AgentType.SafetyValidationAgent,
            "Validate dispatch safety",
            JsonSerializer.Serialize(new { assignmentId = assignment.Id }),
            true,
            async () => JsonSerializer.Serialize(await _safetyAgent.ValidateAsync(assignment.Id)));

        var validationPassed = step3.Status != StepStatus.Failed
            && step3.ValidationResultJson is not null
            && JsonSerializer.Deserialize<SafetyValidationResultDto>(step3.ValidationResultJson)?.Passed == true;

        if (!validationPassed)
            return await FailWorkflowAsync(workflow, "Safety guardrail rejected the agent recommendation.");

        var (dispatch, _, dispatchError) = await _dispatchService.CreateAsync(
            new CreateDispatchDto(assignment.Id, "Created by AgentOrchestrator"));

        if (dispatch is null)
            return await FailWorkflowAsync(workflow, dispatchError ?? "Failed to create dispatch.");

        workflow.PlanJson = JsonSerializer.Serialize(new
        {
            requiredSkill = dto.RequiredSkill.ToString(),
            recommendedTeamId = recommendation.RescueTeamId,
            assignmentId = assignment.Id,
            dispatchId = dispatch.Id
        });
        workflow.Status = WorkflowStatus.AwaitingApproval;
        workflow.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return await ToDtoAsync(workflow.Id);
    }

    public async Task<AgentWorkflowDto?> GetWorkflowAsync(Guid workflowId)
    {
        var exists = await _db.AgentWorkflows.AnyAsync(w => w.Id == workflowId);
        return exists ? await ToDtoAsync(workflowId) : null;
    }

    private async Task<AgentStep> RunStepAsync(
        Guid workflowId,
        int stepNumber,
        AgentType targetAgent,
        string action,
        string inputParamsJson,
        bool persistAsValidationResult,
        Func<Task<string>> run)
    {
        var step = new AgentStep
        {
            AgentWorkflowId = workflowId,
            StepNumber = stepNumber,
            TargetAgent = targetAgent,
            Action = action,
            InputParamsJson = inputParamsJson,
            Status = StepStatus.Running
        };
        _db.AgentSteps.Add(step);
        await _db.SaveChangesAsync();

        try
        {
            var result = await run();
            if (persistAsValidationResult)
                step.ValidationResultJson = result;
            else
                step.ToolResultJson = result;
            step.Status = StepStatus.Completed;
        }
        catch (Exception ex)
        {
            var error = JsonSerializer.Serialize(new { error = ex.Message });
            if (persistAsValidationResult)
                step.ValidationResultJson = error;
            else
                step.ToolResultJson = error;
            step.Status = StepStatus.Failed;
        }
        finally
        {
            step.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return step;
    }

    private async Task<AgentWorkflowDto> FailWorkflowAsync(AgentWorkflow workflow, string reason)
    {
        workflow.Status = WorkflowStatus.Failed;
        workflow.FinalOutcomeJson = JsonSerializer.Serialize(new { reason });
        workflow.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return await ToDtoAsync(workflow.Id);
    }

    private async Task<AgentWorkflowDto> ToDtoAsync(Guid workflowId)
    {
        var workflow = await _db.AgentWorkflows
            .Include(w => w.Steps)
            .FirstAsync(w => w.Id == workflowId);

        return new AgentWorkflowDto(
            workflow.Id,
            workflow.ObjectiveType,
            workflow.ObjectiveId,
            workflow.ObjectiveSnapshotJson,
            workflow.PlanJson,
            workflow.Status,
            workflow.ApprovedByUserId,
            workflow.ApprovalDecisionAt,
            workflow.ApprovalNotes,
            workflow.FinalOutcomeJson,
            workflow.CreatedAt,
            workflow.UpdatedAt,
            workflow.Steps.OrderBy(s => s.StepNumber).Select(s => new AgentStepDto(
                s.StepNumber,
                s.TargetAgent,
                s.Action,
                s.Status,
                s.InputParamsJson,
                s.ToolResultJson,
                s.ValidationResultJson,
                s.CreatedAt,
                s.CompletedAt)).ToList());
    }
}
