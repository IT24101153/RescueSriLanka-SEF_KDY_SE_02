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
using RescueSriLanka.Api.Features.ComponentD.Data;
using RescueSriLanka.Api.Services.Email;

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
    // The three steps:
    //   1. Analysis: the rule-based severity, plus an assessment in which the model calls
    //      read-only tools (nearby incidents, available teams, open requests) before it gives
    //      its verdict. The verdict is recorded; it does not set the severity.
    //   2. Logistics: the nearest available rescue team with a recorded base, from Component D's records.
    //   3. Validation: the duplicate-dispatch and terminal-status checks, and a check that any team the
    //      model suggested was actually returned by a tool. An unverified suggestion is dropped.
    //
    // Approval is the one place this planner acts. Approving a plan with a recommended team moves the
    // help request to Assigned, through B's own transition rules and history, and tells the citizen.
    // Rejecting, or approving with no team, changes nothing. Nothing is dispatched here; Component D's
    // assignment flow still owns that.
    public class PlannerAgentService(
        AppDbContext db,
        ComponentDDbContext componentD,
        IHelpRequestServiceForAgent helpRequestLookup,
        IRequestAssessmentAgent assessment,
        IActionEmailService emails) : IPlannerAgentService
    {
        private readonly AppDbContext _db = db;
        private readonly ComponentDDbContext _componentD = componentD;
        private readonly IHelpRequestServiceForAgent _helpRequestLookup = helpRequestLookup;
        private readonly IRequestAssessmentAgent _assessment = assessment;
        private readonly IActionEmailService _emails = emails;

        private const double NearbyRadiusKm = AssessmentTools.DefaultRadiusKm;

        public async Task<AgentWorkflowResponseDto> TriggerAsync(TriggerWorkflowDto dto)
        {
            if (dto.ObjectiveType != PlannerWorkflowObjectiveType.HelpRequest || dto.ObjectiveId == Guid.Empty)
                throw new ArgumentException("A valid HelpRequest objective is required.", nameof(dto));

            var requestExists = await _db.HelpRequests.AnyAsync(request => request.Id == dto.ObjectiveId);
            if (!requestExists)
                throw new KeyNotFoundException("The help request does not exist.");

            string objectiveSnapshotJson = await _helpRequestLookup.GetSnapshotJsonAsync(dto.ObjectiveType, dto.ObjectiveId);

            // A re-run must replace the plan awaiting approval, not fail beside it: the
            // failed twin would be the newest workflow, and the manager would lose the
            // plan they can still decide on.
            var replaced = await _db.AgentWorkflows
                .Where(w => w.ObjectiveType == dto.ObjectiveType && w.ObjectiveId == dto.ObjectiveId &&
                            w.Status == PlannerWorkflowStatus.AwaitingApproval)
                .ToListAsync();
            foreach (var old in replaced)
            {
                old.Status = PlannerWorkflowStatus.Superseded;
                old.UpdatedAt = DateTime.UtcNow;
                old.FinalOutcomeJson = JsonSerializer.Serialize(new
                {
                    outcome = "superseded",
                    reason = "A newer assessment of this request replaced this plan.",
                    at = DateTime.UtcNow
                });
            }

            var workflow = new AgentWorkflow
            {
                ObjectiveType = dto.ObjectiveType,
                ObjectiveId = dto.ObjectiveId,
                ObjectiveSnapshotJson = objectiveSnapshotJson,
                Status = PlannerWorkflowStatus.Planning
            };

            var steps = new List<AgentStep>
            {
                new() {
                    StepNumber = 1,
                    TargetAgent = PlannerAgentType.IncidentAnalysisAgent,
                    Action = "ClassifySeverityAndZone",
                    InputParamsJson = objectiveSnapshotJson,
                    Status = PlannerStepStatus.Pending
                },
                new() {
                    StepNumber = 2,
                    TargetAgent = PlannerAgentType.ResourceLogisticsPlanningAgent,
                    Action = "FindResourcesAndRoute",
                    InputParamsJson = objectiveSnapshotJson,
                    Status = PlannerStepStatus.Pending
                },
                new() {
                    StepNumber = 3,
                    TargetAgent = PlannerAgentType.SafetyValidationAgent,
                    Action = "ValidatePlan",
                    InputParamsJson = "{}",
                    Status = PlannerStepStatus.Pending
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

            await ExecuteStepsAsync(workflow, dto);

            await _db.SaveChangesAsync();
            return ToDto(workflow);
        }

        private async Task ExecuteStepsAsync(AgentWorkflow workflow, TriggerWorkflowDto dto)
        {
            HelpRequest? request = dto.ObjectiveType == PlannerWorkflowObjectiveType.HelpRequest
                ? await _db.HelpRequests.FindAsync(dto.ObjectiveId)
                : null;

            var step1 = workflow.Steps.First(s => s.StepNumber == 1);
            var step2 = workflow.Steps.First(s => s.StepNumber == 2);
            var step3 = workflow.Steps.First(s => s.StepNumber == 3);

            string? scoreSeverity = null;
            RequestAssessment? verdict = null;

            // ---- Step 1: Analysis — score-based severity, the model's assessment with its tool trace ----
            if (request is not null)
            {
                scoreSeverity = request.UrgencyScore >= 70 ? "High" : request.UrgencyScore >= 40 ? "Medium" : "Low";
                string zone = request.UrgencyScore >= 70 ? "Danger" : request.UrgencyScore >= 40 ? "Caution" : "Safe";

                int nearbyIncidents = await AssessmentTools.CountNearbyActiveIncidentsAsync(
                    _db, request.Latitude, request.Longitude, NearbyRadiusKm);

                try
                {
                    verdict = await _assessment.AssessAsync(request);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // The agent handles model failures itself. Anything else still must not stop the plan.
                    verdict = RequestAssessment.Unavailable($"The assessment failed: {exception.Message}", []);
                }

                step1.ToolResultJson = JsonSerializer.Serialize(new
                {
                    severity = scoreSeverity,
                    zone,
                    basedOnUrgencyScore = request.UrgencyScore,
                    nearbyActiveIncidents = nearbyIncidents,
                    nearbyRadiusKm = NearbyRadiusKm,
                    aiReasoning = verdict.Reasoning,
                    aiCredibilitySignal = verdict.Credibility,
                    aiSuggestedAction = verdict.RecommendedAction,
                    aiAnalysisAvailable = verdict.ModelAvailable && verdict.Reasoning is not null,
                    modelPriority = verdict.Priority,
                    modelUnavailableReason = verdict.UnavailableReason,
                    suggestedTeam = verdict.SuggestedTeam,
                    toolCalls = verdict.ToolCalls.Select(call => new
                    {
                        tool = call.Tool,
                        outcome = call.Outcome,
                        arguments = call.Arguments
                    })
                });
                step1.Status = PlannerStepStatus.Completed;
                step1.CompletedAt = DateTime.UtcNow;
            }
            else
            {
                step1.Status = PlannerStepStatus.Failed;
            }

            // ---- Step 2: Logistics — nearest available rescue team with a recorded base ----
            // Only a Rescue request is fulfilled by a rescue team. Medical, Water,
            // Food, Shelter and Other are the Help Request Manager's own work, and
            // they cannot dispatch a team even if one were offered — naming one
            // just drags Component D's rescue flow into a request that has
            // nothing to do with it.
            string? nearestTeamName = null;
            bool needsRescueTeam = request is { Type: HelpRequestType.Rescue };
            if (request is not null && !needsRescueTeam)
            {
                step2.ToolResultJson = JsonSerializer.Serialize(new
                {
                    found = false,
                    applicable = false,
                    reason = $"A {request.Type} request is fulfilled by the Help Request Manager, "
                        + "not by a rescue team, so no team was looked for."
                });
                step2.Status = PlannerStepStatus.Completed;
                step2.CompletedAt = DateTime.UtcNow;
            }
            else if (request is not null)
            {
                try
                {
                    var search = await AssessmentTools.SearchAvailableTeamsAsync(
                        _componentD, request.Latitude, request.Longitude, max: 1);
                    var nearest = search.Nearest.FirstOrDefault();
                    nearestTeamName = nearest?.Name;

                    object logistics = nearest is null
                        ? new
                        {
                            found = false,
                            reason = "No available rescue team has a recorded base location.",
                            teamsConsidered = search.Considered
                        }
                        : new
                        {
                            found = true,
                            nearestTeam = nearest.Name,
                            straightLineDistanceKm = nearest.DistanceKm,
                            estimatedEtaMinutes = nearest.EstimatedEtaMinutes,
                            etaBasis = $"Straight-line distance at an assumed {AssessmentTools.AssumedResponseSpeedKmh} km/h; road routing is not used.",
                            teamsConsidered = search.Considered
                        };

                    step2.ToolResultJson = JsonSerializer.Serialize(logistics);
                    step2.Status = PlannerStepStatus.Completed;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // The lookup reads another component's records. If it fails, the step says so,
                    // and the coordinator sees it before approving anything.
                    step2.ToolResultJson = JsonSerializer.Serialize(new { found = false, error = exception.Message });
                    step2.Status = PlannerStepStatus.Failed;
                }
                step2.CompletedAt = DateTime.UtcNow;
            }
            else
            {
                step2.Status = PlannerStepStatus.Failed;
            }

            // ---- Step 3: Validation — deterministic checks, and the team the plan will recommend ----
            // Prevents double-dispatch: fails if another workflow for the same objective is already approved.
            // One awaiting approval was superseded above, so it is not a duplicate.
            bool duplicateActiveWorkflow = await _db.AgentWorkflows.AnyAsync(w =>
                w.Id != workflow.Id &&
                w.ObjectiveId == workflow.ObjectiveId &&
                w.Status == PlannerWorkflowStatus.Approved);

            bool requestStillActionable = request is not null &&
                request.Status != HelpRequestStatus.Resolved &&
                request.Status != HelpRequestStatus.Cancelled;

            bool passed = !duplicateActiveWorkflow && requestStillActionable;

            // The model's suggestion counts only if a tool actually returned that team in this run.
            // And only a Rescue request may carry one at all, however confidently the
            // model volunteers a team for a food parcel.
            string? suggestedTeam = needsRescueTeam ? verdict?.SuggestedTeam : null;
            bool suggestionVerified = suggestedTeam is not null
                && verdict is not null
                && verdict.TeamsOffered.Contains(suggestedTeam, StringComparer.Ordinal);

            string? recommendedTeam = suggestionVerified ? suggestedTeam : nearestTeamName;
            string recommendedTeamSource = suggestionVerified
                ? "model, checked against the team records"
                : nearestTeamName is not null ? "nearest available team (rule)"
                : needsRescueTeam ? "none"
                : "not applicable — the Help Request Manager fulfils this type";

            step3.ValidationResultJson = JsonSerializer.Serialize(new
            {
                passed,
                duplicateActiveWorkflow,
                requestStillActionable,
                recommendedTeam,
                recommendedTeamSource,
                suggestionRejected = suggestedTeam is not null && !suggestionVerified ? suggestedTeam : null,
                modelPriority = verdict?.Priority,
                priorityAgreesWithScore = verdict?.Priority is { } priority && scoreSeverity is not null
                    ? AgreesWithScore(priority, scoreSeverity)
                    : (bool?)null
            });
            step3.Status = passed ? PlannerStepStatus.Completed : PlannerStepStatus.Failed;
            step3.CompletedAt = DateTime.UtcNow;

            if (!passed)
            {
                workflow.Status = PlannerWorkflowStatus.Failed;
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
                workflow.Status = PlannerWorkflowStatus.AwaitingApproval;
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

            if (workflow.Status != PlannerWorkflowStatus.AwaitingApproval)
                throw new InvalidOperationException("Only a validated plan awaiting approval can receive a decision.");

            var recommendedTeam = RecommendedTeamFrom(workflow);
            var request = await _db.HelpRequests.FindAsync(workflow.ObjectiveId);
            // Rescue requests need a rescue team, so approving one hands it to the
            // Rescue Coordinator in Component D instead of assigning it here.
            bool routesToRescue = request is { Type: HelpRequestType.Rescue };
            // Approving acts on any pending request — a Rescue one by handing it to
            // the Rescue Coordinator, every other type by assigning it here — so
            // verification is required either way. Keying this off "a team was
            // recommended" would let an unverified Medical or Food request through
            // now that those carry no team.
            if (dto.Approved &&
                request is { Status: HelpRequestStatus.Pending } &&
                request.VerificationStatus != VerificationStatus.Verified)
            {
                throw new InvalidOperationException(
                    "Verify the help request before approving an assignment plan.");
            }

            workflow.ApprovedByUserId = coordinatorUserId;
            workflow.ApprovalDecisionAt = DateTime.UtcNow;
            workflow.ApprovalNotes = dto.Notes;
            workflow.Status = dto.Approved ? PlannerWorkflowStatus.Approved : PlannerWorkflowStatus.Rejected;
            workflow.UpdatedAt = DateTime.UtcNow;

            if (!dto.Approved)
            {
                workflow.FinalOutcomeJson = JsonSerializer.Serialize(new
                {
                    outcome = "rejected",
                    reason = dto.Notes ?? "Rejected by coordinator",
                    at = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();
                return ToDto(workflow);
            }

            // Approval acts only when there is a team to recommend and the request is still waiting for one.
            object outcome;
            bool assigned = false;
            if (request is { Status: HelpRequestStatus.Pending } && routesToRescue)
            {
                // The request stays Pending and Verified, which is what puts it in the Rescue
                // Coordinator's queue. Component D marks it Assigned once a team is planned.
                outcome = new
                {
                    outcome = "approved",
                    helpRequestStatus = HelpRequestStatus.Pending.ToString(),
                    handedOffTo = "RescueCoordinator",
                    note = "Approved and sent to the Rescue Coordinator, who assigns the team and vehicle.",
                    approvedAt = DateTime.UtcNow
                };
            }
            else if (request is { Status: HelpRequestStatus.Pending })
            {
                // Every other type — Medical, Water, Food, Shelter, Other — is the
                // Help Request Manager's own work, so approving assigns it to them.
                // No rescue team is named: only the branch above involves one.
                _db.RequestStatusHistories.Add(HelpRequestStatusTransition.Apply(
                    request,
                    HelpRequestStatus.Assigned,
                    coordinatorUserId,
                    $"Plan approved by a coordinator. A {request.Type} request is fulfilled by the "
                    + "Help Request Manager, so no rescue team is involved."));
                assigned = true;

                outcome = new
                {
                    outcome = "approved",
                    helpRequestStatus = HelpRequestStatus.Assigned.ToString(),
                    handledBy = "HelpRequestManager",
                    approvedAt = DateTime.UtcNow
                };
            }
            else
            {
                outcome = new
                {
                    outcome = "approved",
                    helpRequestStatus = request?.Status.ToString() ?? "Missing",
                    note = "The request is no longer pending, so its status was left as it is.",
                    approvedAt = DateTime.UtcNow
                };
            }

            workflow.FinalOutcomeJson = JsonSerializer.Serialize(outcome);
            await _db.SaveChangesAsync();

            if (assigned)
            {
                // The citizen hears about the change through the same path as any other status change.
                await _emails.HelpRequestStatusChangedAsync(request!.Id, HelpRequestStatus.Assigned.ToString());
            }

            return ToDto(workflow);
        }

        private static string? RecommendedTeamFrom(AgentWorkflow workflow)
        {
            var validation = workflow.Steps.FirstOrDefault(s => s.StepNumber == 3)?.ValidationResultJson;
            if (validation is null) return null;

            using var document = JsonDocument.Parse(validation);
            return document.RootElement.TryGetProperty("recommendedTeam", out var team) && team.ValueKind == JsonValueKind.String
                ? team.GetString()
                : null;
        }

        // A critical rating is the top of the same scale as a high score, so it agrees with "High".
        private static bool AgreesWithScore(string modelPriority, string scoreSeverity)
        {
            var normalised = modelPriority.Equals("Critical", StringComparison.OrdinalIgnoreCase) ? "High" : modelPriority;
            return string.Equals(normalised, scoreSeverity, StringComparison.OrdinalIgnoreCase);
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
            Steps = [.. w.Steps
                .OrderBy(s => s.StepNumber)
                .Select(s => new PlannerAgentStepDto
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
        Task<string> GetSnapshotJsonAsync(PlannerWorkflowObjectiveType type, Guid objectiveId);
    }

    public class HelpRequestServiceForAgent(AppDbContext db) : IHelpRequestServiceForAgent
    {
        private readonly AppDbContext _db = db;

        public async Task<string> GetSnapshotJsonAsync(PlannerWorkflowObjectiveType type, Guid objectiveId)
        {
            if (type == PlannerWorkflowObjectiveType.HelpRequest)
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
