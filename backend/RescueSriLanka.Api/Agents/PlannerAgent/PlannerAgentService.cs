using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.DTOs;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Agents.PlannerAgent
{
    public interface IPlannerAgentService
    {
        Task<AgentWorkflowResponseDto> TriggerAsync(TriggerWorkflowDto dto);
        Task<AgentWorkflowResponseDto?> GetByIdAsync(Guid workflowId);
        Task<AgentWorkflowResponseDto?> DecideApprovalAsync(Guid workflowId, Guid coordinatorUserId, ApprovalDecisionDto dto);
    }

    // Source: Proposal Section 6 — "Receives the objective (a new incident or help request),
    // builds the structured multi-step plan, and delegates to the other three agents."
    // Owner: Student B, per the Proposal's risk mitigation note (explicitly documented here).
    public class PlannerAgentService : IPlannerAgentService
    {
        private readonly ApplicationDbContext _db;
        private readonly IHelpRequestServiceForAgent _helpRequestLookup;

        public PlannerAgentService(ApplicationDbContext db, IHelpRequestServiceForAgent helpRequestLookup)
        {
            _db = db;
            _helpRequestLookup = helpRequestLookup;
        }

        public async Task<AgentWorkflowResponseDto> TriggerAsync(TriggerWorkflowDto dto)
        {
            // Build a snapshot of the objective so the workflow is self-contained
            // even if the underlying HelpRequest changes later.
            string objectiveSnapshotJson = await _helpRequestLookup.GetSnapshotJsonAsync(dto.ObjectiveType, dto.ObjectiveId);

            var workflow = new AgentWorkflow
            {
                ObjectiveType = dto.ObjectiveType,
                ObjectiveId = dto.ObjectiveId,
                ObjectiveSnapshotJson = objectiveSnapshotJson,
                Status = WorkflowStatus.Planning
            };

            // Build the structured multi-step plan.
            // Source: Proposal Section 6 — "each step is delegated to the responsible agent;
            // agents call only allow-listed tools with validated inputs and structured outputs."
            //
            // ASSUMPTION (not specified by documents): the exact 4-step sequence below.
            // This should be confirmed with the team once A, C, D's real tool contracts exist —
            // for now, Step 1/2/3 call PLACEHOLDER tool endpoints that don't exist yet.
            var steps = new List<AgentStep>
            {
                new AgentStep
                {
                    StepNumber = 1,
                    TargetAgent = AgentType.IncidentAnalysisAgent,
                    Action = "ClassifySeverityAndZone",
                    InputParamsJson = objectiveSnapshotJson,
                    Status = StepStatus.Pending
                },
                new AgentStep
                {
                    StepNumber = 2,
                    TargetAgent = AgentType.ResourceLogisticsPlanningAgent,
                    Action = "FindResourcesAndRoute",
                    InputParamsJson = objectiveSnapshotJson,
                    Status = StepStatus.Pending
                },
                new AgentStep
                {
                    StepNumber = 3,
                    TargetAgent = AgentType.SafetyValidationAgent,
                    Action = "ValidatePlan",
                    InputParamsJson = "{}", // filled in once steps 1-2 have real results
                    Status = StepStatus.Pending
                }
            };

            workflow.Steps = steps;

            var planSummary = new
            {
                objective = new { type = dto.ObjectiveType.ToString(), id = dto.ObjectiveId },
                stepCount = steps.Count,
                sequence = steps.Select(s => new { s.StepNumber, agent = s.TargetAgent.ToString(), s.Action })
            };
            workflow.PlanJson = JsonSerializer.Serialize(planSummary);

            // NOTE: real delegation (actually calling the other 3 agents) is not implemented yet —
            // that requires their tool contracts to exist first. For now this persists the plan
            // and marks it awaiting approval, matching the "auditable, safe" requirement even
            // in a partial state.
            workflow.Status = WorkflowStatus.AwaitingApproval;

            _db.AgentWorkflows.Add(workflow);
            await _db.SaveChangesAsync();

            return ToDto(workflow);
        }

        public async Task<AgentWorkflowResponseDto?> GetByIdAsync(Guid workflowId)
        {
            var workflow = await _db.AgentWorkflows
                .Include(w => w.Steps)
                .FirstOrDefaultAsync(w => w.Id == workflowId);

            return workflow is null ? null : ToDto(workflow);
        }

        public async Task<AgentWorkflowResponseDto?> DecideApprovalAsync(Guid workflowId, Guid coordinatorUserId, ApprovalDecisionDto dto)
        {
            var workflow = await _db.AgentWorkflows
                .Include(w => w.Steps)
                .FirstOrDefaultAsync(w => w.Id == workflowId);

            if (workflow is null) return null;

            workflow.ApprovedByUserId = coordinatorUserId;
            workflow.ApprovalDecisionAt = DateTime.UtcNow;
            workflow.ApprovalNotes = dto.Notes;
            workflow.Status = dto.Approved ? WorkflowStatus.Approved : WorkflowStatus.Rejected;
            workflow.UpdatedAt = DateTime.UtcNow;

            if (!dto.Approved)
            {
                workflow.FinalOutcomeJson = JsonSerializer.Serialize(new
                {
                    outcome = "rejected",
                    reason = dto.Notes ?? "Rejected by coordinator",
                    at = DateTime.UtcNow
                });
            }

            await _db.SaveChangesAsync();
            return ToDto(workflow);
        }

        private static AgentWorkflowResponseDto ToDto(AgentWorkflow w) => new()
        {
            Id = w.Id,
            ObjectiveType = w.ObjectiveType,
            ObjectiveId = w.ObjectiveId,
            PlanJson = w.PlanJson,
            Status = w.Status,
            ApprovalNotes = w.ApprovalNotes,
            FinalOutcomeJson = w.FinalOutcomeJson,
            CreatedAt = w.CreatedAt,
            Steps = w.Steps
                .OrderBy(s => s.StepNumber)
                .Select(s => new AgentStepDto
                {
                    Id = s.Id,
                    StepNumber = s.StepNumber,
                    TargetAgent = s.TargetAgent,
                    Action = s.Action,
                    InputParamsJson = s.InputParamsJson,
                    ToolResultJson = s.ToolResultJson,
                    ValidationResultJson = s.ValidationResultJson,
                    Status = s.Status
                }).ToList()
        };
    }

    // Small helper interface so the Planner Agent doesn't depend directly on
    // HelpRequestService's full surface — just needs a snapshot for now.
    public interface IHelpRequestServiceForAgent
    {
        Task<string> GetSnapshotJsonAsync(WorkflowObjectiveType type, Guid objectiveId);
    }

    public class HelpRequestServiceForAgent : IHelpRequestServiceForAgent
    {
        private readonly ApplicationDbContext _db;

        public HelpRequestServiceForAgent(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<string> GetSnapshotJsonAsync(WorkflowObjectiveType type, Guid objectiveId)
        {
            if (type == WorkflowObjectiveType.HelpRequest)
            {
                var hr = await _db.HelpRequests.FindAsync(objectiveId);
                if (hr is null) return "{}";

                return JsonSerializer.Serialize(new
                {
                    hr.Id,
                    Type = hr.Type.ToString(),
                    hr.Description,
                    hr.Latitude,
                    hr.Longitude,
                    hr.UrgencyScore,
                    Status = hr.Status.ToString()
                });
            }

            // Incident snapshot: not available yet — Student A's Incident entity
            // doesn't exist in this project yet. Placeholder for now.
            return "{}";
        }
    }
}