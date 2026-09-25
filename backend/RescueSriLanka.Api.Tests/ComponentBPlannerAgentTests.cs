using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentB.Agents.PlannerAgent;
using RescueSriLanka.Api.Features.ComponentB.DTOs;
using RescueSriLanka.Api.Features.ComponentB.Models;
using RescueSriLanka.Api.Features.ComponentB.Services;

namespace RescueSriLanka.Api.Tests;

public class ComponentBPlannerAgentTests
{
    [Theory]
    [InlineData(90, "High", "Danger")]
    [InlineData(50, "Medium", "Caution")]
    public async Task TriggerAsync_BuildsTheExpectedDeterministicSeverity(int urgency, string severity, string zone)
    {
        await using var db = CreateContext();
        var request = await AddRequest(db, urgency);
        var agent = CreateAgent(db, new FakeAiAnalysis());

        var workflow = await agent.TriggerAsync(Trigger(request));
        var analysis = JsonDocument.Parse(workflow.Steps[0].ToolResultJson!);

        Assert.Equal(PlannerWorkflowStatus.AwaitingApproval, workflow.Status);
        Assert.Equal(severity, analysis.RootElement.GetProperty("severity").GetString());
        Assert.Equal(zone, analysis.RootElement.GetProperty("zone").GetString());
    }

    [Fact]
    public async Task TriggerAsync_FailsDuplicateActiveWorkflowDeterministically()
    {
        await using var db = CreateContext();
        var request = await AddRequest(db, 90);
        var agent = CreateAgent(db, new FakeAiAnalysis());
        await agent.TriggerAsync(Trigger(request));

        var duplicate = await agent.TriggerAsync(Trigger(request));
        var validation = JsonDocument.Parse(duplicate.Steps[2].ValidationResultJson!);

        Assert.Equal(PlannerWorkflowStatus.Failed, duplicate.Status);
        Assert.True(validation.RootElement.GetProperty("duplicateActiveWorkflow").GetBoolean());
    }

    [Theory]
    [InlineData(HelpRequestStatus.Resolved)]
    [InlineData(HelpRequestStatus.Cancelled)]
    public async Task TriggerAsync_FailsWhenRequestIsTerminal(HelpRequestStatus status)
    {
        await using var db = CreateContext();
        var request = await AddRequest(db, 50, status);
        var workflow = await CreateAgent(db, new FakeAiAnalysis()).TriggerAsync(Trigger(request));
        var validation = JsonDocument.Parse(workflow.Steps[2].ValidationResultJson!);

        Assert.Equal(PlannerWorkflowStatus.Failed, workflow.Status);
        Assert.False(validation.RootElement.GetProperty("requestStillActionable").GetBoolean());
    }

    [Fact]
    public async Task TriggerAsync_ContinuesWithoutGeminiAndRecordsAvailabilityFalse()
    {
        await using var db = CreateContext();
        var request = await AddRequest(db, 90);
        var workflow = await CreateAgent(db, new FakeAiAnalysis(throwOnCall: true)).TriggerAsync(Trigger(request));
        var analysis = JsonDocument.Parse(workflow.Steps[0].ToolResultJson!);

        Assert.Equal(PlannerWorkflowStatus.AwaitingApproval, workflow.Status);
        Assert.False(analysis.RootElement.GetProperty("aiAnalysisAvailable").GetBoolean());
        Assert.Equal("High", analysis.RootElement.GetProperty("severity").GetString());
    }

    [Fact]
    public async Task TriggerAsync_RejectsUnknownRequestWithoutPersistingWorkflow()
    {
        await using var db = CreateContext();
        var agent = CreateAgent(db, new FakeAiAnalysis());

        await Assert.ThrowsAsync<KeyNotFoundException>(() => agent.TriggerAsync(Trigger(new HelpRequest { Id = Guid.NewGuid() })));
        Assert.Empty(await db.AgentWorkflows.ToListAsync());
    }

    [Fact]
    public async Task DecideApprovalAsync_RejectsSecondDecision()
    {
        await using var db = CreateContext();
        var request = await AddRequest(db, 60);
        var agent = CreateAgent(db, new FakeAiAnalysis());
        var workflow = await agent.TriggerAsync(Trigger(request));
        await agent.DecideApprovalAsync(workflow.Id, Guid.NewGuid(), new ApprovalDecisionDto { Approved = true });

        await Assert.ThrowsAsync<InvalidOperationException>(() => agent.DecideApprovalAsync(
            workflow.Id, Guid.NewGuid(), new ApprovalDecisionDto { Approved = false }));
    }

    private static TriggerWorkflowDto Trigger(HelpRequest request) => new()
    {
        ObjectiveType = PlannerWorkflowObjectiveType.HelpRequest,
        ObjectiveId = request.Id
    };

    private static async Task<HelpRequest> AddRequest(AppDbContext db, int urgency, HelpRequestStatus status = HelpRequestStatus.Pending)
    {
        var request = new HelpRequest
        {
            CitizenId = Guid.NewGuid(), Type = HelpRequestType.Rescue,
            Description = "Need help at the reported location.",
            Latitude = 6.9271, Longitude = 79.8612,
            UrgencyScore = urgency, Status = status
        };
        db.HelpRequests.Add(request);
        await db.SaveChangesAsync();
        return request;
    }

    private static PlannerAgentService CreateAgent(AppDbContext db, IAiAnalysisService ai) =>
        new(db, new HelpRequestServiceForAgent(db), ai);

    private static AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FakeAiAnalysis(bool throwOnCall = false) : IAiAnalysisService
    {
        public Task<AiAnalysisResult?> AnalyzeHelpRequestAsync(string type, string description, int urgencyScore)
        {
            if (throwOnCall) throw new HttpRequestException("Gemini is unavailable.");
            return Task.FromResult<AiAnalysisResult?>(null);
        }
    }
}
