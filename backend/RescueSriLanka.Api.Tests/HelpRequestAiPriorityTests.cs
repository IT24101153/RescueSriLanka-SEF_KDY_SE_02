using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentB.Agents.PlannerAgent;
using RescueSriLanka.Api.Features.ComponentB.Controllers;
using RescueSriLanka.Api.Features.ComponentB.DTOs;
using RescueSriLanka.Api.Features.ComponentB.Models;
using RescueSriLanka.Api.Features.ComponentB.Services;

namespace RescueSriLanka.Api.Tests;

// Covers the read-only ai-priority endpoint the Help Request Manager's review
// screen now auto-loads: it must surface the Planner Agent's already-stored
// reasoning/suggested action/credibility signal, not just the bare priority,
// and it must never itself call Gemini.
public sealed class HelpRequestAiPriorityTests
{
    [Fact]
    public async Task ReturnsTheStoredReasoningSuggestedActionAndCredibilitySignal_WhenAiAnalysisSucceeded()
    {
        await using var db = CreateContext();
        var citizenId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var workflow = new AgentWorkflow { ObjectiveType = PlannerWorkflowObjectiveType.HelpRequest, ObjectiveId = requestId, Status = PlannerWorkflowStatus.AwaitingApproval };
        workflow.Steps.Add(new AgentStep
        {
            AgentWorkflowId = workflow.Id,
            StepNumber = 1,
            TargetAgent = PlannerAgentType.IncidentAnalysisAgent,
            CompletedAt = DateTime.UtcNow,
            ToolResultJson = """
                {"severity":"High","aiAnalysisAvailable":true,"aiReasoning":"Flooding reported near a known hazard zone.","aiSuggestedAction":"Dispatch a rescue team promptly.","aiCredibilitySignal":"Looks genuine"}
                """
        });
        db.AgentWorkflows.Add(workflow);
        await db.SaveChangesAsync();

        var controller = BuildController(db, citizenId, requestId);

        var response = Assert.IsType<OkObjectResult>((await controller.GetAiPriority(requestId)).Result);
        var dto = Assert.IsType<AiPriorityResponseDto>(response.Value);

        Assert.Equal("High", dto.Priority);
        Assert.True(dto.AiAnalysisAvailable);
        Assert.Equal("Flooding reported near a known hazard zone.", dto.Reasoning);
        Assert.Equal("Dispatch a rescue team promptly.", dto.SuggestedAction);
        Assert.Equal("Looks genuine", dto.CredibilitySignal);
        Assert.Equal(workflow.Id, dto.WorkflowId);
        Assert.Equal(PlannerWorkflowStatus.AwaitingApproval, dto.WorkflowStatus);
    }

    [Fact]
    public async Task ReturnsPriorityOnly_WhenGeminiWasUnavailableForThatRun()
    {
        await using var db = CreateContext();
        var citizenId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var workflow = new AgentWorkflow { ObjectiveType = PlannerWorkflowObjectiveType.HelpRequest, ObjectiveId = requestId };
        workflow.Steps.Add(new AgentStep
        {
            AgentWorkflowId = workflow.Id,
            StepNumber = 1,
            TargetAgent = PlannerAgentType.IncidentAnalysisAgent,
            CompletedAt = DateTime.UtcNow,
            ToolResultJson = """{"severity":"Medium","aiAnalysisAvailable":false}"""
        });
        db.AgentWorkflows.Add(workflow);
        await db.SaveChangesAsync();

        var controller = BuildController(db, citizenId, requestId);

        var response = Assert.IsType<OkObjectResult>((await controller.GetAiPriority(requestId)).Result);
        var dto = Assert.IsType<AiPriorityResponseDto>(response.Value);

        Assert.Equal("Medium", dto.Priority);
        Assert.False(dto.AiAnalysisAvailable);
        Assert.Null(dto.Reasoning);
        Assert.Null(dto.SuggestedAction);
        Assert.Null(dto.CredibilitySignal);
        Assert.Equal(workflow.Id, dto.WorkflowId);
    }

    [Fact]
    public async Task ReturnsAnalysisPending_WhenNoWorkflowHasRunYet()
    {
        await using var db = CreateContext();
        var citizenId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        var controller = BuildController(db, citizenId, requestId);

        var response = Assert.IsType<OkObjectResult>((await controller.GetAiPriority(requestId)).Result);
        var dto = Assert.IsType<AiPriorityResponseDto>(response.Value);

        Assert.Equal("Analysis pending", dto.Priority);
        Assert.False(dto.AiAnalysisAvailable);
        Assert.Null(dto.WorkflowId);
        Assert.Null(dto.WorkflowStatus);
    }

    private static HelpRequestsController BuildController(AppDbContext db, Guid citizenId, Guid requestId)
    {
        var controller = new HelpRequestsController(
            new FakeHelpRequestService(citizenId, requestId), new UnusedAiAnalysisService(), db, new UnusedPlannerAgentService(), new NoOpActionEmailService())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, citizenId.ToString()),
                        new Claim(ClaimTypes.Role, nameof(RescueSriLanka.Api.Models.UserRole.HelpRequestManager))
                    ], "Test"))
                }
            }
        };
        return controller;
    }

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FakeHelpRequestService(Guid citizenId, Guid requestId) : IHelpRequestService
    {
        public Task<HelpRequestResponseDto> CreateAsync(Guid citizenId, CreateHelpRequestDto dto) => throw new NotSupportedException();
        public Task<HelpRequestResponseDto?> GetByIdAsync(Guid id) => Task.FromResult<HelpRequestResponseDto?>(
            id == requestId ? new HelpRequestResponseDto { Id = requestId, CitizenId = citizenId } : null);
        public Task<List<HelpRequestResponseDto>> GetAllAsync() => throw new NotSupportedException();
        public Task<List<HelpRequestResponseDto>> GetByCitizenAsync(Guid citizenId) => throw new NotSupportedException();
        public Task<HelpRequestResponseDto?> UpdateAsync(Guid id, Guid citizenId, UpdateHelpRequestDto dto) => throw new NotSupportedException();
        public Task<HelpRequestResponseDto?> UpdateStatusAsync(Guid id, Guid changedByUserId, UpdateHelpRequestStatusDto dto) => throw new NotSupportedException();
        public Task<List<StatusHistoryDto>> GetHistoryAsync(Guid id) => throw new NotSupportedException();
        public Task<HelpRequestResponseDto?> VerifyAsync(Guid id, Guid verifiedByUserId, VerifyHelpRequestDto dto) => throw new NotSupportedException();
    }

    private sealed class UnusedAiAnalysisService : IAiAnalysisService
    {
        public Task<AiAnalysisResult?> AnalyzeHelpRequestAsync(string type, string description, int urgencyScore) =>
            throw new NotSupportedException("ai-priority must never call Gemini.");
    }

    private sealed class UnusedPlannerAgentService : IPlannerAgentService
    {
        public Task<AgentWorkflowResponseDto> TriggerAsync(TriggerWorkflowDto dto) =>
            throw new NotSupportedException("ai-priority must never trigger a new workflow.");
        public Task<AgentWorkflowResponseDto?> GetByIdAsync(Guid workflowId) => throw new NotSupportedException();
        public Task<AgentWorkflowResponseDto?> DecideApprovalAsync(Guid workflowId, Guid coordinatorUserId, ApprovalDecisionDto dto) => throw new NotSupportedException();
    }
}
