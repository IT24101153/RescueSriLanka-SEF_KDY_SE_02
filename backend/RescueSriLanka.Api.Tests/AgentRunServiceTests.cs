using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentA.Models;
using RescueSriLanka.Api.Features.ComponentA.Services;
using RescueSriLanka.Api.Features.ComponentA.Services.Notifications;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Tests;

/// <summary>
/// The human approval gate for the Incident Analysis Agent. A proposal changes
/// nothing until a coordinator approves it; approve, revise and reject must
/// each leave the incident and the audit record in a defined state.
/// </summary>
public class AgentRunServiceTests
{
    private static readonly Guid Coordinator = Guid.NewGuid();

    /// <summary>Captures notifications instead of sending email.</summary>
    private sealed class RecordingQueue : INotificationQueue
    {
        public List<NotificationJob> Jobs { get; } = [];
        public void Enqueue(NotificationJob job) => Jobs.Add(job);
    }

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"approval-{Guid.NewGuid()}")
            .Options);

    private static AgentRunService NewService(AppDbContext db, RecordingQueue queue) =>
        new(db,
            new SafetyZoneService(db, NullLogger<SafetyZoneService>.Instance),
            queue,
            NullLogger<AgentRunService>.Instance);

    /// <summary>An incident the agent has analysed, and the run awaiting review.</summary>
    private static async Task<(Incident Incident, AgentRun Run)> AddProposalAsync(
        AppDbContext db,
        IncidentSeverity inForce = IncidentSeverity.Moderate,
        IncidentSeverity proposed = IncidentSeverity.High,
        int proposedRadius = 3000)
    {
        var incident = new Incident
        {
            Title = "Flood in Kaduwela",
            Description = "Water rising.",
            Type = IncidentType.Flood,
            Severity = inForce,
            AiSeverity = proposed,
            Latitude = 6.9271,
            Longitude = 79.8612,
            AffectedRadiusMeters = 1000,
            District = "Colombo"
        };
        var run = new AgentRun
        {
            AgentName = "IncidentAnalysisAgent",
            Objective = "Classify severity",
            IncidentId = incident.Id,
            Status = AgentRunStatus.Succeeded,
            OutputJson = $$"""{ "severity": "{{proposed}}", "recommendedRadiusMeters": {{proposedRadius}} }"""
        };
        db.Incidents.Add(incident);
        db.AgentRuns.Add(run);
        await db.SaveChangesAsync();
        return (incident, run);
    }

    [Fact]
    public async Task Approve_AppliesTheProposal_WithoutMarkingAnOverride()
    {
        await using var db = NewDb();
        var queue = new RecordingQueue();
        var (incident, run) = await AddProposalAsync(db);

        var dto = await NewService(db, queue).ApproveAsync(run.Id, severity: null, Coordinator);

        Assert.NotNull(dto);
        var saved = await db.Incidents.SingleAsync();
        Assert.Equal(IncidentSeverity.High, saved.Severity);
        Assert.Equal(3000, saved.AffectedRadiusMeters);
        Assert.Null(saved.SeverityOverriddenBy);

        var savedRun = await db.AgentRuns.SingleAsync();
        Assert.True(savedRun.Approved);
        Assert.Equal(Coordinator, savedRun.ApprovedByUserId);
        Assert.NotNull(savedRun.ApprovedAt);

        // Approval warns the district.
        Assert.Contains(queue.Jobs, job =>
            job.Kind == NotificationKind.DistrictWarning && job.SubjectId == incident.Id);
    }

    [Fact]
    public async Task Approve_CreatesTheSafetyZone()
    {
        await using var db = NewDb();
        var (_, run) = await AddProposalAsync(db, proposed: IncidentSeverity.Critical);

        await NewService(db, new RecordingQueue()).ApproveAsync(run.Id, null, Coordinator);

        var zone = await db.SafetyZones.SingleAsync();
        Assert.Equal(ZoneStatus.Danger, zone.Status);
    }

    [Fact]
    public async Task Revise_AppliesTheCoordinatorsSeverity_AndRecordsTheOverride()
    {
        await using var db = NewDb();
        var (_, run) = await AddProposalAsync(db, proposed: IncidentSeverity.High);

        await NewService(db, new RecordingQueue())
            .ApproveAsync(run.Id, IncidentSeverity.Critical, Coordinator);

        var saved = await db.Incidents.SingleAsync();
        Assert.Equal(IncidentSeverity.Critical, saved.Severity);
        Assert.Equal(IncidentSeverity.High, saved.AiSeverity);
        Assert.Equal(Coordinator, saved.SeverityOverriddenBy);
        Assert.NotNull(saved.SeverityOverriddenAt);
    }

    [Fact]
    public async Task Reject_LeavesTheIncidentUntouched_AndRecordsTheReason()
    {
        await using var db = NewDb();
        var queue = new RecordingQueue();
        var (_, run) = await AddProposalAsync(db, inForce: IncidentSeverity.Moderate);

        await NewService(db, queue).RejectAsync(run.Id, "Photo shows a puddle, not a flood.", Coordinator);

        var saved = await db.Incidents.SingleAsync();
        Assert.Equal(IncidentSeverity.Moderate, saved.Severity);
        Assert.Equal(1000, saved.AffectedRadiusMeters);

        var savedRun = await db.AgentRuns.SingleAsync();
        Assert.False(savedRun.Approved);
        Assert.Equal(Coordinator, savedRun.ApprovedByUserId);
        Assert.Contains("Photo shows a puddle", savedRun.ErrorMessage);
        Assert.Empty(queue.Jobs);
        Assert.Empty(db.SafetyZones);
    }

    [Fact]
    public async Task Approve_ClampsAnOutOfRangeRadius()
    {
        await using var db = NewDb();
        var (_, run) = await AddProposalAsync(db, proposedRadius: 500000);

        await NewService(db, new RecordingQueue()).ApproveAsync(run.Id, null, Coordinator);

        Assert.Equal(20000, (await db.Incidents.SingleAsync()).AffectedRadiusMeters);
    }

    [Fact]
    public async Task UnknownRun_ReturnsNull_ForApproveAndReject()
    {
        await using var db = NewDb();
        var service = NewService(db, new RecordingQueue());

        Assert.Null(await service.ApproveAsync(Guid.NewGuid(), null, Coordinator));
        Assert.Null(await service.RejectAsync(Guid.NewGuid(), "n/a", Coordinator));
    }
}
