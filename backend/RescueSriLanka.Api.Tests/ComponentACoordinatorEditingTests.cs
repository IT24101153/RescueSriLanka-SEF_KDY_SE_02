using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentA.Agents.IncidentEnrichmentAgent;
using RescueSriLanka.Api.Features.ComponentA.Agents.ZonePlanningAgent;
using RescueSriLanka.Api.Features.ComponentA.DTOs;
using RescueSriLanka.Api.Features.ComponentA.Models;
using RescueSriLanka.Api.Features.ComponentA.Services;
using RescueSriLanka.Api.Features.ComponentA.Services.Notifications;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Tests;

/// <summary>
/// The coordinator's side of the new Component A agents: approving an
/// enrichment or zone plan (in full, in part, or edited), merging duplicates,
/// declaring and editing zones by hand, and filing or correcting reports.
/// </summary>
public class ComponentACoordinatorEditingTests
{
    private static readonly Guid Coordinator = Guid.NewGuid();

    private sealed class RecordingQueue : INotificationQueue
    {
        public List<NotificationJob> Jobs { get; } = [];
        public void Enqueue(NotificationJob job) => Jobs.Add(job);
    }

    private sealed class RecordingAnalysis : IIncidentAnalysisQueue
    {
        public List<Guid> Ids { get; } = [];
        public void Enqueue(Guid incidentId) => Ids.Add(incidentId);
    }

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"editing-{Guid.NewGuid()}")
            .Options);

    private static SafetyZoneService Zones(AppDbContext db) =>
        new(db, NullLogger<SafetyZoneService>.Instance);

    private static AgentRunService Gate(AppDbContext db, RecordingQueue? queue = null) =>
        new(db, Zones(db), queue ?? new RecordingQueue(), NullLogger<AgentRunService>.Instance);

    private static Incident Incident(
        AppDbContext db, string title = "Flood near the bridge",
        IncidentStatus status = IncidentStatus.Reported, string? district = null, int? people = null)
    {
        var incident = new Incident
        {
            Title = title,
            Description = "Water rising near the bridge; 40 families cut off.",
            Type = IncidentType.Flood,
            Severity = IncidentSeverity.High,
            Status = status,
            Latitude = 6.955,
            Longitude = 79.882,
            District = district,
            EstimatedAffectedPeople = people
        };
        db.Incidents.Add(incident);
        return incident;
    }

    private static AgentRun Run(AppDbContext db, string agent, Guid? incidentId, object output, DateTime? startedAt = null)
    {
        var run = new AgentRun
        {
            AgentName = agent,
            Objective = "test",
            IncidentId = incidentId,
            Status = AgentRunStatus.Succeeded,
            StartedAt = startedAt ?? DateTime.UtcNow,
            OutputJson = JsonSerializer.Serialize(output)
        };
        db.AgentRuns.Add(run);
        return run;
    }

    private static EnrichmentResult Proposal(Guid? duplicateOf = null) => new()
    {
        Suggestions =
        [
            new FieldSuggestion { Field = EnrichmentFields.District, Proposed = "Colombo", Reason = "Nearest town." },
            new FieldSuggestion { Field = EnrichmentFields.People, Proposed = "160", Reason = "40 families." }
        ],
        Duplicate = duplicateOf is Guid id
            ? new DuplicateProposal { IncidentId = id, Title = "Earlier", DistanceKm = 0.1, Similarity = 0.5, Reason = "Same flood." }
            : null,
        Summary = "test"
    };

    // ------------------------------------------------------- enrichment

    [Fact]
    public async Task ApprovingEnrichment_AppliesEveryField_AndQueuesTheDistrictWarning()
    {
        await using var db = NewDb();
        var incident = Incident(db, status: IncidentStatus.Verified);
        var run = Run(db, IncidentEnrichmentAgent.AgentName, incident.Id, Proposal());
        await db.SaveChangesAsync();
        var queue = new RecordingQueue();

        var dto = await Gate(db, queue).ApproveWithAsync(run.Id, new ApproveAgentRunRequest(), Coordinator);

        Assert.Equal("Approved", dto!.Decision);
        var saved = await db.Incidents.SingleAsync();
        Assert.Equal("Colombo", saved.District);
        Assert.Equal(160, saved.EstimatedAffectedPeople);
        Assert.Contains(queue.Jobs, job => job.Kind == NotificationKind.DistrictWarning);
    }

    [Fact]
    public async Task ApprovingSomeFields_AppliesOnlyThose_AndRecordsARevision()
    {
        await using var db = NewDb();
        var incident = Incident(db);
        var run = Run(db, IncidentEnrichmentAgent.AgentName, incident.Id, Proposal());
        await db.SaveChangesAsync();

        var dto = await Gate(db).ApproveWithAsync(run.Id,
            new ApproveAgentRunRequest { Fields = [EnrichmentFields.District] }, Coordinator);

        Assert.Equal("Revised", dto!.Decision);
        var saved = await db.Incidents.SingleAsync();
        Assert.Equal("Colombo", saved.District);
        Assert.Null(saved.EstimatedAffectedPeople);
    }

    [Fact]
    public async Task ApprovingAFieldTheRunNeverProposed_IsRefused_AndChangesNothing()
    {
        await using var db = NewDb();
        var incident = Incident(db);
        var run = Run(db, IncidentEnrichmentAgent.AgentName, incident.Id, Proposal());
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Gate(db).ApproveWithAsync(run.Id,
            new ApproveAgentRunRequest { Fields = [EnrichmentFields.Title] }, Coordinator));

        Assert.Equal(AgentRunDecision.Pending, (await db.AgentRuns.SingleAsync()).Decision);
        Assert.Null((await db.Incidents.SingleAsync()).District);
    }

    [Fact]
    public async Task MergingADuplicate_FoldsItIntoTheEarlierReport_WithItsPhotosAndHeadCount()
    {
        await using var db = NewDb();
        var kept = Incident(db, "Kelani flood", IncidentStatus.Verified, "Colombo", people: 50);
        var duplicate = Incident(db, people: 200);
        db.IncidentImages.Add(new IncidentImage { IncidentId = duplicate.Id, StoragePath = "https://cdn.test/a.jpg" });
        var run = Run(db, IncidentEnrichmentAgent.AgentName, duplicate.Id, Proposal(kept.Id));
        await db.SaveChangesAsync();

        await Gate(db).ApproveWithAsync(run.Id,
            new ApproveAgentRunRequest { Fields = [], MergeDuplicate = true }, Coordinator);

        var merged = await db.Incidents.SingleAsync(i => i.Id == duplicate.Id);
        Assert.Equal(IncidentStatus.Merged, merged.Status);
        Assert.Equal(kept.Id, merged.DuplicateOfIncidentId);
        Assert.False(merged.IsActive);

        var survivor = await db.Incidents.Include(i => i.Images).SingleAsync(i => i.Id == kept.Id);
        Assert.Single(survivor.Images);
        Assert.Equal(200, survivor.EstimatedAffectedPeople);
    }

    [Fact]
    public async Task MergingIntoAReportThatHasSinceClosed_IsRefused()
    {
        await using var db = NewDb();
        var kept = Incident(db, status: IncidentStatus.Resolved);
        kept.IsActive = false;
        var duplicate = Incident(db);
        var run = Run(db, IncidentEnrichmentAgent.AgentName, duplicate.Id, Proposal(kept.Id));
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Gate(db).ApproveWithAsync(run.Id,
            new ApproveAgentRunRequest { MergeDuplicate = true }, Coordinator));
        Assert.Equal(IncidentStatus.Reported, (await db.Incidents.SingleAsync(i => i.Id == duplicate.Id)).Status);
    }

    [Fact]
    public async Task ANewerEnrichmentRun_DoesNotBlockApprovingTheSeverityProposal()
    {
        await using var db = NewDb();
        var incident = Incident(db);
        incident.AiSeverity = IncidentSeverity.Critical;
        var analysis = Run(db, "IncidentAnalysisAgent", incident.Id,
            new { severity = "Critical", recommendedRadiusMeters = 2000 }, DateTime.UtcNow.AddMinutes(-1));
        Run(db, IncidentEnrichmentAgent.AgentName, incident.Id, Proposal());
        await db.SaveChangesAsync();

        var dto = await Gate(db).ApproveAsync(analysis.Id, null, Coordinator);

        Assert.Equal("Approved", dto!.Decision);
        Assert.Equal(IncidentSeverity.Critical, (await db.Incidents.SingleAsync()).Severity);
    }

    // ---------------------------------------------------- zone planning

    private static ZonePlanResult Plan(Guid incidentId, Guid? retire = null) => new()
    {
        Zones =
        [
            new ZoneProposal
            {
                Action = ZoneActions.Create, Name = "Colombo flood area", Status = ZoneStatus.Danger,
                CenterLatitude = 6.94, CenterLongitude = 79.87, RadiusMeters = 3000, District = "Colombo",
                ExpiresInHours = 48, Rationale = "Two floods.", BasedOnIncidentIds = [incidentId]
            },
            .. retire is Guid id
                ? new[]
                {
                    new ZoneProposal
                    {
                        Action = ZoneActions.Retire, ZoneId = id, Name = "Old", Status = ZoneStatus.Caution,
                        CenterLatitude = 8.3, CenterLongitude = 80.4, RadiusMeters = 1000, Rationale = "Quiet."
                    }
                }
                : []
        ],
        Summary = "test"
    };

    private static SafetyZone ManualZone(AppDbContext db)
    {
        var zone = new SafetyZone
        {
            Name = "Old", Status = ZoneStatus.Caution, Source = ZoneSource.ManualOverride,
            CenterLatitude = 8.3, CenterLongitude = 80.4, RadiusMeters = 1000
        };
        db.SafetyZones.Add(zone);
        return zone;
    }

    [Fact]
    public async Task ApprovingAZonePlan_DrawsItsZones_AndRetiresTheStaleOnes()
    {
        await using var db = NewDb();
        var incident = Incident(db, status: IncidentStatus.Verified);
        var stale = ManualZone(db);
        var run = Run(db, ZonePlanningAgent.AgentName, null, Plan(incident.Id, stale.Id));
        await db.SaveChangesAsync();

        var dto = await Gate(db).ApproveWithAsync(run.Id, new ApproveAgentRunRequest(), Coordinator);

        Assert.Equal("Approved", dto!.Decision);
        var created = await db.SafetyZones.SingleAsync(z => z.SourceAgentRunId == run.Id);
        Assert.Equal(ZoneSource.ManualOverride, created.Source);
        Assert.Equal(ZoneStatus.Danger, created.Status);
        Assert.NotNull(created.ExpiresAt);
        Assert.False((await db.SafetyZones.SingleAsync(z => z.Id == stale.Id)).IsActive);
    }

    [Fact]
    public async Task ApprovingAnEditedZonePlan_UsesTheCoordinatorsDraft()
    {
        await using var db = NewDb();
        var incident = Incident(db, status: IncidentStatus.Verified);
        var stale = ManualZone(db);
        var run = Run(db, ZonePlanningAgent.AgentName, null, Plan(incident.Id, stale.Id));
        await db.SaveChangesAsync();

        var dto = await Gate(db).ApproveWithAsync(run.Id, new ApproveAgentRunRequest
        {
            Zones = [new SafetyZoneRequest
            {
                Name = "Wider evacuation area", Status = ZoneStatus.Danger,
                CenterLatitude = 6.94, CenterLongitude = 79.87, RadiusMeters = 5000
            }],
            RetireZoneIds = []
        }, Coordinator);

        Assert.Equal("Revised", dto!.Decision);
        Assert.Equal(5000, (await db.SafetyZones.SingleAsync(z => z.SourceAgentRunId == run.Id)).RadiusMeters);
        Assert.True((await db.SafetyZones.SingleAsync(z => z.Id == stale.Id)).IsActive);
    }

    [Fact]
    public async Task AnEditedZoneOffTheIsland_IsRefused_BeforeAnythingIsWritten()
    {
        await using var db = NewDb();
        var incident = Incident(db, status: IncidentStatus.Verified);
        var run = Run(db, ZonePlanningAgent.AgentName, null, Plan(incident.Id));
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => Gate(db).ApproveWithAsync(run.Id, new ApproveAgentRunRequest
        {
            Zones = [new SafetyZoneRequest
            {
                Name = "At sea", Status = ZoneStatus.Danger, CenterLatitude = 2, CenterLongitude = 75, RadiusMeters = 1000
            }]
        }, Coordinator));

        Assert.Equal(AgentRunDecision.Pending, (await db.AgentRuns.SingleAsync()).Decision);
        Assert.Empty(db.SafetyZones.Where(z => z.Source == ZoneSource.ManualOverride));
    }

    [Fact]
    public async Task OnlyTheNewestZonePlan_CanBeDecided()
    {
        await using var db = NewDb();
        var incident = Incident(db, status: IncidentStatus.Verified);
        var older = Run(db, ZonePlanningAgent.AgentName, null, Plan(incident.Id), DateTime.UtcNow.AddMinutes(-5));
        Run(db, ZonePlanningAgent.AgentName, null, Plan(incident.Id));
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Gate(db).ApproveWithAsync(older.Id, new ApproveAgentRunRequest(), Coordinator));
        Assert.Contains("newer plan", error.Message);
    }

    // ----------------------------------------------------- manual zones

    private static SafetyZoneRequest ZoneRequest(DateTime? expiresAt = null) => new()
    {
        Name = "Kelani evacuation area", Status = ZoneStatus.Danger,
        CenterLatitude = 6.95, CenterLongitude = 79.88, RadiusMeters = 2500,
        District = "colombo", Rationale = "River above flood level.", ExpiresAt = expiresAt
    };

    [Fact]
    public async Task ACoordinatorCanDeclareEditAndRetireAZone()
    {
        await using var db = NewDb();
        var zones = Zones(db);

        var created = await zones.CreateManualAsync(ZoneRequest(), Coordinator);
        Assert.Equal("ManualOverride", created.Source);
        Assert.Equal("Colombo", created.District);

        var edited = await zones.UpdateManualAsync(created.Id, ZoneRequest() with { RadiusMeters = 4000 }, Coordinator);
        Assert.Equal(4000, edited!.RadiusMeters);

        Assert.True(await zones.RetireManualAsync(created.Id));
        Assert.Empty(await zones.GetActiveAsync());
    }

    [Fact]
    public async Task ADerivedZone_CannotBeEditedOrRetiredByHand()
    {
        await using var db = NewDb();
        var zone = new SafetyZone
        {
            Name = "Derived", Source = ZoneSource.DerivedFromIncident,
            CenterLatitude = 6.95, CenterLongitude = 79.88, RadiusMeters = 1000
        };
        db.SafetyZones.Add(zone);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Zones(db).UpdateManualAsync(zone.Id, ZoneRequest(), Coordinator));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Zones(db).RetireManualAsync(zone.Id));
    }

    [Fact]
    public async Task AZoneOffTheIslandOrAlreadyExpired_IsRefused()
    {
        await using var db = NewDb();

        await Assert.ThrowsAsync<ArgumentException>(() => Zones(db).CreateManualAsync(
            ZoneRequest() with { CenterLatitude = 3, CenterLongitude = 75 }, Coordinator));
        await Assert.ThrowsAsync<ArgumentException>(() => Zones(db).CreateManualAsync(
            ZoneRequest(DateTime.UtcNow.AddHours(-1)), Coordinator));
    }

    [Fact]
    public async Task AnExpiredZone_LeavesTheMapAndThePointCheck()
    {
        await using var db = NewDb();
        db.SafetyZones.Add(new SafetyZone
        {
            Name = "Lapsed", Status = ZoneStatus.Danger, Source = ZoneSource.ManualOverride,
            CenterLatitude = 6.95, CenterLongitude = 79.88, RadiusMeters = 2000,
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1)
        });
        await db.SaveChangesAsync();

        Assert.Empty(await Zones(db).GetActiveAsync());
        Assert.Equal("Safe", (await Zones(db).CheckPointAsync(6.95, 79.88)).Status);
    }

    // -------------------------------------------- filing and correcting

    private static IncidentService Incidents(AppDbContext db, RecordingAnalysis analysis, RecordingQueue notifications) =>
        new(db, Zones(db), analysis, notifications,
            new ImageStorageService(db, new NoOpImageStore(), NullLogger<ImageStorageService>.Instance),
            NullLogger<IncidentService>.Instance);

    private static CreateIncidentRequest Report() => new()
    {
        Title = "Bridge flooded after a phone call from the police",
        Description = "Police report the Kelani bridge road under water.",
        Type = IncidentType.Flood,
        Latitude = 6.955,
        Longitude = 79.882,
        District = "Colombo"
    };

    [Fact]
    public async Task ACoordinatorsReport_IsApprovedOnArrival_AndWarnsRatherThanSendingAReceipt()
    {
        await using var db = NewDb();
        var analysis = new RecordingAnalysis();
        var notifications = new RecordingQueue();

        var dto = await Incidents(db, analysis, notifications).CreateAsync(Report(), Coordinator, filedByStaff: true);

        Assert.Equal("Verified", dto.Status);
        Assert.Contains(dto.Id, analysis.Ids);
        Assert.Equal(NotificationKind.DistrictWarning, Assert.Single(notifications.Jobs).Kind);
        // Approved, so it draws its zone straight away.
        Assert.Single(await Zones(db).GetActiveAsync());
    }

    [Fact]
    public async Task ACitizensReport_StillWaitsForReview()
    {
        await using var db = NewDb();
        var notifications = new RecordingQueue();

        var dto = await Incidents(db, new RecordingAnalysis(), notifications).CreateAsync(Report(), Guid.NewGuid());

        Assert.Equal("Reported", dto.Status);
        Assert.Equal(NotificationKind.ReportReceived, Assert.Single(notifications.Jobs).Kind);
    }

    [Fact]
    public async Task EditingAReport_ChangesItsDetails_AndMovesItsZone()
    {
        await using var db = NewDb();
        var service = Incidents(db, new RecordingAnalysis(), new RecordingQueue());
        var created = await service.CreateAsync(Report(), Coordinator, filedByStaff: true);

        var edited = await service.UpdateAsync(created.Id, new UpdateIncidentRequest
        {
            Title = "Kelani bridge road flooded",
            Description = "Police report the road under water.",
            Type = IncidentType.Flood,
            Latitude = 6.960,
            Longitude = 79.890,
            AffectedRadiusMeters = 2500,
            District = "gampaha",
            EstimatedAffectedPeople = 300
        }, Coordinator);

        Assert.Equal("Gampaha", edited!.District);
        var zone = Assert.Single(await Zones(db).GetActiveAsync());
        Assert.Equal(2500, zone.RadiusMeters);
        Assert.Equal(6.960, zone.CenterLatitude);
    }

    [Fact]
    public async Task AMergedReport_CannotBeEdited()
    {
        await using var db = NewDb();
        var incident = Incident(db, status: IncidentStatus.Merged);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Incidents(db, new RecordingAnalysis(), new RecordingQueue()).UpdateAsync(incident.Id, new UpdateIncidentRequest
            {
                Title = "x x x", Description = "y y y", Type = IncidentType.Flood,
                Latitude = 6.9, Longitude = 79.9, AffectedRadiusMeters = 1000
            }, Coordinator));
    }

    // --------------------------------------------------- authorization

    public static IEnumerable<object?[]> CoordinatorOnlyEndpoints()
    {
        foreach (var role in new string?[] { "Citizen", "RescueTeam", null })
        foreach (var endpoint in new[] { "zone-create", "zone-update", "zone-retire", "zone-plan", "incident-update", "incident-enrich" })
            yield return [role, endpoint];
    }

    [Theory]
    [MemberData(nameof(CoordinatorOnlyEndpoints))]
    public async Task TheNewEditingAndAgentEndpoints_AreCoordinatorOnly(string? role, string endpoint)
    {
        using var factory = new ComponentDApiFactory();
        using var client = factory.Client(role);
        var id = Guid.NewGuid();
        var zone = ZoneRequest();
        var (method, path, payload) = endpoint switch
        {
            "zone-create" => ("POST", "/api/safetyzones", (object?)zone),
            "zone-update" => ("PUT", $"/api/safetyzones/{id}", zone),
            "zone-retire" => ("DELETE", $"/api/safetyzones/{id}", null),
            "zone-plan" => ("POST", "/api/safetyzones/plan", null),
            "incident-update" => ("PUT", $"/api/incidents/{id}", new
            {
                title = "Edited title", description = "Edited description", type = "Flood",
                latitude = 6.9, longitude = 79.9, affectedRadiusMeters = 1000
            }),
            "incident-enrich" => ("POST", $"/api/incidents/{id}/enrich", null),
            _ => throw new ArgumentOutOfRangeException(nameof(endpoint))
        };

        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (payload is not null) request.Content = JsonContent.Create(payload);
        using var response = await client.SendAsync(request);

        Assert.Equal(role is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
    }
}
