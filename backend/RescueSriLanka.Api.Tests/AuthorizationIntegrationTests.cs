using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace RescueSriLanka.Api.Tests;

public class AuthorizationIntegrationTests
{
    [Fact]
    public async Task ProtectedEndpointReturns401WithoutAToken()
    {
        using var factory = new ComponentDApiFactory();
        using var client = factory.Client(role: null);
        var response = await client.GetAsync("/api/dispatches");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RescueEndpointReturns403ForAuthenticatedUserWithoutRescueTeamRole()
    {
        using var factory = new ComponentDApiFactory();
        using var client = factory.Client("Responder");
        var response = await client.PostAsJsonAsync("/api/dispatches",
            new { assignmentId = Guid.NewGuid(), notes = (string?)null });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    public static IEnumerable<object?[]> DeniedDetailedRequests()
    {
        foreach (var role in new string?[] { "EmergencyCoordinator", "Citizen", null })
        foreach (var endpoint in new[] { "teams-read", "teams-create", "assignments-read", "assignments-create",
                     "validate", "decision", "dispatches-read", "dispatches-create", "status", "workflow-read", "workflow-create" })
            yield return [role, endpoint];
    }

    [Theory]
    [MemberData(nameof(DeniedDetailedRequests))]
    public async Task DetailedApisRejectOtherRolesWithoutChangingState(string? role, string endpoint)
    {
        using var factory = new ComponentDApiFactory();
        var fixture = await factory.Seed(dispatch: true);
        var before = await Snapshot(factory);
        using var client = factory.Client(role);
        var (method, path, payload) = endpoint switch
        {
            "teams-read" => ("GET", "/api/rescueteams", (object?)null),
            "teams-create" => ("POST", "/api/rescueteams", new { name = "Denied team" }),
            "assignments-read" => ("GET", "/api/assignments", null),
            "assignments-create" => ("POST", "/api/assignments", fixture.Proposal()),
            "validate" => ("POST", $"/api/assignments/{fixture.AssignmentId}/validate", null),
            "decision" => ("POST", $"/api/assignments/{fixture.AssignmentId}/decision",
                new { workflowId = Guid.NewGuid(), planVersion = 1, decision = "APPROVE" }),
            "dispatches-read" => ("GET", "/api/dispatches", null),
            "dispatches-create" => ("POST", "/api/dispatches", new { assignmentId = fixture.AssignmentId }),
            "status" => ("PATCH", $"/api/dispatches/{fixture.DispatchId}/status", new { status = "EnRoute" }),
            "workflow-read" => ("GET", $"/api/agents/workflows/{Guid.NewGuid()}", null),
            "workflow-create" => ("POST", "/api/agents/workflows",
                new { objectiveType = "Incident", objectiveId = fixture.IncidentId, requiredSkill = "FirstAid" }),
            _ => throw new ArgumentOutOfRangeException(nameof(endpoint))
        };
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (payload is not null) request.Content = JsonContent.Create(payload);
        using var response = await client.SendAsync(request);
        Assert.Equal(role is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, await Snapshot(factory));
        Assert.Equal(0, factory.Gemini.Calls);
    }

    private static Task<string> Snapshot(ComponentDApiFactory factory) => factory.InDb(async db =>
        JsonSerializer.Serialize(new
        {
            Teams = await db.RescueTeams.AsNoTracking().OrderBy(t => t.Id).ToListAsync(),
            Assignments = await db.Assignments.AsNoTracking().OrderBy(a => a.Id).ToListAsync(),
            Dispatches = await db.Dispatches.AsNoTracking().OrderBy(d => d.Id).ToListAsync(),
            Vehicles = await db.Vehicles.AsNoTracking().OrderBy(v => v.Id).ToListAsync(),
            Workflows = await db.AgentWorkflows.AsNoTracking().OrderBy(w => w.Id).ToListAsync(),
            Steps = await db.AgentSteps.AsNoTracking().OrderBy(s => s.Id).ToListAsync()
        }));

    [Theory]
    [InlineData("RescueTeam")]
    [InlineData("EmergencyCoordinator")]
    [InlineData("Citizen")]
    [InlineData(null)]
    public async Task OverviewAllowsAuthenticatedRolesAndExposesOnlySafeContract(string? role)
    {
        using var factory = new ComponentDApiFactory();
        var fixture = await factory.Seed(dispatch: true);
        using var client = factory.Client(role);
        using var response = await client.GetAsync("/api/rescue/overview");
        Assert.Equal(role is null ? HttpStatusCode.Unauthorized : HttpStatusCode.OK, response.StatusCode);
        if (role is null) return;

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        AssertProperties(root, "teamsAvailable", "teamsOnMission", "activeDispatches", "teams");
        Assert.Equal(0, root.GetProperty("teamsAvailable").GetInt32());
        Assert.Equal(1, root.GetProperty("teamsOnMission").GetInt32());
        Assert.Equal(1, root.GetProperty("activeDispatches").GetInt32());
        var team = Assert.Single(root.GetProperty("teams").EnumerateArray());
        AssertProperties(team, "name", "status", "memberCount", "availableMemberCount", "skills", "vehicles", "baseLatitude", "baseLongitude");
        Assert.Equal("Fixture Rescue", team.GetProperty("name").GetString());
        Assert.Equal(1, team.GetProperty("memberCount").GetInt32());
        var vehicle = Assert.Single(team.GetProperty("vehicles").EnumerateArray());
        AssertProperties(vehicle, "type", "count");
        Assert.Equal("Ambulance", vehicle.GetProperty("type").GetString());
        Assert.Equal(1, vehicle.GetProperty("count").GetInt32());
        foreach (var detail in new[] { "Fixture Medic", "0712345678", "TEST-01",
                     fixture.AssignmentId.ToString(), fixture.DispatchId.ToString()! })
            Assert.DoesNotContain(detail, body);
    }

    private static void AssertProperties(JsonElement value, params string[] names) =>
        Assert.Equal(names.OrderBy(n => n), value.EnumerateObject().Select(p => p.Name).OrderBy(n => n));
}
