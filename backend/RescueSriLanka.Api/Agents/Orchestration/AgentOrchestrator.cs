// Updated: Step 2 now calls the real OllamaDispatchRecommendationAgent
// (an LLM in a bounded tool-calling loop) instead of calling
// ITeamMatchingService directly. The matching service is still used —
// it's now one of the agent's allow-listed tools (see
// DispatchAgentTools.search_teams) rather than something the
// orchestrator calls on the agent's behalf. SafetyValidationAgent
// remains a deterministic guardrail positioned AFTER the LLM agent's
// output, per Lecture 07's guardrail model (slide 30).

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Agents.SafetyValidation;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.DTOs;
using RescueSriLanka.Api.Models.Agents;
using RescueSriLanka.Api.Services;

namespace RescueSriLanka.Api.Agents.Orchestration
{
    public interface IAgentOrchestrator
    {
        Task<AgentWorkflowDto> StartWorkflowAsync(StartWorkflowDto dto);
        Task<AgentWorkflowDto?> GetWorkflowAsync(Guid workflowId);
    }

    public class AgentOrchestrator : IAgentOrchestrator
    {
        private readonly ApplicationDbContext _db;
        private readonly IIncidentAnalysisAgent _incidentAnalysisAgent;
        private readonly IDispatchRecommendationAgent _recommendationAgent;
        private readonly IAssignmentService _assignmentService;
        private readonly IDispatchService _dispatchService;
        private readonly ISafetyValidationAgent _safetyAgent;

        public AgentOrchestrator(
            ApplicationDbContext db,
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
                RequiredSkill = dto.RequiredSkill,
                Status = WorkflowStatus.InProgress
            };
            _db.AgentWorkflows.Add(workflow);
            await _db.SaveChangesAsync();

            // --- Step 1: Incident Analysis (Student A's future agent) ---
            var step1 = await RunStepAsync(workflow.Id, 1, "IncidentAnalysis", async () =>
            {
                var result = await _incidentAnalysisAgent.AnalyzeAsync(dto.ObjectiveId);
                return JsonSerializer.Serialize(result);
            });
            if (step1.Status == AgentStepStatus.Failed)
                return await FailWorkflowAsync(workflow, "Incident analysis step failed.");

            // --- Step 2: Dispatch Recommendation — the real LLM agent.
            //     Its tool-call log (think/act/observe trace) is persisted
            //     in full as this step's structured output. ---
            DispatchRecommendationResult? recommendation = null;
            var step2 = await RunStepAsync(workflow.Id, 2, "DispatchRecommendation", async () =>
            {
                recommendation = await _recommendationAgent.RecommendAsync(dto.RequiredSkill.ToString());
                return JsonSerializer.Serialize(recommendation);
            });

            if (step2.Status == AgentStepStatus.Failed || recommendation is null || !recommendation.Success)
            {
                var reason = recommendation?.Reasoning ?? "Dispatch recommendation agent failed.";
                return await FailWorkflowAsync(workflow, reason);
            }

            var assignment = await _assignmentService.CreateAsync(new CreateAssignmentDto(
                IncidentId: dto.ObjectiveType == WorkflowObjectiveType.NewIncident ? dto.ObjectiveId : null,
                HelpRequestId: dto.ObjectiveType == WorkflowObjectiveType.NewHelpRequest ? dto.ObjectiveId : null,
                RescueTeamId: recommendation.RescueTeamId!.Value,
                RequiredSkill: dto.RequiredSkill,
                Notes: $"Agent recommendation: {recommendation.Reasoning}"));

            if (assignment is null)
                return await FailWorkflowAsync(workflow, "Failed to create assignment for the recommended team.");

            // --- Step 3: Safety Validation — deterministic GUARDRAIL on
            //     the LLM agent's recommendation, not itself an agent. ---
            var step3 = await RunStepAsync(workflow.Id, 3, "SafetyValidationGuardrail", async () =>
            {
                var validation = await _safetyAgent.ValidateAsync(assignment.Id);
                return JsonSerializer.Serialize(validation);
            });

            var lastStepOutput = await _db.AgentSteps
                .Where(s => s.AgentWorkflowId == workflow.Id && s.AgentName == "SafetyValidationGuardrail")
                .Select(s => s.OutputJson)
                .FirstOrDefaultAsync();

            var validationPassed = lastStepOutput is not null
                && JsonSerializer.Deserialize<SafetyValidationResultDto>(lastStepOutput)?.Passed == true;

            if (!validationPassed)
                return await FailWorkflowAsync(workflow, "Safety guardrail rejected the agent's recommendation — see step 3 output.");

            // --- Human approval gate: create the dispatch, but it stays
            //     Pending until a coordinator approves via the existing
            //     endpoint. ---
            var (dispatch, _, dispatchError) = await _dispatchService.CreateAsync(
                new CreateDispatchDto(assignment.Id, "Created by AgentOrchestrator"));

            if (dispatch is null)
                return await FailWorkflowAsync(workflow, dispatchError ?? "Failed to create dispatch.");

            workflow.Status = WorkflowStatus.AwaitingApproval;
            workflow.DispatchId = dispatch.Id;
            await _db.SaveChangesAsync();

            return await ToDtoAsync(workflow.Id);
        }

        public async Task<AgentWorkflowDto?> GetWorkflowAsync(Guid workflowId)
        {
            var exists = await _db.AgentWorkflows.AnyAsync(w => w.Id == workflowId);
            return exists ? await ToDtoAsync(workflowId) : null;
        }

        private async Task<AgentStep> RunStepAsync(Guid workflowId, int order, string agentName, Func<Task<string>> run)
        {
            var step = new AgentStep
            {
                AgentWorkflowId = workflowId,
                StepOrder = order,
                AgentName = agentName,
                Status = AgentStepStatus.Running,
                StartedAt = DateTime.UtcNow
            };
            _db.AgentSteps.Add(step);
            await _db.SaveChangesAsync();

            try
            {
                step.OutputJson = await run();
                step.Status = AgentStepStatus.Succeeded;
            }
            catch (Exception ex)
            {
                step.Status = AgentStepStatus.Failed;
                step.ErrorMessage = ex.Message;
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
            workflow.FinalOutcome = reason;
            workflow.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return await ToDtoAsync(workflow.Id);
        }

        private async Task<AgentWorkflowDto> ToDtoAsync(Guid workflowId)
        {
            var workflow = await _db.AgentWorkflows
                .Include(w => w.Steps)
                .FirstAsync(w => w.Id == workflowId);

            return new AgentWorkflowDto(
                workflow.Id, workflow.ObjectiveType, workflow.ObjectiveId, workflow.RequiredSkill,
                workflow.Status, workflow.DispatchId, workflow.FinalOutcome, workflow.CreatedAt, workflow.CompletedAt,
                workflow.Steps.OrderBy(s => s.StepOrder).Select(s => new AgentStepDto(
                    s.StepOrder, s.AgentName, s.Status, s.InputJson, s.OutputJson,
                    s.ErrorMessage, s.StartedAt, s.CompletedAt)).ToList());
        }
    }
}
