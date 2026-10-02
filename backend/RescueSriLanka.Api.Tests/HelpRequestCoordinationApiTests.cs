using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentB.Models;
using RescueSriLanka.Api.Features.ComponentD.DTOs;
using RescueSriLanka.Api.Features.ComponentD.Services;

namespace RescueSriLanka.Api.Tests;

public class HelpRequestCoordinationApiTests
{
    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("Citizen", HttpStatusCode.Forbidden)]
    [InlineData("EmergencyCoordinator", HttpStatusCode.Forbidden)]
    [InlineData("HelpRequestManager", HttpStatusCode.Forbidden)]
    public async Task DetailedQueueAndRecommendationRequireRescueTeam(string? role, HttpStatusCode status)
    {
        using var factory = new ComponentDApiFactory(); using var client = factory.Client(role);
        Assert.Equal(status, (await client.GetAsync("/api/rescue/help-requests")).StatusCode);
        Assert.Equal(status, (await client.PostAsJsonAsync($"/api/rescue/help-requests/{Guid.NewGuid()}/recommend-team", new { requiredSkill = "FirstAid", requiredCapacity = 2 })).StatusCode);
    }

    [Fact]
    public async Task RescueTeamReadsLimitedVerifiedQueueWithoutComponentBWriteAccess()
    {
        using var factory = new ComponentDApiFactory();
        using var scope = factory.Services.CreateScope();
        var shared = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var request = new HelpRequest { Description = "Please assist", Latitude = double.NaN, VerificationStatus = VerificationStatus.Verified };
        shared.HelpRequests.AddRange(request, new HelpRequest { Description = "Unverified" }); await shared.SaveChangesAsync();
        using var client = factory.Client();
        var response = await client.GetAsync("/api/rescue/help-requests"); response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal(request.Id, item.GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("latitude").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("estimatedPeopleCount").ValueKind);
        Assert.Equal(new[] { "createdAt", "description", "estimatedPeopleCount", "id", "latitude", "longitude", "status", "type", "urgencyScore", "verificationStatus" }, item.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/helprequests")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsJsonAsync($"/api/helprequests/{request.Id}/verify", new { isReal = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/helprequests/{request.Id}", new {
            type = 3, description = "Attempted count edit", latitude = 7, longitude = 80, estimatedPeopleCount = 5
        })).StatusCode);
        Assert.Null(shared.HelpRequests.Single(r => r.Id == request.Id).EstimatedPeopleCount);
    }

    [Fact]
    public async Task RecommendationEndpointIsReadOnlyAndValidatesRequiredInputs()
    {
        using var original = new ComponentDApiFactory();
        using var factory = original.WithWebHostBuilder(builder => builder.ConfigureServices(services => {
            services.RemoveAll<IRescueRecommendationExplanation>(); services.AddSingleton<IRescueRecommendationExplanation>(new Offline());
        }));
        using var scope = factory.Services.CreateScope();
        var shared = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var db = scope.ServiceProvider.GetRequiredService<RescueSriLanka.Api.Features.ComponentD.Data.ComponentDDbContext>();
        var request = new HelpRequest { Description = "Transport", EstimatedPeopleCount = 100, Latitude = 7, Longitude = 80, VerificationStatus = VerificationStatus.Verified };
        shared.HelpRequests.Add(request); await shared.SaveChangesAsync();
        var team = new RescueSriLanka.Api.Features.ComponentD.Models.RescueTeam { Name = "Nearest", BaseLatitude = 7.01, BaseLongitude = 80 };
        team.Members.Add(new() { FullName = "Medic", Phone = "0712345678", Skill = RescueSriLanka.Api.Features.ComponentD.Models.SkillType.FirstAid });
        team.Vehicles.Add(new() { PlateNumber = "TEST-HELP", Capacity = 4 }); db.RescueTeams.Add(team); await db.SaveChangesAsync();
        using var client = factory.CreateClient();
        using var authenticated = original.Client();
        client.DefaultRequestHeaders.Authorization = authenticated.DefaultRequestHeaders.Authorization;
        var path = $"/api/rescue/help-requests/{request.Id}/recommend-team";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path, new { })).StatusCode);
        var response = await client.PostAsJsonAsync(path, new { requiredSkill = "FirstAid", requiredCapacity = 2 });
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.GetProperty("aiAvailable").GetBoolean());
        Assert.Single(json.RootElement.GetProperty("candidates").EnumerateArray());
        Assert.Empty(db.Assignments); Assert.Empty(db.Dispatches);
        Assert.Equal(100, shared.HelpRequests.Single().EstimatedPeopleCount);
        Assert.Equal(RescueSriLanka.Api.Features.ComponentD.Models.TeamStatus.Available, team.Status);
        Assert.Equal(RescueSriLanka.Api.Features.ComponentD.Models.VehicleStatus.Available, team.Vehicles.Single().Status);
    }
    private sealed class Offline : IRescueRecommendationExplanation
    {
        public Task<RescueAiExplanation?> ExplainAsync(IReadOnlyList<RescueCandidateDto> candidates, CancellationToken ct) => throw new InvalidOperationException("Offline");
    }
}
