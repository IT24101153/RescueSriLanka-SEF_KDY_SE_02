using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentB.Models;
using RescueSriLanka.Api.Features.ComponentD.Agents.SafetyValidation;
using RescueSriLanka.Api.Features.ComponentD.Data;
using RescueSriLanka.Api.Features.ComponentD.DTOs;
using RescueSriLanka.Api.Features.ComponentD.Models;
using RescueSriLanka.Api.Features.ComponentD.Services;

namespace RescueSriLanka.Api.Tests;

public class ComponentDAssignmentCancellationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("Citizen", HttpStatusCode.Forbidden)]
    [InlineData("EmergencyCoordinator", HttpStatusCode.Forbidden)]
    [InlineData("HelpRequestManager", HttpStatusCode.Forbidden)]
    public async Task CancelRequiresRescueTeam(string? role, HttpStatusCode expected)
    {
        using var factory = new ComponentDApiFactory();
        var fixture = await factory.Seed(assignment: true);
        using var client = factory.Client(role);
        Assert.Equal(expected, (await client.PostAsync($"/api/assignments/{fixture.AssignmentId}/cancel", null)).StatusCode);
        Assert.Equal(AssignmentStatus.Proposed, await factory.InDb(db => db.Assignments.Select(a => a.Status).SingleAsync()));
    }

    [Theory]
    [InlineData(AssignmentStatus.Proposed)]
    [InlineData(AssignmentStatus.PendingApproval)]
    [InlineData(AssignmentStatus.Rejected)]
    [InlineData(AssignmentStatus.Approved)]
    public async Task CancelPreservesRecordAndReleasesPlanningResources(AssignmentStatus status)
    {
        using var factory = new ComponentDApiFactory();
        var fixture = await factory.Seed(assignment: true);
        await factory.InDb(async db => { var a = await db.Assignments.SingleAsync(); a.Status = status; await db.SaveChangesAsync(); return true; });
        var before = await factory.InDb(db => db.Assignments.AsNoTracking().SingleAsync());
        using var client = factory.Client();
        var response = await client.PostAsync($"/api/assignments/{fixture.AssignmentId}/cancel", null);
        response.EnsureSuccessStatusCode();
        var cancelled = (await response.Content.ReadFromJsonAsync<AssignmentDto>(JsonOptions))!;
        Assert.Equal(AssignmentStatus.Cancelled, cancelled.Status);
        Assert.Equal(fixture.AssignmentId, cancelled.Id); Assert.Equal(fixture.IncidentId, cancelled.IncidentId);
        Assert.Null(cancelled.HelpRequestId); Assert.Equal(before.PlanVersion + 1, cancelled.PlanVersion);
        await factory.InDb(async db => {
            var saved = await db.Assignments.SingleAsync();
            Assert.True(saved.UpdatedAt >= before.UpdatedAt); Assert.Equal(before.CreatedAt, saved.CreatedAt);
            Assert.Empty(await db.Assignments.Active().ToListAsync());
            Assert.Empty(db.Dispatches);
            Assert.Equal(TeamStatus.Available, (await db.RescueTeams.SingleAsync()).Status);
            Assert.Equal(VehicleStatus.Available, (await db.Vehicles.SingleAsync()).Status);
            return true;
        });
        // A new plan can reuse the same team AND vehicle; no historical record was deleted.
        (await client.PostAsJsonAsync("/api/assignments", fixture.Proposal())).EnsureSuccessStatusCode();
        Assert.Equal(2, await factory.InDb(db => db.Assignments.CountAsync()));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/assignments/{fixture.AssignmentId}/cancel", null)).StatusCode);
    }

    [Theory]
    [InlineData(DispatchStatus.Pending)]
    [InlineData(DispatchStatus.Dispatched)]
    [InlineData(DispatchStatus.EnRoute)]
    [InlineData(DispatchStatus.OnScene)]
    [InlineData(DispatchStatus.Resolved)]
    [InlineData(DispatchStatus.Cancelled)]
    public async Task EveryExistingDispatchBlocksCancellationAndRevision(DispatchStatus status)
    {
        using var factory = new ComponentDApiFactory(); var fixture = await factory.Seed(dispatch: true);
        await factory.InDb(async db => { (await db.Dispatches.SingleAsync()).Status = status; await db.SaveChangesAsync(); return true; });
        using var client = factory.Client();
        var response = await client.PostAsync($"/api/assignments/{fixture.AssignmentId}/cancel", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Cancel the dispatch instead", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/assignments/{fixture.AssignmentId}/revise", new ReviseAssignmentDto(fixture.TeamId, fixture.VehicleId, SkillType.FirstAid, 2, null))).StatusCode);
        Assert.Equal(AssignmentStatus.Approved, await factory.InDb(db => db.Assignments.Select(a => a.Status).SingleAsync()));
        Assert.Equal(status, await factory.InDb(db => db.Dispatches.Select(d => d.Status).SingleAsync()));
    }

    [Fact]
    public async Task CancellationBlocksReviewsDecisionsRevisionAndDispatchWithoutDeletingAudit()
    {
        using var factory = new ComponentDApiFactory(); var fixture = await factory.Seed(assignment: true);
        using var client = factory.Client();
        var review = (await (await client.PostAsync($"/api/assignments/{fixture.AssignmentId}/validate", null)).Content.ReadFromJsonAsync<SafetyValidationWorkflowResultDto>(JsonOptions))!;
        var snapshot = await factory.InDb(async db => (Workflow: (await db.AgentWorkflows.SingleAsync()).FinalOutcomeJson, Steps: await db.AgentSteps.CountAsync()));
        (await client.PostAsync($"/api/assignments/{fixture.AssignmentId}/cancel", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/assignments/{fixture.AssignmentId}/validate", null)).StatusCode);
        foreach (var decision in new[] { "APPROVE", "REVISE", "REJECT" })
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/assignments/{fixture.AssignmentId}/decision", new { workflowId = review.WorkflowId, planVersion = review.PlanVersion, decision })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/assignments/{fixture.AssignmentId}/revise", new ReviseAssignmentDto(fixture.TeamId, fixture.VehicleId, SkillType.FirstAid, 2, null))).StatusCode);
        using var scope = factory.Services.CreateScope();
        var blocked = await scope.ServiceProvider.GetRequiredService<IAssignmentSafetyValidationAgent>().ValidateAsync(fixture.AssignmentId);
        Assert.Null(blocked.WorkflowId);
        var legacy = await scope.ServiceProvider.GetRequiredService<IDispatchService>().CreateAsync(new CreateDispatchDto(fixture.AssignmentId, null));
        Assert.Null(legacy.Dispatch);
        await factory.InDb(async db => {
            Assert.Equal(snapshot.Workflow, (await db.AgentWorkflows.SingleAsync()).FinalOutcomeJson);
            Assert.Equal(snapshot.Steps, await db.AgentSteps.CountAsync()); Assert.Empty(db.Dispatches);
            Assert.Equal(AssignmentStatus.Cancelled, (await db.Assignments.SingleAsync()).Status);
            return true;
        });
    }

    [Fact]
    public async Task CancellationReturnsVerifiedHelpRequestToPendingQueue()
    {
        using var factory = new ComponentDApiFactory(); var fixture = await factory.Seed();
        using var scope = factory.Services.CreateScope();
        var shared = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var help = new HelpRequest { Latitude = 7, Longitude = 80, Description = "Rescue", VerificationStatus = VerificationStatus.Verified };
        shared.HelpRequests.Add(help); await shared.SaveChangesAsync();
        await factory.InDb(async db => { var team = await db.RescueTeams.SingleAsync(); team.BaseLatitude = 7.01; team.BaseLongitude = 80; await db.SaveChangesAsync(); return true; });
        using var client = factory.Client();
        var created = (await (await client.PostAsJsonAsync("/api/assignments", fixture.Proposal() with { IncidentId = null, HelpRequestId = help.Id })).Content.ReadFromJsonAsync<AssignmentDto>(JsonOptions))!;
        var revised = (await (await client.PostAsJsonAsync($"/api/assignments/{created.Id}/revise", new ReviseAssignmentDto(fixture.TeamId, fixture.VehicleId, SkillType.FirstAid, 1, "Updated notes"))).Content.ReadFromJsonAsync<AssignmentDto>(JsonOptions))!;
        Assert.Equal(help.Id, revised.HelpRequestId); Assert.Null(revised.IncidentId); Assert.Equal(2, revised.PlanVersion);
        var assigned = await shared.HelpRequests.AsNoTracking().SingleAsync();
        (await client.PostAsync($"/api/assignments/{created.Id}/cancel", null)).EnsureSuccessStatusCode();
        Assert.Single((await client.GetFromJsonAsync<List<RescueHelpRequestDto>>("/api/rescue/help-requests"))!);
        var unchanged = await shared.HelpRequests.AsNoTracking().SingleAsync();
        Assert.Equal(HelpRequestStatus.Pending, unchanged.Status); Assert.True(unchanged.UpdatedAt > assigned.UpdatedAt); Assert.Equal(2, shared.RequestStatusHistories.Count());
        Assert.Equal(VerificationStatus.Verified, unchanged.VerificationStatus);
    }

    [Fact]
    public async Task NotesOnlyRevisionKeepsIdentityAndIncidentAndInvalidatesReview()
    {
        using var factory = new ComponentDApiFactory(); var fixture = await factory.Seed(assignment: true);
        using var client = factory.Client();
        var review = (await (await client.PostAsync($"/api/assignments/{fixture.AssignmentId}/validate", null)).Content.ReadFromJsonAsync<SafetyValidationWorkflowResultDto>(JsonOptions))!;
        var updated = (await (await client.PostAsJsonAsync($"/api/assignments/{fixture.AssignmentId}/revise", new ReviseAssignmentDto(fixture.TeamId, fixture.VehicleId, SkillType.FirstAid, 1, "New notes"))).Content.ReadFromJsonAsync<AssignmentDto>(JsonOptions))!;
        Assert.Equal(2, updated.PlanVersion); Assert.Equal(fixture.AssignmentId, updated.Id);
        Assert.Equal(fixture.IncidentId, updated.IncidentId); Assert.Null(updated.HelpRequestId);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/assignments/{fixture.AssignmentId}/decision", new { workflowId = review.WorkflowId, planVersion = 1, decision = "APPROVE" })).StatusCode);
    }

    [Fact]
    public async Task MissingAssignmentReturnsNotFound()
    {
        using var factory = new ComponentDApiFactory(); using var client = factory.Client();
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/assignments/{Guid.NewGuid()}/cancel", null)).StatusCode);
    }
}
