using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RescueSriLanka.Api.Agents.SafetyValidation;
using RescueSriLanka.Api.DTOs;
using RescueSriLanka.Api.Models;
using Xunit;

namespace RescueSriLanka.Api.Tests;

public class GeminiSafetyValidationAgentTests
{
    [Fact]
    public async Task AllMandatoryChecksPassing_AllowsApproveRecommendation()
    {
        using var db = TestDbFactory.Create();
        var assignment = await SeedValidAssignmentAsync(db);

        var result = await CreateAgent(db, _ => Approve()).ValidateAsync(assignment.Id);

        Assert.Equal(SafetyValidationDecision.APPROVE, result.Decision);
        Assert.All(result.Checks, check => Assert.True(check.Passed));
        Assert.Equal(assignment.Id, result.AssignmentId);
        Assert.Equal(assignment.PlanVersion, result.PlanVersion);
    }

    [Theory]
    [InlineData("team")]
    [InlineData("skill")]
    [InlineData("vehicle")]
    [InlineData("capacity")]
    public async Task DeterministicFailures_CannotApprove(string failure)
    {
        using var db = TestDbFactory.Create();
        var assignment = await SeedValidAssignmentAsync(db);
        var team = await db.RescueTeams.FindAsync(assignment.RescueTeamId);
        var vehicle = await db.Vehicles.FindAsync(assignment.VehicleId);
        switch (failure)
        {
            case "team": team!.Status = TeamStatus.OnMission; break;
            case "skill": team!.Members.Single().Skill = SkillType.Driving; break;
            case "vehicle": vehicle!.Status = VehicleStatus.InUse; break;
            case "capacity": vehicle!.Capacity = 0; break;
        }
        await db.SaveChangesAsync();

        var result = await CreateAgent(db, _ => Approve()).ValidateAsync(assignment.Id);

        Assert.NotEqual(SafetyValidationDecision.APPROVE, result.Decision);
        Assert.NotEmpty(result.FailedChecks);
    }

    [Fact]
    public async Task TeamConflict_CannotApprove()
    {
        using var db = TestDbFactory.Create();
        var assignment = await SeedValidAssignmentAsync(db);
        db.Assignments.Add(new Assignment
        {
            IncidentId = Guid.NewGuid(), RescueTeamId = assignment.RescueTeamId,
            VehicleId = Guid.NewGuid(), RequiredSkill = assignment.RequiredSkill,
            Status = AssignmentStatus.Proposed
        });
        await db.SaveChangesAsync();

        var result = await CreateAgent(db, _ => Approve()).ValidateAsync(assignment.Id);

        Assert.NotEqual(SafetyValidationDecision.APPROVE, result.Decision);
        Assert.Contains("TEAM_CONFLICT", result.FailedChecks);
    }

    [Fact]
    public async Task VehicleConflict_CannotApprove()
    {
        using var db = TestDbFactory.Create();
        var assignment = await SeedValidAssignmentAsync(db);
        db.Assignments.Add(new Assignment
        {
            IncidentId = Guid.NewGuid(), RescueTeamId = Guid.NewGuid(),
            VehicleId = assignment.VehicleId, RequiredSkill = assignment.RequiredSkill,
            Status = AssignmentStatus.Proposed
        });
        await db.SaveChangesAsync();

        var result = await CreateAgent(db, _ => Approve()).ValidateAsync(assignment.Id);

        Assert.NotEqual(SafetyValidationDecision.APPROVE, result.Decision);
        Assert.Contains("VEHICLE_CONFLICT", result.FailedChecks);
    }

    [Fact]
    public async Task VehicleOwnedByAnotherTeam_CannotApprove()
    {
        using var db = TestDbFactory.Create();
        var assignment = await SeedValidAssignmentAsync(db);
        var vehicle = await db.Vehicles.FindAsync(assignment.VehicleId);
        vehicle!.RescueTeamId = Guid.NewGuid();
        await db.SaveChangesAsync();

        var result = await CreateAgent(db, _ => Approve()).ValidateAsync(assignment.Id);

        Assert.NotEqual(SafetyValidationDecision.APPROVE, result.Decision);
        Assert.Contains("VEHICLE_OWNERSHIP", result.FailedChecks);
    }

    [Fact]
    public async Task UnknownToolRequest_FailsClosedAndPersistsFailedStep()
    {
        using var db = TestDbFactory.Create();
        var assignment = await SeedValidAssignmentAsync(db);
        var result = await CreateAgent(db, _ => Tool("arbitrary_sql", assignment.Id, assignment.PlanVersion)).ValidateAsync(assignment.Id);

        Assert.Equal(SafetyValidationDecision.REVISE, result.Decision);
        Assert.Contains("GEMINI_PROVIDER", result.FailedChecks);
        Assert.Contains(db.AgentSteps, s => s.Status == Models.Agents.StepStatus.Failed);
    }

    [Fact]
    public async Task MalformedToolArguments_FailClosed()
    {
        using var db = TestDbFactory.Create();
        var assignment = await SeedValidAssignmentAsync(db);
        var args = JsonDocument.Parse("{\"assignmentId\":\"not-a-guid\"}").RootElement.Clone();
        var result = await CreateAgent(db, _ => new GeminiSafetyAgentResponse(
            [new GeminiSafetyToolCall("check_team_availability", args, "call-1")], null, null, null)).ValidateAsync(assignment.Id);

        Assert.Equal(SafetyValidationDecision.REVISE, result.Decision);
        Assert.Contains("GEMINI_PROVIDER", result.FailedChecks);
    }

    [Fact]
    public async Task ProviderException_FailsClosed()
    {
        using var db = TestDbFactory.Create();
        var assignment = await SeedValidAssignmentAsync(db);
        var result = await CreateAgent(db, _ => throw new HttpRequestException("offline")).ValidateAsync(assignment.Id);

        Assert.Equal(SafetyValidationDecision.REVISE, result.Decision);
        Assert.Contains("GEMINI_PROVIDER", result.FailedChecks);
    }

    [Fact]
    public async Task MissingAssignment_FailsClosedWithoutCreatingWorkflow()
    {
        using var db = TestDbFactory.Create();

        var result = await CreateAgent(db, _ => Approve()).ValidateAsync(Guid.NewGuid());

        Assert.Equal(SafetyValidationDecision.REVISE, result.Decision);
        Assert.Contains("ASSIGNMENT_EXISTS", result.FailedChecks);
        Assert.Null(result.WorkflowId);
        Assert.Empty(db.AgentWorkflows);
    }

    [Fact]
    public async Task MalformedGeminiResponse_FailsClosed()
    {
        using var db = TestDbFactory.Create();
        var assignment = await SeedValidAssignmentAsync(db);

        var result = await CreateAgent(db, _ => new GeminiSafetyAgentResponse([], null, null, null)).ValidateAsync(assignment.Id);

        Assert.Equal(SafetyValidationDecision.REVISE, result.Decision);
        Assert.Contains("GEMINI_PROVIDER", result.FailedChecks);
    }

    [Fact]
    public async Task ModelCanOmitToolCalls_ButCannotSkipMandatoryChecks()
    {
        using var db = TestDbFactory.Create();
        var assignment = await SeedValidAssignmentAsync(db);
        (await db.RescueTeams.FindAsync(assignment.RescueTeamId))!.Status = TeamStatus.OffDuty;
        await db.SaveChangesAsync();

        var result = await CreateAgent(db, _ => Approve()).ValidateAsync(assignment.Id);

        Assert.Equal(SafetyValidationDecision.REVISE, result.Decision);
        Assert.Contains("TEAM_AVAILABLE", result.FailedChecks);
    }

    [Fact]
    public async Task IterationLimit_FailsClosed()
    {
        using var db = TestDbFactory.Create();
        var assignment = await SeedValidAssignmentAsync(db);

        var result = await CreateAgent(db, _ => Tool("check_team_availability", assignment.Id, assignment.PlanVersion)).ValidateAsync(assignment.Id);

        Assert.Equal(SafetyValidationDecision.REVISE, result.Decision);
        Assert.Contains("GEMINI_PROVIDER", result.FailedChecks);
        Assert.True(db.AgentSteps.Count() >= 12);
    }

    [Fact]
    public async Task ExplicitReject_RemainsReject()
    {
        using var db = TestDbFactory.Create();
        var assignment = await SeedValidAssignmentAsync(db);

        var result = await CreateAgent(db, _ => new GeminiSafetyAgentResponse([], SafetyValidationDecision.REJECT, "Manual review rejected this plan.", [])).ValidateAsync(assignment.Id);

        Assert.Equal(SafetyValidationDecision.REJECT, result.Decision);
        Assert.Empty(result.FailedChecks);
    }

    [Fact]
    public async Task AssignmentContextTool_ReturnsTrustedStructuredContext()
    {
        using var db = TestDbFactory.Create();
        var assignment = await SeedValidAssignmentAsync(db);
        var tools = new SafetyValidationTools(db);
        var args = JsonDocument.Parse($"{{\"assignmentId\":\"{assignment.Id}\",\"planVersion\":{assignment.PlanVersion}}}").RootElement.Clone();

        var toolResult = await tools.ExecuteAsync("get_assignment_context", args, assignment.Id, assignment.PlanVersion);

        Assert.NotNull(toolResult);
        Assert.True(toolResult!.Passed);
        var context = Assert.IsType<AssignmentValidationContextDto>(toolResult.Details);
        Assert.Equal(assignment.Id, context.AssignmentId);
        Assert.Equal(assignment.PlanVersion, context.PlanVersion);
    }

    [Fact]
    public async Task MismatchedPlanVersionToolArgument_FailsClosed()
    {
        using var db = TestDbFactory.Create();
        var assignment = await SeedValidAssignmentAsync(db);
        var args = JsonDocument.Parse($"{{\"assignmentId\":\"{assignment.Id}\",\"planVersion\":{assignment.PlanVersion + 1}}}").RootElement.Clone();

        var result = await CreateAgent(db, _ => new GeminiSafetyAgentResponse(
            [new GeminiSafetyToolCall("check_vehicle_capacity", args, "call-1")], null, null, null)).ValidateAsync(assignment.Id);

        Assert.Equal(SafetyValidationDecision.REVISE, result.Decision);
        Assert.Contains("GEMINI_PROVIDER", result.FailedChecks);
    }

    [Fact]
    public async Task PlanVersionChange_MarksResultStale()
    {
        using var db = TestDbFactory.Create();
        var assignment = await SeedValidAssignmentAsync(db);
        var agent = CreateAgent(db, _ =>
        {
            assignment.PlanVersion++;
            db.SaveChanges();
            return Approve();
        });

        var result = await agent.ValidateAsync(assignment.Id);

        Assert.True(result.IsStale);
        Assert.Equal(SafetyValidationDecision.REVISE, result.Decision);
        Assert.Contains("PLAN_VERSION_CURRENT", result.FailedChecks);
    }

    [Fact]
    public async Task ToolResultsAndFinalOutcome_ArePersistedWithoutDispatching()
    {
        using var db = TestDbFactory.Create();
        var assignment = await SeedValidAssignmentAsync(db);
        var responses = new Queue<GeminiSafetyAgentResponse>([
            Tool("check_team_availability", assignment.Id, assignment.PlanVersion), Approve()]);

        var result = await CreateAgent(db, _ => responses.Dequeue()).ValidateAsync(assignment.Id);

        var workflow = await db.AgentWorkflows.FindAsync(result.WorkflowId);
        Assert.NotNull(workflow);
        Assert.NotNull(workflow!.FinalOutcomeJson);
        Assert.Contains(db.AgentSteps, s => s.Action == "check_team_availability" && s.ToolResultJson is not null);
        Assert.Empty(db.Dispatches);
        Assert.Equal(AssignmentStatus.Proposed, (await db.Assignments.FindAsync(assignment.Id))!.Status);
    }

    private static GeminiSafetyValidationAgent CreateAgent(
        Data.ComponentDDbContext db,
        Func<AssignmentValidationContextDto, GeminiSafetyAgentResponse> response) =>
        new(db, new SafetyValidationTools(db), new FakeGeminiClient(response), NullLogger<GeminiSafetyValidationAgent>.Instance);

    private static GeminiSafetyAgentResponse Approve() => new([], SafetyValidationDecision.APPROVE, "Deterministic facts support human review.", []);

    private static GeminiSafetyAgentResponse Tool(string name, Guid assignmentId, int version)
    {
        var arguments = JsonDocument.Parse($"{{\"assignmentId\":\"{assignmentId}\",\"planVersion\":{version}}}").RootElement.Clone();
        return new([new GeminiSafetyToolCall(name, arguments, "call-1")], null, null, null);
    }

    private static async Task<Assignment> SeedValidAssignmentAsync(Data.ComponentDDbContext db)
    {
        var team = new RescueTeam { Name = "Safety", Status = TeamStatus.Available };
        team.Members.Add(new TeamMember { FullName = "Medic", Phone = "1", Skill = SkillType.Paramedic, IsAvailable = true });
        var vehicle = new Vehicle { PlateNumber = "SV-1", Type = VehicleType.Ambulance, Status = VehicleStatus.Available, Capacity = 4, RescueTeamId = team.Id };
        db.RescueTeams.Add(team);
        db.Vehicles.Add(vehicle);
        var assignment = new Assignment
        {
            IncidentId = Guid.NewGuid(), RescueTeamId = team.Id, VehicleId = vehicle.Id,
            RequiredSkill = SkillType.Paramedic, RequiredCapacity = 2, Status = AssignmentStatus.Proposed, PlanVersion = 3
        };
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();
        return assignment;
    }

    private sealed class FakeGeminiClient(Func<AssignmentValidationContextDto, GeminiSafetyAgentResponse> response) : IGeminiSafetyValidationClient
    {
        public Task<GeminiSafetyAgentResponse> GetNextResponseAsync(AssignmentValidationContextDto context, IReadOnlyList<object> _, CancellationToken __)
            => Task.FromResult(response(context));
    }
}
