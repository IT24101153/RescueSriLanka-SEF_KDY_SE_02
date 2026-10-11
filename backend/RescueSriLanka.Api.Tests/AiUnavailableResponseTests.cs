using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentA.Agents.IncidentAnalysisAgent;
using RescueSriLanka.Api.Features.ComponentA.Agents.IncidentEnrichmentAgent;
using RescueSriLanka.Api.Features.ComponentA.Agents.ZonePlanningAgent;
using RescueSriLanka.Api.Features.ComponentA.Controllers;
using RescueSriLanka.Api.Features.ComponentA.Models;
using RescueSriLanka.Api.Features.ComponentA.Services;
using RescueSriLanka.Api.Services.Llm;

namespace RescueSriLanka.Api.Tests;

/// <summary>
/// What a coordinator is told when the AI model cannot be used. Component A's
/// agents have no rule engine behind them, so their three endpoints answer 503
/// with the provider's own reason — "the daily quota is used up" — where the
/// dashboard shows it, instead of a result no model produced.
/// </summary>
public class AiUnavailableResponseTests
{
    private const string Quota =
        "Google AI's daily quota for this key is used up, so AI recommendations are paused until it resets.";

    /// <summary>Every Component A agent, failing the way it does when the model cannot answer.</summary>
    private sealed class FailingAgents(Exception failure)
        : IIncidentAnalysisAgent, IIncidentEnrichmentAgent, IZonePlanningAgent
    {
        public Task<IncidentAnalysisResult> AnalyseAsync(Guid incidentId, CancellationToken ct = default) =>
            Task.FromException<IncidentAnalysisResult>(failure);

        public Task<EnrichmentResult> EnrichAsync(Guid incidentId, CancellationToken ct = default) =>
            Task.FromException<EnrichmentResult>(failure);

        public Task<ZonePlanResult> PlanAsync(CancellationToken ct = default) =>
            Task.FromException<ZonePlanResult>(failure);
    }

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"ai-unavailable-{Guid.NewGuid()}")
            .Options);

    private static async Task<Guid> AddIncidentAsync(AppDbContext db)
    {
        var incident = new Incident
        {
            Title = "Flood in Kaduwela",
            Description = "Water rising.",
            Type = IncidentType.Flood,
            Latitude = 6.9271,
            Longitude = 79.8612
        };
        db.Incidents.Add(incident);
        await db.SaveChangesAsync();
        return incident.Id;
    }

    // The agent endpoints only look the incident up; nothing else is touched.
    private static IncidentsController Incidents(AppDbContext db, FailingAgents agents) =>
        new(new IncidentService(
                db,
                new SafetyZoneService(db, NullLogger<SafetyZoneService>.Instance),
                analysisQueue: null!,
                notificationQueue: null!,
                imageStorage: null!,
                NullLogger<IncidentService>.Instance),
            agents, agents, imageStorage: null!, emails: null!);

    private static void AssertSaysWhy(ActionResult? result)
    {
        var response = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, response.StatusCode);
        // ProblemDetails.detail is the field the dashboard's API client shows.
        Assert.Equal(Quota, Assert.IsType<ProblemDetails>(response.Value).Detail);
    }

    [Fact]
    public async Task Analyse_WhenTheQuotaIsUsedUp_Answers503WithTheReason()
    {
        await using var db = NewDb();
        var id = await AddIncidentAsync(db);

        var response = await Incidents(db, new FailingAgents(new LlmUnavailableException(Quota)))
            .Analyse(id, CancellationToken.None);

        AssertSaysWhy(response.Result);
    }

    [Fact]
    public async Task Enrich_WhenTheQuotaIsUsedUp_Answers503WithTheReason()
    {
        await using var db = NewDb();
        var id = await AddIncidentAsync(db);

        var response = await Incidents(db, new FailingAgents(new LlmUnavailableException(Quota)))
            .Enrich(id, CancellationToken.None);

        AssertSaysWhy(response.Result);
    }

    [Fact]
    public async Task ZonePlan_WhenTheQuotaIsUsedUp_Answers503WithTheReason()
    {
        var controller = new SafetyZonesController(
            zoneService: null!, manualZones: null!, new FailingAgents(new LlmUnavailableException(Quota)));

        var response = await controller.Plan(CancellationToken.None);

        AssertSaysWhy(response.Result);
    }

    [Fact]
    public async Task Analyse_OnAnyOtherFailure_StaysAGeneric502()
    {
        await using var db = NewDb();
        var id = await AddIncidentAsync(db);

        var response = await Incidents(db, new FailingAgents(new IOException("disk unavailable")))
            .Analyse(id, CancellationToken.None);

        // Only a model failure is explained to the caller; internals are not.
        var result = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, result.StatusCode);
        Assert.IsNotType<ProblemDetails>(result.Value);
    }
}
