using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentA.Models;
using RescueSriLanka.Api.Features.ComponentD.Agents.SafetyValidation;
using RescueSriLanka.Api.Features.ComponentD.Data;
using RescueSriLanka.Api.Features.ComponentD.DTOs;
using RescueSriLanka.Api.Features.ComponentD.Models;
using RescueSriLanka.Api.Features.ComponentD.Services;

namespace RescueSriLanka.Api.Tests;

public class ComponentDSafetyAgentGoldenTests
{
    private static readonly string[] Mandatory =
    [
        "ASSIGNMENT_EXISTS", "PLAN_VERSION_MATCHES", "TEAM_AVAILABLE", "REQUIRED_SKILL_PRESENT",
        "TEAM_CONFLICT", "VEHICLE_EXISTS", "VEHICLE_OWNERSHIP", "VEHICLE_AVAILABLE",
        "VEHICLE_CAPACITY", "VEHICLE_CONFLICT"
    ];

    [Theory]
    [InlineData("G01", "Fully safe assignment")]
    [InlineData("G02", "Team unavailable")]
    [InlineData("G03", "Required skill missing")]
    [InlineData("G04", "Vehicle unavailable")]
    [InlineData("G05", "Insufficient capacity")]
    [InlineData("G06", "Team conflict")]
    [InlineData("G07", "Vehicle conflict")]
    [InlineData("G08", "Plan changes during validation")]
    [InlineData("G09", "Provider unavailable")]
    [InlineData("G10", "Malformed provider content")]
    [InlineData("G11", "Unknown tool")]
    [InlineData("G12", "Tool loop exhaustion")]
    [InlineData("G13", "Tool execution is not dispatch authority")]
    [InlineData("G14", "Human approval requires live revalidation")]
    [InlineData("G15", "Explicit provider rejection")]
    public async Task GoldenCase(string caseId, string scenario)
    {
        Assert.False(string.IsNullOrWhiteSpace(scenario));
        using var db = TestDbFactory.Create();
        using var shared = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var incident = new Incident
        {
            Id = Guid.Parse("d1000000-0000-4000-8000-000000000001"),
            Title = "Golden fixture incident", Description = "Isolated fictional reference"
        };
        shared.Incidents.Add(incident);
        await shared.SaveChangesAsync();
        var team = new RescueTeam { Id = Guid.Parse("d1000000-0000-4000-8000-000000000002"), Name = "Golden team" };
        var member = new TeamMember { Id = Guid.Parse("d1000000-0000-4000-8000-000000000003"),
            RescueTeamId = team.Id, FullName = "Golden medic", Phone = "0712345678", Skill = SkillType.FirstAid };
        var vehicle = new Vehicle { Id = Guid.Parse("d1000000-0000-4000-8000-000000000004"),
            RescueTeamId = team.Id, PlateNumber = "GOLDEN-01", Type = VehicleType.Ambulance, Capacity = 4 };
        db.AddRange(team, member, vehicle);
        await db.SaveChangesAsync();
        // Start with a proposal accepted by the real service and an existing isolated incident.
        var (created, error) = await new AssignmentService(db, new IncidentReadService(shared), new HelpRequestCandidateService(db, new HelpRequestReadService(shared)), new RescueSriLanka.Api.Features.ComponentB.Services.HelpRequestResponseStatusService(shared)).CreateAsync(
            new(incident.Id, null, team.Id, vehicle.Id, SkillType.FirstAid, 2, null));
        Assert.Null(error);
        Assert.NotNull(created);
        var assignment = await db.Assignments.SingleAsync();
        var initialVersion = assignment.PlanVersion;

        switch (caseId)
        {
            case "G02": team.Status = TeamStatus.OffDuty; break;
            case "G03": member.IsAvailable = false; break;
            case "G04": vehicle.Status = VehicleStatus.UnderMaintenance; break;
            case "G05": vehicle.Capacity = 1; break;
            case "G06":
                var otherVehicle = new Vehicle { RescueTeamId = team.Id, PlateNumber = "GOLDEN-02", Capacity = 4 };
                db.Add(otherVehicle);
                db.Add(new Assignment { IncidentId = incident.Id, RescueTeamId = team.Id,
                    VehicleId = otherVehicle.Id, RequiredSkill = SkillType.FirstAid, RequiredCapacity = 2 });
                break;
            case "G07":
                var otherTeam = new RescueTeam { Name = "Competing commitment" };
                db.Add(otherTeam);
                // Deliberately conflicting commitment; isolate VEHICLE_CONFLICT from TEAM_CONFLICT.
                db.Add(new Assignment { IncidentId = incident.Id, RescueTeamId = otherTeam.Id,
                    VehicleId = vehicle.Id, RequiredSkill = SkillType.FirstAid, RequiredCapacity = 2 });
                break;
        }
        await db.SaveChangesAsync();
        var before = await OperationalSnapshot(db);

        var provider = new ScriptedProvider(async (call, context, results, previousId) =>
        {
            switch (caseId)
            {
                case "G08":
                    assignment.PlanVersion++;
                    await db.SaveChangesAsync();
                    break;
                case "G09": throw new HttpRequestException("Controlled offline provider");
                case "G10": return GeminiSafetyValidationClient.Parse("{ invalid-json");
                case "G11": return Tool("arbitrary_sql", context, "unknown-call", "unknown-interaction");
                case "G12": return Tool("check_team_availability", context, $"call-{call}", $"interaction-{call}");
                case "G13":
                    if (call == 1) return Tool("check_team_availability", context, "read-only-call", "read-only-interaction");
                    Assert.Equal("read-only-interaction", previousId);
                    var toolResult = JsonSerializer.SerializeToElement(Assert.Single(results));
                    Assert.Equal("read-only-call", toolResult.GetProperty("call_id").GetString());
                    var check = toolResult.GetProperty("result").Deserialize<SafetyValidationCheckDto>()!;
                    Assert.Equal("TEAM_AVAILABLE", check.Name);
                    Assert.True(check.Passed);
                    break;
                case "G15": return new([], SafetyValidationDecision.REJECT, "Deterministic scripted rejection", []);
            }
            return new([], SafetyValidationDecision.APPROVE, "Deterministic scripted recommendation", []);
        });
        var agent = new GeminiSafetyValidationAgent(db, new SafetyValidationTools(db), provider,
            NullLogger<GeminiSafetyValidationAgent>.Instance);
        var result = await agent.ValidateAsync(assignment.Id);

        var failed = caseId switch
        {
            "G02" => new[] { "TEAM_AVAILABLE" },
            "G03" => ["REQUIRED_SKILL_PRESENT"],
            "G04" => ["VEHICLE_AVAILABLE"],
            "G05" => ["VEHICLE_CAPACITY"],
            "G06" => ["TEAM_CONFLICT"],
            "G07" => ["VEHICLE_CONFLICT"],
            "G08" => ["PLAN_VERSION_MATCHES", "PLAN_VERSION_CURRENT"],
            "G09" or "G10" or "G11" or "G12" => ["GEMINI_PROVIDER"],
            _ => []
        };
        var expectedDecision = caseId == "G15" ? SafetyValidationDecision.REJECT
            : failed.Length == 0 ? SafetyValidationDecision.APPROVE : SafetyValidationDecision.REVISE;
        Assert.Equal(expectedDecision, result.Decision);
        Assert.Equal(failed.Order(), result.FailedChecks.Order());
        Assert.Equal(Mandatory.Order(), result.Checks.Select(c => c.Name).Order());
        Assert.All(result.Checks, check => Assert.Equal(!failed.Contains(check.Name), check.Passed));
        Assert.Equal(WorkflowStatus.AwaitingApproval, result.WorkflowStatus);
        Assert.Equal(caseId == "G08", result.IsStale);
        Assert.Equal(assignment.Id, result.AssignmentId);
        Assert.Equal(initialVersion, result.PlanVersion);
        Assert.NotNull(result.WorkflowId);
        Assert.Equal(caseId == "G08" ? initialVersion + 1 : initialVersion, assignment.PlanVersion);
        Assert.Equal(before, await OperationalSnapshot(db));
        Assert.Empty(db.Dispatches);
        Assert.Equal(AssignmentStatus.Proposed, assignment.Status);

        var workflow = await db.AgentWorkflows.AsNoTracking().SingleAsync();
        Assert.Equal(result.WorkflowId, workflow.Id);
        Assert.Equal(WorkflowStatus.AwaitingApproval, workflow.Status);
        Assert.Null(workflow.ApprovedByUserId);
        Assert.Null(workflow.ApprovalDecisionAt);
        var input = JsonSerializer.Deserialize<AssignmentValidationContextDto>(workflow.ObjectiveSnapshotJson)!;
        Assert.Equal(assignment.Id, input.AssignmentId);
        Assert.Equal(initialVersion, input.PlanVersion);
        var persisted = JsonSerializer.Deserialize<SafetyValidationWorkflowResultDto>(workflow.FinalOutcomeJson!)!;
        Assert.Equal(result.Decision, persisted.Decision);
        Assert.Equal(result.AssignmentId, persisted.AssignmentId);
        Assert.Equal(result.PlanVersion, persisted.PlanVersion);
        Assert.Equal(result.IsStale, persisted.IsStale);
        Assert.Equal(failed.Order(), persisted.FailedChecks.Order());
        var steps = await db.AgentSteps.AsNoTracking().Where(s => s.AgentWorkflowId == workflow.Id).ToListAsync();
        var mandatorySteps = steps.Where(s => s.Action.StartsWith("mandatory_")).ToList();
        Assert.Equal(10, mandatorySteps.Count);
        foreach (var step in mandatorySteps)
        {
            Assert.Equal(StepStatus.Completed, step.Status); // Execution completed even if a safety check failed.
            var check = JsonSerializer.Deserialize<SafetyValidationCheckDto>(step.ValidationResultJson!)!;
            Assert.Contains(check.Name, Mandatory);
            Assert.Equal(!failed.Contains(check.Name), check.Passed);
        }
        var toolSteps = steps.Except(mandatorySteps).ToList();
        Assert.Equal(caseId == "G12" ? 12 : caseId == "G13" ? 2 : 1, provider.Calls);
        Assert.Equal(caseId == "G12" ? 12 : caseId is "G11" or "G13" ? 1 : 0, toolSteps.Count);
        if (caseId == "G11")
        {
            var step = Assert.Single(toolSteps);
            Assert.Equal("arbitrary_sql", step.Action);
            Assert.Equal(StepStatus.Failed, step.Status);
            Assert.Equal("TOOL_CALL", JsonSerializer.Deserialize<SafetyValidationCheckDto>(step.ValidationResultJson!)!.Name);
        }
        if (caseId is "G12" or "G13")
            Assert.All(toolSteps, step => { Assert.Equal("check_team_availability", step.Action); Assert.Equal(StepStatus.Completed, step.Status); });

        if (caseId == "G14")
        {
            // Simulate an external resource change AFTER validation, in a fresh request scope.
            vehicle.Status = VehicleStatus.UnderMaintenance;
            await db.SaveChangesAsync();
            var beforeDecision = await OperationalSnapshot(db);
            var historicalJson = workflow.FinalOutcomeJson;
            db.ChangeTracker.Clear();
            var decision = await new DispatchService(db, new SafetyValidationAgent(db), new RejectHelpRequestResponseStatusService()).DecideAsync(
                assignment.Id, "d1000000-0000-4000-8000-000000000099",
                new(result.WorkflowId!.Value, initialVersion, CoordinatorDecision.APPROVE, "Golden live revalidation"));
            Assert.False(decision.Success);
            Assert.Contains("Live deterministic safety validation failed", decision.Error);
            Assert.Null(decision.Dispatch);
            Assert.Empty(db.Dispatches);
            Assert.Equal(beforeDecision, await OperationalSnapshot(db));
            var afterWorkflow = await db.AgentWorkflows.AsNoTracking().SingleAsync();
            Assert.Equal(WorkflowStatus.AwaitingApproval, afterWorkflow.Status);
            Assert.Equal(historicalJson, afterWorkflow.FinalOutcomeJson);
            Assert.Null(afterWorkflow.ApprovalDecisionAt);
            Assert.Null(afterWorkflow.ApprovedByUserId);
        }
    }

    // Compare persisted operational state, excluding audit timestamps and the deliberately changed G08 version.
    private static async Task<string> OperationalSnapshot(ComponentDDbContext db) => JsonSerializer.Serialize(new
    {
        Teams = await db.RescueTeams.AsNoTracking().OrderBy(t => t.Id).Select(t => new { t.Id, t.Name, t.Status }).ToListAsync(),
        Members = await db.TeamMembers.AsNoTracking().OrderBy(m => m.Id).Select(m => new { m.Id, m.RescueTeamId, m.Skill, m.IsAvailable }).ToListAsync(),
        Vehicles = await db.Vehicles.AsNoTracking().OrderBy(v => v.Id).Select(v => new { v.Id, v.RescueTeamId, v.Status, v.Capacity }).ToListAsync(),
        Assignments = await db.Assignments.AsNoTracking().OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.RescueTeamId, a.VehicleId, a.Status, a.RequiredSkill, a.RequiredCapacity, a.IncidentId, a.HelpRequestId }).ToListAsync(),
        Dispatches = await db.Dispatches.AsNoTracking().Select(d => d.Id).ToListAsync()
    });

    private static GeminiSafetyAgentResponse Tool(string name, AssignmentValidationContextDto context, string callId, string interactionId)
        => new([new(name, JsonSerializer.SerializeToElement(new { assignmentId = context.AssignmentId, planVersion = context.PlanVersion }), callId)],
            null, null, null, interactionId);

    private sealed class ScriptedProvider(
        Func<int, AssignmentValidationContextDto, IReadOnlyList<object>, string?, Task<GeminiSafetyAgentResponse>> response)
        : IGeminiSafetyValidationClient
    {
        public int Calls { get; private set; }
        public Task<GeminiSafetyAgentResponse> GetNextResponseAsync(AssignmentValidationContextDto context,
            IReadOnlyList<object> priorToolResults, string? previousInteractionId, CancellationToken cancellationToken)
            => response(++Calls, context, priorToolResults, previousInteractionId);
    }
}
