using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentB.Models;
using RescueSriLanka.Api.Features.ComponentD.DTOs;
using RescueSriLanka.Api.Features.ComponentD.Models;

namespace RescueSriLanka.Api.Tests;

public sealed class HelpRequestResponseStatusApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };

    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task<(Guid RequestId, CreateAssignmentDto Proposal)> Seed(ComponentDApiFactory factory)
    {
        var fixture = await factory.Seed();
        using var scope = factory.Services.CreateScope();
        var shared = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var request = new HelpRequest
        {
            Type = HelpRequestType.Rescue,
            CitizenId = Guid.Parse(ComponentDApiFactory.CoordinatorId), Description = "Rescue needed",
            Latitude = 7, Longitude = 80, VerificationStatus = VerificationStatus.Verified,
            UpdatedAt = DateTime.UtcNow.AddDays(-1), EstimatedPeopleCount = 2
        };
        shared.HelpRequests.Add(request);
        await shared.SaveChangesAsync();
        await factory.InDb(async db =>
        {
            var team = await db.RescueTeams.SingleAsync();
            team.BaseLatitude = 7.01; team.BaseLongitude = 80;
            return await db.SaveChangesAsync();
        });
        return (request.Id, fixture.Proposal() with { IncidentId = null, HelpRequestId = request.Id });
    }

    private static async Task<(HelpRequest Request, List<RequestStatusHistory> History)> State(ComponentDApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.HelpRequests.AsNoTracking().SingleAsync(),
            await db.RequestStatusHistories.AsNoTracking().OrderBy(h => h.ChangedAt).ToListAsync());
    }

    private static async Task<CoordinatorDecisionResultDto> Dispatch(HttpClient client, Guid assignmentId)
    {
        var validation = await Read<SafetyValidationWorkflowResultDto>(await client.PostAsync($"/api/assignments/{assignmentId}/validate", null));
        Assert.Equal(10, validation.Checks.Count);
        Assert.All(validation.Checks, c => Assert.True(c.Passed));
        var decision = new CoordinatorDecisionDto(validation.WorkflowId!.Value, validation.PlanVersion!.Value,
            CoordinatorDecision.APPROVE, "Approved response");
        var result = await Read<CoordinatorDecisionResultDto>(await client.PostAsJsonAsync($"/api/assignments/{assignmentId}/decision", decision));
        Assert.True(result.Success);
        var replay = await Read<CoordinatorDecisionResultDto>(await client.PostAsJsonAsync($"/api/assignments/{assignmentId}/decision", decision));
        Assert.True(replay.Idempotent);
        return result;
    }

    [Fact]
    public async Task FullLifecyclePersistsHistoryAndBothManagerAndCitizenSeeResolved()
    {
        using var factory = new ComponentDApiFactory();
        var seed = await Seed(factory);
        using var client = factory.Client();
        var before = await State(factory);
        var assignment = await Read<AssignmentDto>(await client.PostAsJsonAsync("/api/assignments", seed.Proposal));
        var assigned = await State(factory);
        Assert.Equal(HelpRequestStatus.Assigned, assigned.Request.Status);
        Assert.True(assigned.Request.UpdatedAt > before.Request.UpdatedAt);
        Assert.Equal(HelpRequestStatus.Pending, Assert.Single(assigned.History).OldStatus);
        Assert.Equal(Guid.Parse(ComponentDApiFactory.CoordinatorId), assigned.History[0].ChangedByUserId);
        Assert.Empty(await Read<JsonElement[]>(await client.GetAsync("/api/rescue/help-requests")));
        Assert.False((await client.PostAsJsonAsync("/api/assignments", seed.Proposal)).IsSuccessStatusCode);

        var dispatch = await Dispatch(client, assignment.Id);
        var operational = await State(factory);
        Assert.Equal(HelpRequestStatus.InProgress, operational.Request.Status);
        Assert.Equal(2, operational.History.Count);
        Assert.True(operational.Request.UpdatedAt > assigned.Request.UpdatedAt);
        foreach (var status in new[] { DispatchStatus.EnRoute, DispatchStatus.OnScene })
        {
            (await client.PatchAsJsonAsync($"/api/dispatches/{dispatch.Dispatch!.Id}/status", new { newStatus = status.ToString() })).EnsureSuccessStatusCode();
            var unchanged = await State(factory);
            Assert.Equal(HelpRequestStatus.InProgress, unchanged.Request.Status);
            Assert.Equal(operational.Request.UpdatedAt, unchanged.Request.UpdatedAt);
            Assert.Equal(operational.History.Select(h => h.Id), unchanged.History.Select(h => h.Id));
        }
        (await client.PatchAsJsonAsync($"/api/dispatches/{dispatch.Dispatch!.Id}/status", new { newStatus = "Resolved" })).EnsureSuccessStatusCode();
        var resolved = await State(factory);
        Assert.Equal(HelpRequestStatus.Resolved, resolved.Request.Status);
        Assert.True(resolved.Request.UpdatedAt > operational.Request.UpdatedAt);
        Assert.Equal(new[] { HelpRequestStatus.Assigned, HelpRequestStatus.InProgress, HelpRequestStatus.Resolved }, resolved.History.Select(h => h.NewStatus));
        Assert.Equal(assigned.History[0].Id, resolved.History[0].Id);
        Assert.Empty(await Read<JsonElement[]>(await client.GetAsync("/api/rescue/help-requests")));
        using var manager = factory.Client("HelpRequestManager");
        using var citizen = factory.Client("Citizen");
        foreach (var response in new[] { await manager.GetAsync("/api/helprequests"), await citizen.GetAsync("/api/helprequests/mine") })
        {
            var request = Assert.Single(await Read<JsonElement[]>(response));
            Assert.Equal(seed.RequestId, request.GetProperty("id").GetGuid());
            Assert.Equal((int)HelpRequestStatus.Resolved, request.GetProperty("status").GetInt32());
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsJsonAsync($"/api/helprequests/{seed.RequestId}/status", new { newStatus = (int)HelpRequestStatus.Cancelled })).StatusCode);
        Assert.Equal(HelpRequestStatus.Resolved, (await State(factory)).Request.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellingResponsePreservesCitizenRequest(bool dispatchFirst)
    {
        using var factory = new ComponentDApiFactory();
        var seed = await Seed(factory);
        using var client = factory.Client();
        var assignment = await Read<AssignmentDto>(await client.PostAsJsonAsync("/api/assignments", seed.Proposal));
        Guid? dispatchId = dispatchFirst ? (await Dispatch(client, assignment.Id)).Dispatch!.Id : null;
        var before = await State(factory);
        var response = dispatchFirst
            ? await client.PatchAsJsonAsync($"/api/dispatches/{dispatchId}/status", new { newStatus = "Cancelled" })
            : await client.PostAsync($"/api/assignments/{assignment.Id}/cancel", null);
        response.EnsureSuccessStatusCode();
        var after = await State(factory);
        Assert.Equal(dispatchFirst ? HelpRequestStatus.InProgress : HelpRequestStatus.Pending, after.Request.Status);
        Assert.Equal(VerificationStatus.Verified, after.Request.VerificationStatus);
        if (dispatchFirst)
        {
            Assert.Equal(before.Request.UpdatedAt, after.Request.UpdatedAt);
            Assert.Equal(before.History.Select(h => h.Id), after.History.Select(h => h.Id));
            Assert.Empty(await Read<JsonElement[]>(await client.GetAsync("/api/rescue/help-requests")));
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/assignments/{assignment.Id}/cancel", null)).StatusCode);
        }
        else
        {
            Assert.True(after.Request.UpdatedAt > before.Request.UpdatedAt);
            Assert.Equal(2, after.History.Count);
            Assert.Equal(before.History[0].Id, after.History[0].Id);
            Assert.Equal(HelpRequestStatus.Assigned, after.History[1].OldStatus);
            Assert.Equal(HelpRequestStatus.Pending, after.History[1].NewStatus);
            Assert.Contains(assignment.Id.ToString(), after.History[1].Notes);
            Assert.Equal(Guid.Parse(ComponentDApiFactory.CoordinatorId), after.History[1].ChangedByUserId);
            Assert.Equal(AssignmentStatus.Cancelled, (await factory.InDb(db => db.Assignments.SingleAsync())).Status);
            Assert.Single(await Read<JsonElement[]>(await client.GetAsync("/api/rescue/help-requests")));
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/assignments/{assignment.Id}/cancel", null)).StatusCode);
            Assert.Equal(2, (await State(factory)).History.Count);
            using var manager = factory.Client("HelpRequestManager");
            using var citizen = factory.Client("Citizen");
            foreach (var result in new[] { await manager.GetAsync("/api/helprequests"), await citizen.GetAsync("/api/helprequests/mine") })
                Assert.Equal((int)HelpRequestStatus.Pending, Assert.Single(await Read<JsonElement[]>(result)).GetProperty("status").GetInt32());
            // A new response using both the same team and vehicle proves their
            // reservations were released with the cancelled assignment.
            var replacement = await Read<AssignmentDto>(await client.PostAsJsonAsync("/api/assignments", seed.Proposal));
            Assert.NotEqual(assignment.Id, replacement.Id);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/assignments/{assignment.Id}/cancel", null)).StatusCode);
            Assert.Equal(HelpRequestStatus.Assigned, (await State(factory)).Request.Status);
            Assert.False((await client.PostAsJsonAsync("/api/assignments", seed.Proposal)).IsSuccessStatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await manager.PatchAsJsonAsync($"/api/helprequests/{seed.RequestId}/status",
                new { newStatus = (int)HelpRequestStatus.Pending })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsJsonAsync($"/api/helprequests/{seed.RequestId}/status",
                new { newStatus = (int)HelpRequestStatus.Pending })).StatusCode);
            Assert.Equal(HelpRequestStatus.Assigned, (await State(factory)).Request.Status);
        }
    }

    [Theory]
    [InlineData(DispatchStatus.Pending)]
    [InlineData(DispatchStatus.Dispatched)]
    [InlineData(DispatchStatus.EnRoute)]
    [InlineData(DispatchStatus.OnScene)]
    [InlineData(DispatchStatus.Resolved)]
    [InlineData(DispatchStatus.Cancelled)]
    public async Task AnyExistingDispatchBlocksAssignmentCancellationAndRequestReset(DispatchStatus dispatchStatus)
    {
        using var factory = new ComponentDApiFactory();
        var seed = await Seed(factory);
        using var client = factory.Client();
        var assignment = await Read<AssignmentDto>(await client.PostAsJsonAsync("/api/assignments", seed.Proposal));
        await factory.InDb(async db =>
        {
            db.Dispatches.Add(new Dispatch { AssignmentId = assignment.Id, Status = dispatchStatus });
            return await db.SaveChangesAsync();
        });
        var before = await State(factory);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/assignments/{assignment.Id}/cancel", null)).StatusCode);
        var after = await State(factory);
        Assert.Equal(HelpRequestStatus.Assigned, after.Request.Status);
        Assert.Equal(before.Request.UpdatedAt, after.Request.UpdatedAt);
        Assert.Equal(before.History.Select(h => h.Id), after.History.Select(h => h.Id));
        Assert.Equal(AssignmentStatus.Proposed, (await factory.InDb(db => db.Assignments.SingleAsync())).Status);
    }
}
