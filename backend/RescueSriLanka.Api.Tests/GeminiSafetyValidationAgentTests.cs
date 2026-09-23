using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RescueSriLanka.Api.Features.ComponentD.Agents.SafetyValidation;
using RescueSriLanka.Api.Features.ComponentD.DTOs;
using RescueSriLanka.Api.Features.ComponentD.Models;
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
    public async Task AvailableMemberWithFirstAid_PassesRequiredSkillCheckWithPositiveReason()
    {
        using var db = TestDbFactory.Create();
        var assignment = await SeedValidAssignmentAsync(db);
        var member = (await db.RescueTeams.Include(t => t.Members).SingleAsync()).Members.Single();
        member.Skill = SkillType.FirstAid;
        assignment.RequiredSkill = SkillType.FirstAid;
        await db.SaveChangesAsync();

        var check = (await new SafetyValidationTools(db).RunMandatoryChecksAsync(assignment.Id, assignment.PlanVersion))
            .Single(c => c.Name == "REQUIRED_SKILL_PRESENT");

        Assert.True(check.Passed);
        Assert.Equal("An available member has the required skill.", check.Reason);
    }

    [Fact]
    public async Task NoAvailableMemberWithFirstAid_FailsRequiredSkillCheckWithFailureReason()
    {
        using var db = TestDbFactory.Create();
        var assignment = await SeedValidAssignmentAsync(db);
        var member = (await db.RescueTeams.Include(t => t.Members).SingleAsync()).Members.Single();
        member.Skill = SkillType.FirstAid;
        member.IsAvailable = false;
        assignment.RequiredSkill = SkillType.FirstAid;
        await db.SaveChangesAsync();

        var check = (await new SafetyValidationTools(db).RunMandatoryChecksAsync(assignment.Id, assignment.PlanVersion))
            .Single(c => c.Name == "REQUIRED_SKILL_PRESENT");

        Assert.False(check.Passed);
        Assert.Equal("No available member has the required skill.", check.Reason);
    }

    [Fact]
    public void InteractionsResponse_FunctionCall_IsParsedWithItsProviderCallId()
    {
        var result = GeminiSafetyValidationClient.Parse("""
            {"id":"int-1","steps":[{"type":"function_call","name":"check_required_skill","id":"call-1","arguments":{"assignmentId":"00000000-0000-0000-0000-000000000001","planVersion":1}}]}
            """);

        var call = Assert.Single(result.ToolCalls);
        Assert.Equal("check_required_skill", call.Name);
        Assert.Equal("call-1", call.CallId);
        Assert.Equal("int-1", result.InteractionId);
    }

    [Theory]
    [InlineData("APPROVE")]
    [InlineData("REVISE")]
    [InlineData("REJECT")]
    public void InteractionsResponse_StructuredFinalDecision_IsParsed(string decision)
    {
        var result = GeminiSafetyValidationClient.Parse($$"""
            {"id":"int-final","steps":[{"type":"model_output","content":[{"type":"text","text":"{\"decision\":\"{{decision}}\",\"summary\":\"Structured result.\",\"failedChecks\":[],\"suggestedActions\":[]}"}]}]}
            """);

        Assert.Equal(Enum.Parse<SafetyValidationDecision>(decision), result.Decision);
        Assert.Equal("Structured result.", result.Summary);
    }

    [Fact]
    public void InteractionsResponse_MarkdownFencedStructuredDecision_IsParsed()
    {
        var result = GeminiSafetyValidationClient.Parse("""
            {"id":"int-final","steps":[{"type":"model_output","content":[{"type":"text","text":"```json\n{\"decision\":\"REVISE\",\"summary\":\"Review needed.\",\"failedChecks\":[\"TEAM_CONFLICT\"],\"suggestedActions\":[\"Revise\"]}\n```"}]}]}
            """);

        Assert.Equal(SafetyValidationDecision.REVISE, result.Decision);
        Assert.Equal("Review needed.", result.Summary);
    }

    [Theory]
    [InlineData("{\"steps\":[]}")]
    [InlineData("{ not json }")]
    public void InteractionsResponse_EmptyOrMalformedOutput_HasNoDecision(string raw)
    {
        var result = GeminiSafetyValidationClient.Parse(raw);

        Assert.Null(result.Decision);
        Assert.Empty(result.ToolCalls);
    }

    [Fact]
    public async Task ToolContinuation_PreservesInteractionAndFunctionCallIds()
    {
        using var db = TestDbFactory.Create();
        var assignment = await SeedValidAssignmentAsync(db);
        var client = new RecordingGeminiClient(new Queue<GeminiSafetyAgentResponse>([
            Tool("check_team_availability", assignment.Id, assignment.PlanVersion, "int-1", "call-1"),
            Approve("int-2")]));
        var agent = new GeminiSafetyValidationAgent(db, new SafetyValidationTools(db), client, NullLogger<GeminiSafetyValidationAgent>.Instance);

        var result = await agent.ValidateAsync(assignment.Id);

        Assert.Equal(SafetyValidationDecision.APPROVE, result.Decision);
        Assert.Equal("int-1", client.Calls[1].PreviousInteractionId);
        var continuation = JsonSerializer.Serialize(client.Calls[1].ToolResults);
        Assert.Contains("\"type\":\"function_result\"", continuation);
        Assert.Contains("\"call_id\":\"call-1\"", continuation);
    }

    [Fact]
    public async Task MissingFunctionCallId_FailsClosedWithoutExecutingTool()
    {
        using var db = TestDbFactory.Create();
        var assignment = await SeedValidAssignmentAsync(db);
        var result = await CreateAgent(db, _ => Tool("check_team_availability", assignment.Id, assignment.PlanVersion, "int-1", null))
            .ValidateAsync(assignment.Id);

        Assert.Equal(SafetyValidationDecision.REVISE, result.Decision);
        Assert.Contains("GEMINI_PROVIDER", result.FailedChecks);
        Assert.DoesNotContain(db.AgentSteps, step => step.Action == "check_team_availability" && step.Status == StepStatus.Completed);
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
        Assert.Contains(db.AgentSteps, s => s.Status == StepStatus.Failed);
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
        RescueSriLanka.Api.Features.ComponentD.Data.ComponentDDbContext db,
        Func<AssignmentValidationContextDto, GeminiSafetyAgentResponse> response) =>
        new(db, new SafetyValidationTools(db), new FakeGeminiClient(response), NullLogger<GeminiSafetyValidationAgent>.Instance);

    private static GeminiSafetyAgentResponse Approve(string? interactionId = null) => new([], SafetyValidationDecision.APPROVE, "Deterministic facts support human review.", [], interactionId);

    private static GeminiSafetyAgentResponse Tool(string name, Guid assignmentId, int version, string interactionId = "int-1", string? callId = "call-1")
    {
        var arguments = JsonDocument.Parse($"{{\"assignmentId\":\"{assignmentId}\",\"planVersion\":{version}}}").RootElement.Clone();
        return new([new GeminiSafetyToolCall(name, arguments, callId)], null, null, null, interactionId);
    }

    private static async Task<Assignment> SeedValidAssignmentAsync(RescueSriLanka.Api.Features.ComponentD.Data.ComponentDDbContext db)
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
        public Task<GeminiSafetyAgentResponse> GetNextResponseAsync(AssignmentValidationContextDto context, IReadOnlyList<object> _, string? ___, CancellationToken __)
            => Task.FromResult(response(context));
    }

    private sealed class RecordingGeminiClient(Queue<GeminiSafetyAgentResponse> responses) : IGeminiSafetyValidationClient
    {
        public List<(IReadOnlyList<object> ToolResults, string? PreviousInteractionId)> Calls { get; } = [];

        public Task<GeminiSafetyAgentResponse> GetNextResponseAsync(
            AssignmentValidationContextDto _, IReadOnlyList<object> toolResults, string? previousInteractionId, CancellationToken __)
        {
            Calls.Add((toolResults.ToList(), previousInteractionId));
            return Task.FromResult(responses.Dequeue());
        }
    }
}
