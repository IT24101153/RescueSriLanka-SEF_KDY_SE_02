using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentB.DTOs;
using RescueSriLanka.Api.Features.ComponentB.Models;
using RescueSriLanka.Api.Features.ComponentB.Services;

namespace RescueSriLanka.Api.Features.ComponentB.Agents.PlannerAgent
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
    //
    // Step execution: each step below runs REAL logic against data Student B actually owns.
    // Where a step conceptually belongs to another student's agent (A/C/D), the logic here
    // is a clearly-marked placeholder using a small reference dataset — swap for a real HTTP
    // call to that agent's endpoint once the team's tool contracts are confirmed (see the
    // Agentic AI Contract Questions document). The Safety Validation step is NOT a placeholder —
    // it's real, working deterministic validation against this project's own data.
    public class PlannerAgentService(AppDbContext db, IHelpRequestServiceForAgent helpRequestLookup, IAiAnalysisService aiAnalysis) : IPlannerAgentService
    {
        private readonly AppDbContext _db = db;
        private readonly IHelpRequestServiceForAgent _helpRequestLookup = helpRequestLookup;
        private readonly IAiAnalysisService _aiAnalysis = aiAnalysis;

        // PLACEHOLDER reference dataset for the Resource & Logistics step, standing in for
        // Student C's real Shelter/MedicalSupply table until it exists.
        private static readonly (string Name, double Lat, double Lng)[] KnownFacilities =
        [
            ("Colombo National Hospital", 6.9214, 79.8621),
            ("Kalutara District Hospital", 6.5854, 79.9607),
            ("Ratnapura General Hospital", 6.6828, 80.4012),
        ];

        public async Task<AgentWorkflowResponseDto> TriggerAsync(TriggerWorkflowDto dto)
        {
            string objectiveSnapshotJson = await _helpRequestLookup.GetSnapshotJsonAsync(dto.ObjectiveType, dto.ObjectiveId);

            var workflow = new AgentWorkflow
            {
                ObjectiveType = dto.ObjectiveType,
                ObjectiveId = dto.ObjectiveId,
                ObjectiveSnapshotJson = objectiveSnapshotJson,
                Status = WorkflowStatus.Planning
            };

            var steps = new List<AgentStep>
            {
                new() {
                    StepNumber = 1,
                    TargetAgent = AgentType.IncidentAnalysisAgent,
                    Action = "ClassifySeverityAndZone",
                    InputParamsJson = objectiveSnapshotJson,
                    Status = StepStatus.Pending
                },
                new() {
                    StepNumber = 2,
                    TargetAgent = AgentType.ResourceLogisticsPlanningAgent,
                    Action = "FindResourcesAndRoute",
                    InputParamsJson = objectiveSnapshotJson,
                    Status = StepStatus.Pending
                },
                new() {
                    StepNumber = 3,
                    TargetAgent = AgentType.SafetyValidationAgent,
                    Action = "ValidatePlan",
                    InputParamsJson = "{}",
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

            _db.AgentWorkflows.Add(workflow);
            await _db.SaveChangesAsync();

            // Execute each step with real logic (see class-level note on placeholders vs real logic).
            await ExecuteStepsAsync(workflow, dto);

            await _db.SaveChangesAsync();
            return ToDto(workflow);
        }

        private async Task ExecuteStepsAsync(AgentWorkflow workflow, TriggerWorkflowDto dto)
        {
            HelpRequest? request = dto.ObjectiveType == WorkflowObjectiveType.HelpRequest
                ? await _db.HelpRequests.FindAsync(dto.ObjectiveId)
                : null;

            var step1 = workflow.Steps.First(s => s.StepNumber == 1);
            var step2 = workflow.Steps.First(s => s.StepNumber == 2);
            var step3 = workflow.Steps.First(s => s.StepNumber == 3);

            // ---- Step 1: Incident Analysis — REAL rule-based classification + real Gemini AI reasoning ----
            if (request is not null)
            {
                string severity = request.UrgencyScore >= 70 ? "High" : request.UrgencyScore >= 40 ? "Medium" : "Low";
                string zone = request.UrgencyScore >= 70 ? "Danger" : request.UrgencyScore >= 40 ? "Caution" : "Safe";

                // Real Gemini call — adds human-readable reasoning and a credibility
                // signal on top of the deterministic score. If the AI call fails or
                // no key is configured, we still have the rule-based result above,
                // so the workflow degrades gracefully rather than breaking.
                var aiResult = await _aiAnalysis.AnalyzeHelpRequestAsync(
                    request.Type.ToString(), request.Description, request.UrgencyScore);

                step1.ToolResultJson = JsonSerializer.Serialize(new
                {
                    severity,
                    zone,
                    basedOnUrgencyScore = request.UrgencyScore,
                    aiReasoning = aiResult?.Reasoning,
                    aiCredibilitySignal = aiResult?.CredibilitySignal,
                    aiSuggestedAction = aiResult?.SuggestedAction,
                    aiAnalysisAvailable = aiResult is not null
                });
                step1.Status = StepStatus.Completed;
                step1.CompletedAt = DateTime.UtcNow;
            }
            else
            {
                step1.Status = StepStatus.Failed;
            }

            // ---- Step 2: Resource & Logistics — PLACEHOLDER dataset until Student C's Shelter table exists ----
            if (request is not null)
            {
                var nearest = KnownFacilities
                    .Select(f => new
                    {
                        f.Name,
                        DistanceKm = HaversineDistanceMeters(request.Latitude, request.Longitude, f.Lat, f.Lng) / 1000.0
                    })
                    .OrderBy(f => f.DistanceKm)
                    .First();

                double etaMinutes = (nearest.DistanceKm / 40.0) * 60.0; // assumes ~40km/h average response speed

                step2.ToolResultJson = JsonSerializer.Serialize(new
                {
                    nearestFacility = nearest.Name,
                    distanceKm = Math.Round(nearest.DistanceKm, 1),
                    estimatedEtaMinutes = Math.Round(etaMinutes, 0),
                    note = "PLACEHOLDER dataset — replace with Student C's real Shelter/Resource table once available."
                });
                step2.Status = StepStatus.Completed;
                step2.CompletedAt = DateTime.UtcNow;
            }
            else
            {
                step2.Status = StepStatus.Failed;
            }

            // ---- Step 3: Safety Validation — REAL deterministic check ----
            // Prevents double-dispatch: fails if another workflow for the same objective
            // is already awaiting approval or approved.
            bool duplicateActiveWorkflow = await _db.AgentWorkflows.AnyAsync(w =>
                w.Id != workflow.Id &&
                w.ObjectiveId == workflow.ObjectiveId &&
                (w.Status == WorkflowStatus.AwaitingApproval || w.Status == WorkflowStatus.Approved));

            bool requestStillActionable = request is not null &&
                request.Status != HelpRequestStatus.Resolved &&
                request.Status != HelpRequestStatus.Cancelled;

            bool passed = !duplicateActiveWorkflow && requestStillActionable;

            step3.ValidationResultJson = JsonSerializer.Serialize(new
            {
                passed,
                duplicateActiveWorkflow,
                requestStillActionable
            });
            step3.Status = passed ? StepStatus.Completed : StepStatus.Failed;
            step3.CompletedAt = DateTime.UtcNow;

            // Overall workflow outcome, based on real validation result.
            if (!passed)
            {
                workflow.Status = WorkflowStatus.Failed;
                workflow.FinalOutcomeJson = JsonSerializer.Serialize(new
                {
                    outcome = "failed_validation",
                    reason = duplicateActiveWorkflow
                        ? "Another active workflow already exists for this request."
                        : "The request is no longer actionable (resolved or cancelled)."
                });
            }
            else
            {
                workflow.Status = WorkflowStatus.AwaitingApproval;
            }

            workflow.UpdatedAt = DateTime.UtcNow;
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
            else
            {
                workflow.FinalOutcomeJson = JsonSerializer.Serialize(new
                {
                    outcome = "approved",
                    approvedAt = DateTime.UtcNow
                });
            }

            await _db.SaveChangesAsync();
            return ToDto(workflow);
        }

        private static double HaversineDistanceMeters(double lat1, double lon1, double lat2, double lon2)
        {
            const double earthRadiusMeters = 6371000;
            double dLat = DegreesToRadians(lat2 - lat1);
            double dLon = DegreesToRadians(lon2 - lon1);

            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return earthRadiusMeters * c;
        }

        private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

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
            Steps = [.. w.Steps
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
                })]
        };
    }

    public interface IHelpRequestServiceForAgent
    {
        Task<string> GetSnapshotJsonAsync(WorkflowObjectiveType type, Guid objectiveId);
    }

    public class HelpRequestServiceForAgent(AppDbContext db) : IHelpRequestServiceForAgent
    {
        private readonly AppDbContext _db = db;

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

            return "{}";
        }
    }
}