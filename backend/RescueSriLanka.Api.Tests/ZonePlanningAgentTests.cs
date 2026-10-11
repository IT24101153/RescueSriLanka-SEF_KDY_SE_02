using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentA.Agents.IncidentAnalysisAgent;
using RescueSriLanka.Api.Features.ComponentA.Agents.ZonePlanningAgent;
using RescueSriLanka.Api.Features.ComponentA.Models;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Services.Llm;

namespace RescueSriLanka.Api.Tests;

/// <summary>
/// Evaluation of the Zone Planning Agent (Component A): clustering, what the
/// model is shown, validation of its plan, and that it never changes the map
/// by itself. The plan comes from the model or not at all: there is no rule
/// engine, so a model that cannot answer fails the run with the reason.
/// </summary>
public class ZonePlanningAgentTests
{
    private sealed class FakeLlm(Func<string> respond, bool configured = true) : ILlmClient
    {
        public string? LastPrompt { get; private set; }
        public int Calls { get; private set; }
        public bool IsConfigured => configured;
        public string ModelName => "fake-model";

        public Task<string> GenerateAsync(
            string systemInstruction, string prompt, object? jsonSchema = null,
            IReadOnlyList<LlmImage>? images = null, CancellationToken ct = default)
        {
            Calls++;
            LastPrompt = prompt;
            return Task.FromResult(respond());
        }
    }

    private sealed class Rain(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"zoneplan-{Guid.NewGuid()}")
            .Options);

    private static ZonePlanningAgent NewAgent(AppDbContext db, ILlmClient llm, double rainMm = 0) =>
        new(db, llm,
            new IncidentAnalysisTools(db,
                new HttpClient(new Rain($$"""{ "hourly": { "precipitation": [{{rainMm}}] } }"""))
                {
                    BaseAddress = new Uri("https://api.open-meteo.com/")
                },
                NullLogger<IncidentAnalysisTools>.Instance),
            NullLogger<ZonePlanningAgent>.Instance);

    private static Incident AddIncident(
        AppDbContext db, double latitude, double longitude,
        IncidentSeverity severity = IncidentSeverity.High,
        IncidentStatus status = IncidentStatus.Verified,
        IncidentType type = IncidentType.Flood)
    {
        var incident = new Incident
        {
            Title = $"Flood at {latitude:F3}",
            Description = "Water rising.",
            Type = type,
            Severity = severity,
            Status = status,
            Latitude = latitude,
            Longitude = longitude,
            AffectedRadiusMeters = 1000,
            District = "Colombo",
            EstimatedAffectedPeople = 100
        };
        db.Incidents.Add(incident);
        return incident;
    }

    [Fact]
    public async Task GoldenCase_ModelDrawsOneAreaZoneOverAClusterOfApprovedIncidents()
    {
        await using var db = NewDb();
        var a = AddIncident(db, 6.930, 79.860);
        var b = AddIncident(db, 6.950, 79.870, IncidentSeverity.Moderate);
        var unapproved = AddIncident(db, 6.940, 79.865, status: IncidentStatus.Reported);
        var alone = AddIncident(db, 7.290, 80.630); // Kandy: no cluster
        await db.SaveChangesAsync();

        var llm = new FakeLlm(() => $$"""
            {
              "zones": [
                { "action": "create", "name": "Colombo flood area", "status": "Danger", "centerLatitude": 6.94,
                  "centerLongitude": 79.865, "radiusMeters": 3000, "expiresInHours": 72,
                  "rationale": "Two approved floods 2 km apart after 150 mm of rain.",
                  "basedOnIncidentIds": ["{{a.Id}}", "{{b.Id}}"] }
              ],
              "summary": "One area zone over the Colombo floods."
            }
            """);

        var result = await NewAgent(db, llm, rainMm: 150).PlanAsync();

        var zone = Assert.Single(result.Zones);
        Assert.Equal(ZoneActions.Create, zone.Action);
        Assert.Equal(ZoneStatus.Danger, zone.Status);
        Assert.Equal(new[] { a.Id, b.Id }.Order(), zone.BasedOnIncidentIds.Order());
        Assert.Equal(3000, zone.RadiusMeters);
        Assert.Equal(72, zone.ExpiresInHours);

        // The model is shown the approved cluster and its rainfall, and nothing else.
        Assert.Contains(a.Id.ToString(), llm.LastPrompt);
        Assert.Contains(b.Id.ToString(), llm.LastPrompt);
        Assert.Contains("150 mm", llm.LastPrompt);
        Assert.DoesNotContain(unapproved.Id.ToString(), llm.LastPrompt);
        Assert.DoesNotContain(alone.Id.ToString(), llm.LastPrompt);

        var run = await db.AgentRuns.SingleAsync();
        Assert.Equal(ZonePlanningAgent.AgentName, run.AgentName);
        Assert.Equal(AgentRunStatus.Succeeded, run.Status);
        Assert.Equal("fake-model", run.Model);
        Assert.False(run.UsedFallback);
        Assert.Null(run.IncidentId);

        // Proposes only: the map has no manual zone yet.
        Assert.Empty(db.SafetyZones.Where(z => z.Source == ZoneSource.ManualOverride));
    }

    [Fact]
    public async Task Model_CanRetireAManualZoneItWasShown()
    {
        await using var db = NewDb();
        var stale = new SafetyZone
        {
            Name = "Old closure",
            Status = ZoneStatus.Caution,
            Source = ZoneSource.ManualOverride,
            CenterLatitude = 8.3,
            CenterLongitude = 80.4,
            RadiusMeters = 1000,
            ComputedAt = DateTime.UtcNow.AddDays(-2)
        };
        db.SafetyZones.Add(stale);
        await db.SaveChangesAsync();

        var llm = new FakeLlm(() => $$"""
            {
              "zones": [ { "action": "retire", "zoneId": "{{stale.Id}}", "rationale": "No active incident inside it." } ],
              "summary": "Retire the old closure."
            }
            """);

        var result = await NewAgent(db, llm).PlanAsync();

        var retire = Assert.Single(result.Zones);
        Assert.Equal(ZoneActions.Retire, retire.Action);
        Assert.Equal(stale.Id, retire.ZoneId);
        Assert.Contains("active incidents inside 0", llm.LastPrompt);
        // Proposes only: the zone is still on the map.
        Assert.True((await db.SafetyZones.SingleAsync()).IsActive);
    }

    // ---------- model failure: nothing stands in for the model ----------

    [Fact]
    public async Task QuotaUsedUp_FailsTheRun_AndRecordsTheProvidersReason()
    {
        const string quota =
            "Google AI's daily quota for this key is used up, so AI recommendations are paused until it resets.";

        await using var db = NewDb();
        AddIncident(db, 6.930, 79.860);
        AddIncident(db, 6.950, 79.870);
        await db.SaveChangesAsync();
        var llm = new FakeLlm(() => throw new LlmUnavailableException(quota));

        var error = await Assert.ThrowsAsync<LlmUnavailableException>(() => NewAgent(db, llm).PlanAsync());

        // A cluster rules could have drawn a zone over, and no zone is proposed.
        Assert.Equal(quota, error.Message);
        var run = await db.AgentRuns.SingleAsync();
        Assert.Equal(AgentRunStatus.Failed, run.Status);
        Assert.Equal(quota, run.ErrorMessage);
        Assert.Null(run.OutputJson);
        Assert.False(run.UsedFallback);
    }

    [Fact]
    public async Task NoModelConfigured_FailsTheRun_WithoutCallingTheModel()
    {
        await using var db = NewDb();
        AddIncident(db, 6.930, 79.860);
        AddIncident(db, 6.950, 79.870);
        await db.SaveChangesAsync();
        var llm = new FakeLlm(() => "{}", configured: false);

        await Assert.ThrowsAsync<LlmUnavailableException>(() => NewAgent(db, llm).PlanAsync());

        Assert.Equal(0, llm.Calls);
        var run = await db.AgentRuns.SingleAsync();
        Assert.Equal(AgentRunStatus.Failed, run.Status);
        Assert.Contains("No AI model is configured", run.ErrorMessage);
    }

    [Fact]
    public async Task Validator_RejectsZonesOffTheIsland_UnknownIds_AndUnfoundedDangerZones()
    {
        await using var db = NewDb();
        var a = AddIncident(db, 6.930, 79.860);
        AddIncident(db, 6.950, 79.870);
        await db.SaveChangesAsync();

        var llm = new FakeLlm(() => $$"""
            {
              "zones": [
                { "action": "create", "name": "Sea", "status": "Danger", "centerLatitude": 1.0, "centerLongitude": 70.0,
                  "radiusMeters": 1000, "rationale": "Off the island.", "basedOnIncidentIds": ["{{a.Id}}"] },
                { "action": "create", "name": "Invented", "status": "Danger", "centerLatitude": 6.94, "centerLongitude": 79.86,
                  "radiusMeters": 3000, "rationale": "No real incident.", "basedOnIncidentIds": ["{{Guid.NewGuid()}}"] },
                { "action": "retire", "zoneId": "{{Guid.NewGuid()}}", "rationale": "Not a zone." },
                { "action": "create", "name": "Colombo flood area", "status": "Danger", "centerLatitude": 6.94,
                  "centerLongitude": 79.865, "radiusMeters": 999999, "expiresInHours": 9999,
                  "rationale": "Two floods.", "basedOnIncidentIds": ["{{a.Id}}"] }
              ],
              "summary": "Mixed plan."
            }
            """);

        var result = await NewAgent(db, llm).PlanAsync();

        var zone = Assert.Single(result.Zones);
        Assert.Equal("Colombo flood area", zone.Name);
        Assert.Equal(ZonePlanningRules.MaxRadiusMeters, zone.RadiusMeters);
        Assert.Equal(ZonePlanningRules.MaxExpiryHours, zone.ExpiresInHours);

        var run = await db.AgentRuns.SingleAsync();
        Assert.Equal(AgentRunStatus.Succeeded, run.Status);
        Assert.Contains("outside Sri Lanka", run.PlanJson);
        Assert.Contains("must rest on an approved incident", run.PlanJson);
        Assert.Contains("not an existing manual zone", run.PlanJson);
    }

    [Fact]
    public async Task NothingToPlan_DoesNotCallTheModel()
    {
        await using var db = NewDb();
        var llm = new FakeLlm(() => "{}");

        var result = await NewAgent(db, llm).PlanAsync();

        Assert.Empty(result.Zones);
        Assert.Equal(0, llm.Calls);
        var run = await db.AgentRuns.SingleAsync();
        Assert.Equal(AgentRunStatus.Succeeded, run.Status);
        Assert.Null(run.Model);
        Assert.False(run.UsedFallback);
        Assert.Contains("Clustering skipped", run.PlanJson);
    }

    [Fact]
    public void Cluster_LinksIncidentsInAChain_AndIgnoresLoneOnes()
    {
        PlanningIncident At(double lat, double lng) =>
            new(Guid.NewGuid(), "x", IncidentType.Flood, IncidentSeverity.Low, lat, lng, 500, null, null);

        // 0.04° latitude ≈ 4.4 km: A–B and B–C link, A–C would not on its own.
        var clusters = ZonePlanningRules.Cluster([At(7.00, 80.0), At(7.04, 80.0), At(7.08, 80.0), At(8.0, 81.0)]);

        Assert.Equal(3, Assert.Single(clusters).IncidentIds.Count);
    }
}
