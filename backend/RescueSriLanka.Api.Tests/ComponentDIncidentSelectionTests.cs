using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentA.DTOs;
using RescueSriLanka.Api.Features.ComponentA.Models;

namespace RescueSriLanka.Api.Tests;

public class ComponentDIncidentSelectionTests
{
    [Fact]
    public async Task RescueTeamCanReadActiveReportedIncidentsButCannotManageTheirStatus()
    {
        using var factory = new ComponentDApiFactory();
        var fixture = await factory.Seed();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Incidents.Add(new Incident { Title = "Closed", Description = "Closed incident", IsActive = false, Status = IncidentStatus.Resolved });
            await db.SaveChangesAsync();
        }
        using var client = factory.Client();
        var incidents = await client.GetFromJsonAsync<List<IncidentDto>>("/api/incidents?activeOnly=true");
        var incident = Assert.Single(incidents!);
        Assert.Equal(fixture.IncidentId, incident.Id);
        Assert.Equal("Reported", incident.Status);
        Assert.Equal("Isolated fixture", incident.Title);
        var response = await client.PatchAsJsonAsync($"/api/incidents/{incident.Id}/status", new { status = "Verified" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateReturnsClearValidationErrorWhenSelectedIncidentCloses()
    {
        using var factory = new ComponentDApiFactory();
        var fixture = await factory.Seed();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var incident = await db.Incidents.FindAsync(fixture.IncidentId);
            incident!.IsActive = false;
            incident.Status = IncidentStatus.Resolved;
            await db.SaveChangesAsync();
        }
        using var client = factory.Client();
        var response = await client.PostAsJsonAsync("/api/assignments", fixture.Proposal());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Selected incident is no longer active", await response.Content.ReadAsStringAsync());
        await factory.InDb(db => { Assert.Empty(db.Assignments); return Task.FromResult(true); });
    }
}
