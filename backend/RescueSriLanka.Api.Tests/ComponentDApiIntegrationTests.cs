using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Features.ComponentD.DTOs;
using RescueSriLanka.Api.Features.ComponentD.Models;

namespace RescueSriLanka.Api.Tests;

public class ComponentDApiIntegrationTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static async Task<T> Body<T>(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    [Theory]
    [InlineData("rescueteams")]
    [InlineData("assignments")]
    [InlineData("dispatches")]
    public async Task RescueTeamReadsReturnFixture(string resource)
    {
        using var factory = new ComponentDApiFactory();
        var fixture = await factory.Seed(dispatch: true);
        using var client = factory.Client();
        var rows = await Body<JsonElement[]>(await client.GetAsync("/api/" + resource), HttpStatusCode.OK);
        var id = resource switch { "rescueteams" => fixture.TeamId, "assignments" => fixture.AssignmentId, _ => fixture.DispatchId!.Value };
        Assert.Equal(id, Assert.Single(rows).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task RescueTeamCreatesTeamWithLocationAndPersistedRow()
    {
        using var factory = new ComponentDApiFactory();
        using var client = factory.Client();
        var response = await client.PostAsJsonAsync("/api/rescueteams", new CreateRescueTeamDto("HTTP Team", 7, 80));
        var team = await Body<RescueTeamDto>(response, HttpStatusCode.Created);
        Assert.Equal("HTTP Team", team.Name);
        Assert.Equal(TeamStatus.Available, team.Status);
        Assert.NotEqual(Guid.Empty, team.Id);
        Assert.NotNull(response.Headers.Location);
        var fetched = await Body<RescueTeamDto>(await client.GetAsync(response.Headers.Location), HttpStatusCode.OK);
        Assert.Equal(team.Id, fetched.Id);
        var row = await factory.InDb(db => db.RescueTeams.SingleAsync());
        Assert.Equal(team.Id, row.Id);
        Assert.Equal(team.Name, row.Name);
        Assert.Equal(7d, row.BaseLatitude);
        Assert.Equal(80d, row.BaseLongitude);
    }

    [Fact]
    public async Task UnauthenticatedWriteIs401AndDoesNotInsert()
    {
        using var factory = new ComponentDApiFactory();
        using var client = factory.Client(null);
        var response = await client.PostAsJsonAsync("/api/rescueteams", new CreateRescueTeamDto("Denied", null, null));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, await factory.InDb(db => db.RescueTeams.CountAsync()));
    }

    [Fact]
    public async Task EmergencyCoordinatorCannotManageComponentD()
    {
        using var factory = new ComponentDApiFactory();
        using var client = factory.Client("EmergencyCoordinator");
        var response = await client.PostAsJsonAsync("/api/rescueteams", new CreateRescueTeamDto("Denied", null, null));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await factory.InDb(db => db.RescueTeams.CountAsync()));
    }

    private static async Task ValidationError(HttpResponseMessage response)
    {
        var problem = await Body<JsonElement>(response, HttpStatusCode.BadRequest);
        Assert.NotEmpty(problem.GetProperty("errors").EnumerateObject());
    }

    [Fact]
    public async Task RescueTeamStartsWorkflowWithFakeProvidersWithoutDispatching()
    {
        using var factory = new ComponentDApiFactory();
        var fixture = await factory.Seed();
        using var client = factory.Client();
        var workflow = await Body<AgentWorkflowDto>(await client.PostAsJsonAsync("/api/agents/workflows",
            new { objectiveType = "Incident", objectiveId = fixture.IncidentId, requiredSkill = "FirstAid" }),
            HttpStatusCode.Created);
        Assert.Equal(WorkflowStatus.AwaitingApproval, workflow.Status);
        Assert.Equal(fixture.IncidentId, workflow.ObjectiveId);
        Assert.Equal(3, workflow.Steps.Count);
        Assert.All(workflow.Steps, step => Assert.Equal(StepStatus.Completed, step.Status));
        Assert.Equal(1, factory.Gemini.Calls);
        Assert.Equal(1, await factory.InDb(db => db.Assignments.CountAsync()));
        Assert.Equal(0, await factory.InDb(db => db.Dispatches.CountAsync()));
        var fetched = await Body<AgentWorkflowDto>(await client.GetAsync($"/api/agents/workflows/{workflow.Id}"), HttpStatusCode.OK);
        Assert.Equal(workflow.Id, fetched.Id);
    }

    [Fact]
    public async Task EmptyTeamNameIs400WithoutInsertion()
    {
        using var factory = new ComponentDApiFactory();
        using var client = factory.Client();
        await ValidationError(await client.PostAsJsonAsync("/api/rescueteams", new CreateRescueTeamDto("", null, null)));
        Assert.Equal(0, await factory.InDb(db => db.RescueTeams.CountAsync()));
    }

    [Fact]
    public async Task PhoneWithLettersIs400WithoutInsertion()
    {
        using var factory = new ComponentDApiFactory();
        var fixture = await factory.Seed();
        using var client = factory.Client();
        await ValidationError(await client.PostAsJsonAsync($"/api/rescueteams/{fixture.TeamId}/members",
            new CreateTeamMemberDto("Invalid member", "07664abc12", SkillType.FirstAid)));
        Assert.Equal(1, await factory.InDb(db => db.TeamMembers.CountAsync()));
        Assert.False(await factory.InDb(db => db.TeamMembers.AnyAsync(m => m.FullName == "Invalid member")));
    }

    [Fact]
    public async Task ZeroVehicleCapacityIs400WithoutInsertion()
    {
        using var factory = new ComponentDApiFactory();
        var fixture = await factory.Seed();
        using var client = factory.Client();
        await ValidationError(await client.PostAsJsonAsync($"/api/rescueteams/{fixture.TeamId}/vehicles",
            new CreateVehicleDto("INVALID", VehicleType.Ambulance, 0)));
        Assert.Equal(1, await factory.InDb(db => db.Vehicles.CountAsync()));
        Assert.False(await factory.InDb(db => db.Vehicles.AnyAsync(v => v.PlateNumber == "INVALID")));
    }

    [Fact]
    public async Task ZeroAssignmentCapacityIs400WithoutInsertion()
    {
        using var factory = new ComponentDApiFactory();
        var fixture = await factory.Seed();
        using var client = factory.Client();
        await ValidationError(await client.PostAsJsonAsync("/api/assignments", fixture.Proposal(0)));
        Assert.Equal(0, await factory.InDb(db => db.Assignments.CountAsync()));
    }

    [Fact]
    public async Task MissingTeamIs404()
    {
        using var factory = new ComponentDApiFactory();
        using var client = factory.Client();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/rescueteams/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task AssignedTeamDeletionIs409AndRetainsTeam()
    {
        using var factory = new ComponentDApiFactory();
        var fixture = await factory.Seed(assignment: true);
        using var client = factory.Client();
        var problem = await Body<JsonElement>(await client.DeleteAsync($"/api/rescueteams/{fixture.TeamId}"), HttpStatusCode.Conflict);
        Assert.Contains("assignments", problem.GetProperty("error").GetString());
        Assert.True(await factory.InDb(db => db.RescueTeams.AnyAsync(t => t.Id == fixture.TeamId)));
        Assert.Equal(1, await factory.InDb(db => db.Assignments.CountAsync()));
    }

    [Fact]
    public async Task AssignmentCreationPersistsProposalBindings()
    {
        using var factory = new ComponentDApiFactory();
        var fixture = await factory.Seed();
        using var client = factory.Client();
        var dto = await Body<AssignmentDto>(await client.PostAsJsonAsync("/api/assignments", fixture.Proposal(2)), HttpStatusCode.Created);
        Assert.Equal(fixture.TeamId, dto.RescueTeamId);
        Assert.Equal(fixture.VehicleId, dto.VehicleId);
        Assert.Equal(SkillType.FirstAid, dto.RequiredSkill);
        Assert.Equal(2, dto.RequiredCapacity);
        Assert.Equal(AssignmentStatus.Proposed, dto.Status);
        Assert.Equal(1, dto.PlanVersion);
        var row = await factory.InDb(db => db.Assignments.SingleAsync());
        Assert.Equal(dto.Id, row.Id);
        Assert.Equal(dto.RescueTeamId, row.RescueTeamId);
        Assert.Equal(dto.VehicleId, row.VehicleId);
        Assert.Equal(dto.RequiredSkill, row.RequiredSkill);
        Assert.Equal(dto.RequiredCapacity, row.RequiredCapacity);
        Assert.Equal(dto.Status, row.Status);
        Assert.Equal(dto.PlanVersion, row.PlanVersion);
        Assert.Equal(fixture.IncidentId, row.IncidentId);
    }

    [Fact]
    public async Task SafetyRelevantRevisionIncrementsPlanVersion()
    {
        using var factory = new ComponentDApiFactory();
        var fixture = await factory.Seed(assignment: true);
        using var client = factory.Client();
        var dto = await Body<AssignmentDto>(await client.PostAsJsonAsync($"/api/assignments/{fixture.AssignmentId}/revise",
            new ReviseAssignmentDto(fixture.TeamId, fixture.VehicleId, SkillType.FirstAid, 2, "Revised")), HttpStatusCode.OK);
        Assert.Equal(2, dto.PlanVersion);
        Assert.Equal(2, dto.RequiredCapacity);
        var row = await factory.InDb(db => db.Assignments.SingleAsync());
        Assert.Equal(2, row.PlanVersion);
        Assert.Equal(2, row.RequiredCapacity);
        Assert.Equal("Revised", row.Notes);
    }

    [Fact]
    public async Task MatchingRanksEligibleTeamsDeterministically()
    {
        using var factory = new ComponentDApiFactory();
        var weaker = await factory.Seed();
        var stronger = await factory.Seed();
        await factory.InDb(async db =>
        {
            db.TeamMembers.Add(new TeamMember { RescueTeamId = stronger.TeamId, FullName = "Second medic", Phone = "0712345679", Skill = SkillType.FirstAid });
            db.RescueTeams.Add(new RescueTeam { Name = "Ineligible", Status = TeamStatus.OffDuty });
            return await db.SaveChangesAsync();
        });
        using var client = factory.Client();
        var rows = await Body<List<TeamMatchResultDto>>(await client.PostAsJsonAsync("/api/assignments/match",
            new MatchRequestDto(SkillType.FirstAid, null, null, 2)), HttpStatusCode.OK);
        Assert.Equal(new[] { stronger.TeamId, weaker.TeamId }, rows.Select(r => r.RescueTeamId));
        Assert.True(rows[0].Score > rows[1].Score);
    }

    private static async Task<SafetyValidationWorkflowResultDto> Validate(HttpClient client, Guid id)
        => await Body<SafetyValidationWorkflowResultDto>(
            await client.PostAsync($"/api/assignments/{id}/validate", null), HttpStatusCode.OK);

    [Fact]
    public async Task ValidationUsesFakeProviderAndPersistsRealChecksWithoutDispatch()
    {
        using var factory = new ComponentDApiFactory();
        var fixture = await factory.Seed(assignment: true);
        using var client = factory.Client();
        var result = await Validate(client, fixture.AssignmentId);
        Assert.NotNull(result.WorkflowId);
        Assert.Equal(fixture.AssignmentId, result.AssignmentId);
        Assert.Equal(1, result.PlanVersion);
        Assert.Equal(SafetyValidationDecision.APPROVE, result.Decision);
        Assert.False(result.IsStale);
        Assert.Empty(result.FailedChecks);
        Assert.Equal(10, result.Checks.Count);
        Assert.All(result.Checks, c => Assert.True(c.Passed));
        Assert.Equal(1, factory.Gemini.Calls);
        var workflow = await factory.InDb(db => db.AgentWorkflows.SingleAsync());
        Assert.Equal(result.WorkflowId, workflow.Id);
        var fetchedWorkflow = await Body<AgentWorkflowDto>(
            await client.GetAsync($"/api/agents/workflows/{workflow.Id}"), HttpStatusCode.OK);
        Assert.Equal(workflow.Id, fetchedWorkflow.Id);
        Assert.Equal(WorkflowStatus.AwaitingApproval, workflow.Status);
        var persisted = JsonSerializer.Deserialize<SafetyValidationWorkflowResultDto>(workflow.FinalOutcomeJson!, Json)!;
        Assert.Equal(result.AssignmentId, persisted.AssignmentId);
        Assert.Equal(result.Decision, persisted.Decision);
        var steps = await factory.InDb(db => db.AgentSteps.Where(s => s.AgentWorkflowId == workflow.Id).ToListAsync());
        Assert.Equal(10, steps.Count);
        Assert.All(steps, s => { Assert.Equal(StepStatus.Completed, s.Status); Assert.NotNull(s.ValidationResultJson); });
        Assert.Equal(0, await factory.InDb(db => db.Dispatches.CountAsync()));
        Assert.Equal(AssignmentStatus.Proposed, (await factory.InDb(db => db.Assignments.SingleAsync())).Status);
    }

    private static async Task<CoordinatorDecisionResultDto> Approve(HttpClient client, Guid assignmentId, SafetyValidationWorkflowResultDto validation)
        => await Body<CoordinatorDecisionResultDto>(await client.PostAsJsonAsync($"/api/assignments/{assignmentId}/decision",
            new CoordinatorDecisionDto(validation.WorkflowId!.Value, validation.PlanVersion!.Value, CoordinatorDecision.APPROVE, "HTTP approval")),
            HttpStatusCode.OK);

    [Fact]
    public async Task HumanApprovalCommitsReservationsAndIsIdempotent()
    {
        using var factory = new ComponentDApiFactory();
        var fixture = await factory.Seed(assignment: true);
        using var client = factory.Client();
        var validation = await Validate(client, fixture.AssignmentId);
        var result = await Approve(client, fixture.AssignmentId, validation);
        Assert.True(result.Success);
        Assert.False(result.Idempotent);
        Assert.NotNull(result.Dispatch);
        var repeated = await Approve(client, fixture.AssignmentId, validation);
        Assert.True(repeated.Success);
        Assert.True(repeated.Idempotent);
        Assert.Equal(result.Dispatch.Id, repeated.Dispatch!.Id);
        var dispatch = await factory.InDb(db => db.Dispatches.SingleAsync());
        Assert.Equal(result.Dispatch.Id, dispatch.Id);
        Assert.Equal(fixture.AssignmentId, dispatch.AssignmentId);
        Assert.Equal(ComponentDApiFactory.CoordinatorId, dispatch.ApprovedByUserId);
        Assert.Equal(DispatchStatus.Dispatched, dispatch.Status);
        Assert.Equal(AssignmentStatus.Approved, (await factory.InDb(db => db.Assignments.SingleAsync())).Status);
        Assert.Equal(TeamStatus.OnMission, (await factory.InDb(db => db.RescueTeams.SingleAsync())).Status);
        Assert.Equal(VehicleStatus.InUse, (await factory.InDb(db => db.Vehicles.SingleAsync())).Status);
        Assert.Equal(WorkflowStatus.Executing, (await factory.InDb(db => db.AgentWorkflows.SingleAsync())).Status);
    }

    [Fact]
    public async Task StalePlanDecisionIs409WithoutReservations()
    {
        using var factory = new ComponentDApiFactory();
        var fixture = await factory.Seed(assignment: true);
        using var client = factory.Client();
        var validation = await Validate(client, fixture.AssignmentId);
        await Body<AssignmentDto>(await client.PostAsJsonAsync($"/api/assignments/{fixture.AssignmentId}/revise",
            new ReviseAssignmentDto(fixture.TeamId, fixture.VehicleId, SkillType.FirstAid, 2, null)), HttpStatusCode.OK);
        var result = await Body<CoordinatorDecisionResultDto>(await client.PostAsJsonAsync($"/api/assignments/{fixture.AssignmentId}/decision",
            new CoordinatorDecisionDto(validation.WorkflowId!.Value, 1, CoordinatorDecision.APPROVE, null)), HttpStatusCode.Conflict);
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Equal(0, await factory.InDb(db => db.Dispatches.CountAsync()));
        Assert.Equal(TeamStatus.Available, (await factory.InDb(db => db.RescueTeams.SingleAsync())).Status);
        Assert.Equal(VehicleStatus.Available, (await factory.InDb(db => db.Vehicles.SingleAsync())).Status);
        Assert.Equal(AssignmentStatus.Proposed, (await factory.InDb(db => db.Assignments.SingleAsync())).Status);
    }

    [Fact]
    public async Task RescueTeamLegalLifecycleReleasesResources()
    {
        using var factory = new ComponentDApiFactory();
        var fixture = await factory.Seed(assignment: true);
        using var client = factory.Client();
        var validation = await Validate(client, fixture.AssignmentId);
        var approved = await Approve(client, fixture.AssignmentId, validation);
        var path = $"/api/dispatches/{approved.Dispatch!.Id}/status";
        foreach (var state in new[] { DispatchStatus.EnRoute, DispatchStatus.OnScene, DispatchStatus.Resolved })
        {
            var dto = await Body<DispatchDto>(await client.PatchAsJsonAsync(path, new TransitionDispatchStatusDto(state, null)), HttpStatusCode.OK);
            Assert.Equal(state, dto.Status);
            Assert.NotNull(state switch { DispatchStatus.EnRoute => dto.EnRouteAt, DispatchStatus.OnScene => dto.OnSceneAt, _ => dto.ResolvedAt });
        }
        var row = await factory.InDb(db => db.Dispatches.SingleAsync());
        Assert.Equal(DispatchStatus.Resolved, row.Status);
        Assert.NotNull(row.DispatchedAt);
        Assert.NotNull(row.EnRouteAt);
        Assert.NotNull(row.OnSceneAt);
        Assert.NotNull(row.ResolvedAt);
        Assert.True(row.DispatchedAt <= row.EnRouteAt && row.EnRouteAt <= row.OnSceneAt && row.OnSceneAt <= row.ResolvedAt);
        Assert.Equal(TeamStatus.Available, (await factory.InDb(db => db.RescueTeams.SingleAsync())).Status);
        Assert.Equal(VehicleStatus.Available, (await factory.InDb(db => db.Vehicles.SingleAsync())).Status);
        Assert.Equal(WorkflowStatus.Completed, (await factory.InDb(db => db.AgentWorkflows.SingleAsync())).Status);
    }

    [Fact]
    public async Task IllegalLifecycleJumpIs400AndLeavesStateUnchanged()
    {
        using var factory = new ComponentDApiFactory();
        var fixture = await factory.Seed(dispatch: true);
        var before = await factory.InDb(db => db.Dispatches.SingleAsync());
        using var client = factory.Client("RescueTeam");
        var error = await Body<JsonElement>(await client.PatchAsJsonAsync($"/api/dispatches/{fixture.DispatchId}/status",
            new TransitionDispatchStatusDto(DispatchStatus.Resolved, null)), HttpStatusCode.BadRequest);
        Assert.Contains("Cannot transition", error.GetProperty("error").GetString());
        var after = await factory.InDb(db => db.Dispatches.SingleAsync());
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        Assert.Equal(before.DispatchedAt, after.DispatchedAt);
        Assert.Null(after.ResolvedAt);
        Assert.Equal(TeamStatus.OnMission, (await factory.InDb(db => db.RescueTeams.SingleAsync())).Status);
        Assert.Equal(VehicleStatus.InUse, (await factory.InDb(db => db.Vehicles.SingleAsync())).Status);
    }
}
