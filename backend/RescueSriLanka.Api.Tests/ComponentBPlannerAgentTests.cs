using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentA.Models;
using RescueSriLanka.Api.Features.ComponentB.Agents.PlannerAgent;
using RescueSriLanka.Api.Features.ComponentB.DTOs;
using RescueSriLanka.Api.Features.ComponentB.Models;
using RescueSriLanka.Api.Features.ComponentD.Data;
using TeamStatus = RescueSriLanka.Api.Features.ComponentD.Models.TeamStatus;
using RescueTeam = RescueSriLanka.Api.Features.ComponentD.Models.RescueTeam;

namespace RescueSriLanka.Api.Tests;

public class ComponentBPlannerAgentTests
{
    private const string CoordinatorTeam = "Colombo River Unit";

    [Theory]
    [InlineData(90, "High", "Danger")]
    [InlineData(50, "Medium", "Caution")]
    public async Task TriggerAsync_BuildsTheExpectedDeterministicSeverity(int urgency, string severity, string zone)
    {
        await using var db = CreateContext();
        var request = await AddRequest(db, urgency);
        var (planner, _) = CreatePlanner(db, new ScriptedModel(_ => Final()));

        var workflow = await planner.TriggerAsync(Trigger(request));
        var analysis = JsonDocument.Parse(workflow.Steps[0].ToolResultJson!);

        Assert.Equal(PlannerWorkflowStatus.AwaitingApproval, workflow.Status);
        Assert.Equal(severity, analysis.RootElement.GetProperty("severity").GetString());
        Assert.Equal(zone, analysis.RootElement.GetProperty("zone").GetString());
    }

    [Fact]
    public async Task TriggerAsync_FailsWhenAnApprovedWorkflowAlreadyExists()
    {
        await using var db = CreateContext();
        var request = await AddRequest(db, 90);
        var (planner, _) = CreatePlanner(db, new ScriptedModel(_ => Final()));
        var first = await planner.TriggerAsync(Trigger(request));
        (await db.AgentWorkflows.FindAsync(first.Id))!.Status = PlannerWorkflowStatus.Approved;
        await db.SaveChangesAsync();

        var duplicate = await planner.TriggerAsync(Trigger(request));
        var validation = JsonDocument.Parse(duplicate.Steps[2].ValidationResultJson!);

        Assert.Equal(PlannerWorkflowStatus.Failed, duplicate.Status);
        Assert.True(validation.RootElement.GetProperty("duplicateActiveWorkflow").GetBoolean());
    }

    [Fact]
    public async Task TriggerAsync_ReplacesAPlanAwaitingApprovalInsteadOfFailingBesideIt()
    {
        await using var db = CreateContext();
        var request = await AddRequest(db, 90);
        var (planner, _) = CreatePlanner(db, new ScriptedModel(_ => Final()));
        var first = await planner.TriggerAsync(Trigger(request));

        var rerun = await planner.TriggerAsync(Trigger(request));

        // The re-run is the plan the manager can decide on; the old one is retired.
        Assert.Equal(PlannerWorkflowStatus.AwaitingApproval, rerun.Status);
        Assert.Equal(PlannerWorkflowStatus.Superseded, (await db.AgentWorkflows.FindAsync(first.Id))!.Status);
        Assert.Single(await db.AgentWorkflows
            .Where(w => w.ObjectiveId == request.Id && w.Status == PlannerWorkflowStatus.AwaitingApproval)
            .ToListAsync());
    }

    [Fact]
    public async Task DecideApprovalAsync_RefusesAPlanThatWasSuperseded()
    {
        await using var db = CreateContext();
        var request = await AddRequest(db, 90);
        var (planner, _) = CreatePlanner(db, new ScriptedModel(_ => Final()));
        var first = await planner.TriggerAsync(Trigger(request));
        await planner.TriggerAsync(Trigger(request));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            planner.DecideApprovalAsync(first.Id, Guid.NewGuid(), new ApprovalDecisionDto { Approved = true }));
    }

    [Theory]
    [InlineData(HelpRequestStatus.Resolved)]
    [InlineData(HelpRequestStatus.Cancelled)]
    public async Task TriggerAsync_FailsWhenRequestIsTerminal(HelpRequestStatus status)
    {
        await using var db = CreateContext();
        var request = await AddRequest(db, 50, status);
        var (planner, _) = CreatePlanner(db, new ScriptedModel(_ => Final()));

        var workflow = await planner.TriggerAsync(Trigger(request));
        var validation = JsonDocument.Parse(workflow.Steps[2].ValidationResultJson!);

        Assert.Equal(PlannerWorkflowStatus.Failed, workflow.Status);
        Assert.False(validation.RootElement.GetProperty("requestStillActionable").GetBoolean());
    }

    [Fact]
    public async Task TriggerAsync_ContinuesWithoutTheModelAndRecordsWhy()
    {
        await using var db = CreateContext();
        var request = await AddRequest(db, 90);
        var (planner, _) = CreatePlanner(db, new ScriptedModel(_ => Final(), throwOnCall: true));

        var workflow = await planner.TriggerAsync(Trigger(request));
        var analysis = JsonDocument.Parse(workflow.Steps[0].ToolResultJson!).RootElement;

        Assert.Equal(PlannerWorkflowStatus.AwaitingApproval, workflow.Status);
        Assert.False(analysis.GetProperty("aiAnalysisAvailable").GetBoolean());
        Assert.Equal("High", analysis.GetProperty("severity").GetString());
        Assert.Contains("could not be reached", analysis.GetProperty("modelUnavailableReason").GetString());
    }

    [Fact]
    public async Task TriggerAsync_RejectsUnknownRequestWithoutPersistingWorkflow()
    {
        await using var db = CreateContext();
        var (planner, _) = CreatePlanner(db, new ScriptedModel(_ => Final()));

        await Assert.ThrowsAsync<KeyNotFoundException>(() => planner.TriggerAsync(Trigger(new HelpRequest { Id = Guid.NewGuid() })));
        Assert.Empty(await db.AgentWorkflows.ToListAsync());
    }

    [Fact]
    public async Task DecideApprovalAsync_RejectsSecondDecision()
    {
        await using var db = CreateContext();
        var request = await AddRequest(db, 60, type: HelpRequestType.Shelter);
        var (planner, _) = CreatePlanner(db, new ScriptedModel(_ => Final()));
        var workflow = await planner.TriggerAsync(Trigger(request));
        await planner.DecideApprovalAsync(workflow.Id, Guid.NewGuid(), new ApprovalDecisionDto { Approved = true });

        await Assert.ThrowsAsync<InvalidOperationException>(() => planner.DecideApprovalAsync(
            workflow.Id, Guid.NewGuid(), new ApprovalDecisionDto { Approved = false }));
    }

    // ---- logistics: real rescue-team records

    [Fact]
    public async Task Logistics_UsesTheNearestAvailableTeamWithARecordedBase()
    {
        await using var db = CreateContext();
        var componentD = TestDbFactory.Create();
        componentD.RescueTeams.AddRange(
            Team(CoordinatorTeam, TeamStatus.Available, 6.93, 79.86),
            Team("Kandy Hill Unit", TeamStatus.Available, 7.29, 80.63),
            // Closest of all, but already deployed, so it must not be offered.
            Team("Deployed Unit", TeamStatus.OnMission, 6.9271, 79.8612));
        await componentD.SaveChangesAsync();
        var request = await AddRequest(db, 90);
        var (planner, _) = CreatePlanner(db, new ScriptedModel(_ => Final()), componentD);

        var workflow = await planner.TriggerAsync(Trigger(request));
        var logistics = JsonDocument.Parse(workflow.Steps[1].ToolResultJson!).RootElement;

        Assert.Equal(PlannerStepStatus.Completed, workflow.Steps[1].Status);
        Assert.True(logistics.GetProperty("found").GetBoolean());
        Assert.Equal(CoordinatorTeam, logistics.GetProperty("nearestTeam").GetString());
        Assert.Equal(2, logistics.GetProperty("teamsConsidered").GetInt32());
    }

    [Fact]
    public async Task Logistics_SaysSoPlainlyWhenNoAvailableTeamHasABase()
    {
        await using var db = CreateContext();
        var componentD = TestDbFactory.Create();
        componentD.RescueTeams.Add(Team("Unplaced Unit", TeamStatus.Available, null, null));
        await componentD.SaveChangesAsync();
        var request = await AddRequest(db, 90);
        var (planner, _) = CreatePlanner(db, new ScriptedModel(_ => Final()), componentD);

        var workflow = await planner.TriggerAsync(Trigger(request));
        var logistics = JsonDocument.Parse(workflow.Steps[1].ToolResultJson!).RootElement;

        Assert.False(logistics.GetProperty("found").GetBoolean());
        Assert.Contains("No available rescue team", logistics.GetProperty("reason").GetString());
        Assert.Equal(PlannerWorkflowStatus.AwaitingApproval, workflow.Status);
    }

    // ---- analysis: the model acts through tools

    [Fact]
    public async Task Assessment_CallsReadOnlyToolsAndItsSuggestionIsCheckedAgainstWhatTheyReturned()
    {
        await using var db = CreateContext();
        var componentD = await SeedTeamsAsync();
        var request = await AddRequest(db, 90);
        var model = new ScriptedModel(turn => turn switch
        {
            0 => Call(AssessmentTools.CountNearbyIncidents),
            1 => Call(AssessmentTools.ListAvailableTeams),
            _ => Final(CoordinatorTeam, priority: "High")
        });
        var (planner, _) = CreatePlanner(db, model, componentD);

        var workflow = await planner.TriggerAsync(Trigger(request));
        var analysis = JsonDocument.Parse(workflow.Steps[0].ToolResultJson!).RootElement;
        var validation = JsonDocument.Parse(workflow.Steps[2].ValidationResultJson!).RootElement;

        var calls = analysis.GetProperty("toolCalls").EnumerateArray().ToList();
        Assert.Equal(2, calls.Count);
        Assert.All(calls, call => Assert.Equal("Answered", call.GetProperty("outcome").GetString()));
        Assert.True(analysis.GetProperty("aiAnalysisAvailable").GetBoolean());
        Assert.Equal(CoordinatorTeam, validation.GetProperty("recommendedTeam").GetString());
        Assert.StartsWith("model", validation.GetProperty("recommendedTeamSource").GetString());
        Assert.True(validation.GetProperty("priorityAgreesWithScore").GetBoolean());
    }

    [Fact]
    public async Task Assessment_RefusesAToolOffTheAllowListAndWritesNothing()
    {
        await using var db = CreateContext();
        var request = await AddRequest(db, 90);
        var model = new ScriptedModel(turn => turn switch
        {
            0 => Call("update_help_request_status", new { status = "Resolved" }),
            _ => Final(null)
        });
        var (planner, _) = CreatePlanner(db, model);

        var workflow = await planner.TriggerAsync(Trigger(request));
        var analysis = JsonDocument.Parse(workflow.Steps[0].ToolResultJson!).RootElement;
        var refused = analysis.GetProperty("toolCalls").EnumerateArray().Single();

        Assert.Equal("update_help_request_status", refused.GetProperty("tool").GetString());
        Assert.Equal("Refused", refused.GetProperty("outcome").GetString());
        Assert.Equal(HelpRequestStatus.Pending, (await db.HelpRequests.AsNoTracking().SingleAsync(r => r.Id == request.Id)).Status);
    }

    [Fact]
    public async Task Assessment_DropsATeamTheToolsNeverReturned_AndFallsBackToTheNearest()
    {
        await using var db = CreateContext();
        var componentD = await SeedTeamsAsync();
        var request = await AddRequest(db, 90);
        var model = new ScriptedModel(turn => turn switch
        {
            0 => Call(AssessmentTools.ListAvailableTeams),
            _ => Final("Ghost Unit")
        });
        var (planner, _) = CreatePlanner(db, model, componentD);

        var workflow = await planner.TriggerAsync(Trigger(request));
        var validation = JsonDocument.Parse(workflow.Steps[2].ValidationResultJson!).RootElement;

        Assert.Equal("Ghost Unit", validation.GetProperty("suggestionRejected").GetString());
        Assert.Equal(CoordinatorTeam, validation.GetProperty("recommendedTeam").GetString());
        Assert.Equal("nearest available team (rule)", validation.GetProperty("recommendedTeamSource").GetString());
    }

    [Fact]
    public async Task Assessment_StopsAtTheStepLimitAndSaysSo()
    {
        await using var db = CreateContext();
        var request = await AddRequest(db, 90);
        var (planner, _) = CreatePlanner(db, new ScriptedModel(_ => Call(AssessmentTools.CountNearbyIncidents)));

        var workflow = await planner.TriggerAsync(Trigger(request));
        var analysis = JsonDocument.Parse(workflow.Steps[0].ToolResultJson!).RootElement;

        Assert.False(analysis.GetProperty("aiAnalysisAvailable").GetBoolean());
        Assert.Contains("tool steps", analysis.GetProperty("modelUnavailableReason").GetString());
        Assert.Equal(RequestAssessmentAgent.MaxSteps, analysis.GetProperty("toolCalls").GetArrayLength());
        Assert.Equal(PlannerWorkflowStatus.AwaitingApproval, workflow.Status);
    }

    // ---- approval: the one place the plan acts

    [Fact]
    public async Task Approving_AssignsTheRequestRecordsWhoDidItAndTellsTheCitizen()
    {
        await using var db = CreateContext();
        var componentD = await SeedTeamsAsync();
        var request = await AddRequest(db, 90, type: HelpRequestType.Shelter);
        request.VerificationStatus = VerificationStatus.Verified;
        await db.SaveChangesAsync();
        var model = new ScriptedModel(turn => turn switch
        {
            0 => Call(AssessmentTools.ListAvailableTeams),
            _ => Final(CoordinatorTeam)
        });
        var (planner, emails) = CreatePlanner(db, model, componentD);
        var workflow = await planner.TriggerAsync(Trigger(request));
        var coordinator = Guid.NewGuid();

        var decided = await planner.DecideApprovalAsync(workflow.Id, coordinator, new ApprovalDecisionDto { Approved = true });

        var saved = await db.HelpRequests.AsNoTracking().SingleAsync(r => r.Id == request.Id);
        var history = await db.RequestStatusHistories.AsNoTracking().SingleAsync(h => h.HelpRequestId == request.Id);
        Assert.Equal(HelpRequestStatus.Assigned, saved.Status);
        Assert.Equal(HelpRequestStatus.Pending, history.OldStatus);
        Assert.Equal(HelpRequestStatus.Assigned, history.NewStatus);
        Assert.Equal(coordinator, history.ChangedByUserId);
        Assert.Contains(CoordinatorTeam, history.Notes);
        Assert.Equal([(request.Id, "Assigned")], emails.HelpRequestStatusChanges);
        Assert.Contains("\"outcome\":\"approved\"", decided!.FinalOutcomeJson);
    }

    [Fact]
    public async Task Approving_Rescue_HandsTheRequestToTheRescueCoordinator()
    {
        await using var db = CreateContext();
        var componentD = await SeedTeamsAsync();
        var request = await AddRequest(db, 90, type: HelpRequestType.Rescue);
        request.VerificationStatus = VerificationStatus.Verified;
        await db.SaveChangesAsync();
        var model = new ScriptedModel(turn => turn switch
        {
            0 => Call(AssessmentTools.ListAvailableTeams),
            _ => Final(CoordinatorTeam)
        });
        var (planner, emails) = CreatePlanner(db, model, componentD);
        var workflow = await planner.TriggerAsync(Trigger(request));

        var decided = await planner.DecideApprovalAsync(workflow.Id, Guid.NewGuid(), new ApprovalDecisionDto { Approved = true });

        var saved = await db.HelpRequests.AsNoTracking().SingleAsync(r => r.Id == request.Id);
        Assert.Equal(HelpRequestStatus.Pending, saved.Status);
        Assert.Equal(VerificationStatus.Verified, saved.VerificationStatus);
        Assert.Empty(await db.RequestStatusHistories.ToListAsync());
        Assert.Empty(emails.HelpRequestStatusChanges);
        Assert.Contains("RescueCoordinator", decided!.FinalOutcomeJson);
    }

    [Fact]
    public async Task ApprovingAssignmentPlan_RequiresRequestVerification()
    {
        await using var db = CreateContext();
        var componentD = await SeedTeamsAsync();
        var request = await AddRequest(db, 90, type: HelpRequestType.Shelter);
        var model = new ScriptedModel(turn => turn switch
        {
            0 => Call(AssessmentTools.ListAvailableTeams),
            _ => Final(CoordinatorTeam)
        });
        var (planner, emails) = CreatePlanner(db, model, componentD);
        var workflow = await planner.TriggerAsync(Trigger(request));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => planner.DecideApprovalAsync(
            workflow.Id, Guid.NewGuid(), new ApprovalDecisionDto { Approved = true }));

        Assert.Contains("Verify", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PlannerWorkflowStatus.AwaitingApproval,
            (await planner.GetByIdAsync(workflow.Id))!.Status);
        Assert.Equal(HelpRequestStatus.Pending,
            (await db.HelpRequests.AsNoTracking().SingleAsync(r => r.Id == request.Id)).Status);
        Assert.Empty(await db.RequestStatusHistories.ToListAsync());
        Assert.Empty(emails.HelpRequestStatusChanges);

        request.VerificationStatus = VerificationStatus.Verified;
        await db.SaveChangesAsync();
        await planner.DecideApprovalAsync(workflow.Id, Guid.NewGuid(), new ApprovalDecisionDto { Approved = true });

        Assert.Equal(HelpRequestStatus.Assigned,
            (await db.HelpRequests.AsNoTracking().SingleAsync(r => r.Id == request.Id)).Status);
    }

    [Fact]
    public async Task Rejecting_ChangesNothingAndTellsNobody()
    {
        await using var db = CreateContext();
        var componentD = await SeedTeamsAsync();
        var request = await AddRequest(db, 90);
        var (planner, emails) = CreatePlanner(db, new ScriptedModel(_ => Final(CoordinatorTeam)), componentD);
        var workflow = await planner.TriggerAsync(Trigger(request));

        await planner.DecideApprovalAsync(workflow.Id, Guid.NewGuid(), new ApprovalDecisionDto { Approved = false, Notes = "Duplicate report" });

        Assert.Equal(HelpRequestStatus.Pending, (await db.HelpRequests.AsNoTracking().SingleAsync(r => r.Id == request.Id)).Status);
        Assert.Empty(await db.RequestStatusHistories.ToListAsync());
        Assert.Empty(emails.HelpRequestStatusChanges);
    }

    [Fact]
    public async Task ApprovingWithoutAnyTeamRecordsTheDecisionOnly()
    {
        await using var db = CreateContext();
        var request = await AddRequest(db, 90, type: HelpRequestType.Shelter);
        var (planner, emails) = CreatePlanner(db, new ScriptedModel(_ => Final(null)));
        var workflow = await planner.TriggerAsync(Trigger(request));

        var decided = await planner.DecideApprovalAsync(workflow.Id, Guid.NewGuid(), new ApprovalDecisionDto { Approved = true });

        Assert.Equal(HelpRequestStatus.Pending, (await db.HelpRequests.AsNoTracking().SingleAsync(r => r.Id == request.Id)).Status);
        Assert.Empty(emails.HelpRequestStatusChanges);
        Assert.Contains("\"helpRequestStatus\":\"Pending\"", decided!.FinalOutcomeJson);
    }

    // ---- helpers

    private static TriggerWorkflowDto Trigger(HelpRequest request) => new()
    {
        ObjectiveType = PlannerWorkflowObjectiveType.HelpRequest,
        ObjectiveId = request.Id
    };

    private static RescueTeam Team(string name, TeamStatus status, double? latitude, double? longitude) => new()
    {
        Name = name, Status = status, BaseLatitude = latitude, BaseLongitude = longitude
    };

    /// <summary>Two available teams, one nearer the request than the other.</summary>
    private static async Task<ComponentDDbContext> SeedTeamsAsync()
    {
        var componentD = TestDbFactory.Create();
        componentD.RescueTeams.AddRange(
            Team(CoordinatorTeam, TeamStatus.Available, 6.93, 79.86),
            Team("Kandy Hill Unit", TeamStatus.Available, 7.29, 80.63));
        await componentD.SaveChangesAsync();
        return componentD;
    }

    private static (PlannerAgentService Planner, RecordingActionEmailService Emails) CreatePlanner(
        AppDbContext db, IPlanningModel model, ComponentDDbContext? componentD = null)
    {
        componentD ??= TestDbFactory.Create();
        var emails = new RecordingActionEmailService();
        var assessment = new RequestAssessmentAgent(db, componentD, model, NullLogger<RequestAssessmentAgent>.Instance);
        var planner = new PlannerAgentService(db, componentD, new HelpRequestServiceForAgent(db), assessment, emails);
        return (planner, emails);
    }

    private static async Task<HelpRequest> AddRequest(AppDbContext db, int urgency, HelpRequestStatus status = HelpRequestStatus.Pending,
        HelpRequestType type = HelpRequestType.Rescue)
    {
        var request = new HelpRequest
        {
            CitizenId = Guid.NewGuid(), Type = type,
            Description = "Need help at the reported location.",
            Latitude = 6.9271, Longitude = 79.8612,
            UrgencyScore = urgency, Status = status
        };
        db.HelpRequests.Add(request);
        await db.SaveChangesAsync();
        return request;
    }

    private static AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static JsonElement ModelContent => JsonSerializer.SerializeToElement(new { role = "model", parts = Array.Empty<object>() });

    private static PlanningTurn Call(string name, object? arguments = null) => new(
        [new PlannedToolCall(name, JsonSerializer.SerializeToElement(arguments ?? new { }))],
        null,
        ModelContent);

    private static PlanningTurn Final(string? suggestedTeam = null, string priority = "High") => new(
        [],
        JsonSerializer.Serialize(new
        {
            priority,
            credibility = "Genuine",
            reasoning = "Water is rising near the road.",
            recommendedAction = "Send a boat team",
            suggestedTeam
        }),
        ModelContent);

    /// <summary>A model that answers each turn from a script, or throws, to test the agent without Gemini.</summary>
    private sealed class ScriptedModel(Func<int, PlanningTurn> turnFor, bool throwOnCall = false) : IPlanningModel
    {
        private int _turns;

        public bool IsConfigured => true;

        public Task<PlanningTurn> NextTurnAsync(IReadOnlyList<object> contents, string instruction, CancellationToken ct = default)
        {
            if (throwOnCall) throw new HttpRequestException("Gemini is unavailable.");
            return Task.FromResult(turnFor(_turns++));
        }
    }
}
