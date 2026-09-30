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
    public async Task Approve_CreatesTheSafetyZone_OnceTheReportIsApproved()
    {
        await using var db = NewDb();
        var (incident, run) = await AddProposalAsync(db, proposed: IncidentSeverity.Critical);
        incident.Status = IncidentStatus.Verified;
        await db.SaveChangesAsync();

        await NewService(db, new RecordingQueue()).ApproveAsync(run.Id, null, Coordinator);

        var zone = await db.SafetyZones.SingleAsync();
        Assert.Equal(ZoneStatus.Danger, zone.Status);
    }

    [Fact]
    public async Task Approve_OnAReportNotYetApproved_DrawsNoZone()
    {
        await using var db = NewDb();
        var (_, run) = await AddProposalAsync(db, proposed: IncidentSeverity.Critical);

        await NewService(db, new RecordingQueue()).ApproveAsync(run.Id, null, Coordinator);

        Assert.Empty(await db.SafetyZones.ToListAsync());
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
        Assert.Equal(AgentRunDecision.Rejected, savedRun.Decision);
        Assert.Equal(Coordinator, savedRun.ApprovedByUserId);
        Assert.Contains("Photo shows a puddle", savedRun.DecisionNote);
        // A human rejection is not an agent failure.
        Assert.Null(savedRun.ErrorMessage);
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

    // ---------- one decision per proposal ----------

    [Fact]
    public async Task Approve_Twice_IsRefused_AndWarnsTheDistrictOnlyOnce()
    {
        await using var db = NewDb();
        var queue = new RecordingQueue();
        var (_, run) = await AddProposalAsync(db);
        var service = NewService(db, queue);

        await service.ApproveAsync(run.Id, null, Coordinator);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ApproveAsync(run.Id, null, Coordinator));

        Assert.Contains("already been approved", ex.Message);
        Assert.Single(queue.Jobs);
    }

    [Fact]
    public async Task Approve_AfterReject_IsRefused_AndChangesNothing()
    {
        await using var db = NewDb();
        var queue = new RecordingQueue();
        var (_, run) = await AddProposalAsync(db, inForce: IncidentSeverity.Moderate);
        var service = NewService(db, queue);

        await service.RejectAsync(run.Id, "Duplicate report.", Coordinator);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ApproveAsync(run.Id, IncidentSeverity.Critical, Coordinator));

        Assert.Contains("already been rejected", ex.Message);
        Assert.Equal(IncidentSeverity.Moderate, (await db.Incidents.SingleAsync()).Severity);
        Assert.False((await db.AgentRuns.SingleAsync()).Approved);
        Assert.Empty(queue.Jobs);
    }

    [Fact]
    public async Task Reject_AfterApprove_IsRefused_AndKeepsTheApproval()
    {
        await using var db = NewDb();
        var (_, run) = await AddProposalAsync(db);
        var service = NewService(db, new RecordingQueue());

        await service.ApproveAsync(run.Id, null, Coordinator);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RejectAsync(run.Id, "Changed my mind.", Coordinator));

        Assert.True((await db.AgentRuns.SingleAsync()).Approved);
    }

    [Theory]
    [InlineData(AgentRunStatus.Failed)]
    [InlineData(AgentRunStatus.Running)]
    public async Task RunWithoutAProposal_CannotBeDecided(AgentRunStatus status)
    {
        await using var db = NewDb();
        var queue = new RecordingQueue();
        var (_, run) = await AddProposalAsync(db);
        run.Status = status;
        await db.SaveChangesAsync();
        var service = NewService(db, queue);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApproveAsync(run.Id, null, Coordinator));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RejectAsync(run.Id, "n/a", Coordinator));
        Assert.Empty(queue.Jobs);
    }

    // ---------- decision record, staleness, atomicity ----------

    [Fact]
    public async Task Approve_StoresTheNote_AndMarksTheDecision()
    {
        await using var db = NewDb();
        var (_, run) = await AddProposalAsync(db);

        await NewService(db, new RecordingQueue())
            .ApproveAsync(run.Id, null, Coordinator, "  Matches the photo.  ");

        var saved = await db.AgentRuns.SingleAsync();
        Assert.Equal(AgentRunDecision.Approved, saved.Decision);
        Assert.Equal("Matches the photo.", saved.DecisionNote);
    }

    [Fact]
    public async Task Approve_WithADifferentSeverity_IsRecordedAsRevised()
    {
        await using var db = NewDb();
        var (_, run) = await AddProposalAsync(db, proposed: IncidentSeverity.High);

        await NewService(db, new RecordingQueue())
            .ApproveAsync(run.Id, IncidentSeverity.Critical, Coordinator);

        Assert.Equal(AgentRunDecision.Revised, (await db.AgentRuns.SingleAsync()).Decision);
    }

    [Fact]
    public async Task AnOlderRun_CannotBeDecided_OnceANewerAnalysisExists()
    {
        await using var db = NewDb();
        var queue = new RecordingQueue();
        var (incident, older) = await AddProposalAsync(db, inForce: IncidentSeverity.Moderate);
        older.StartedAt = DateTime.UtcNow.AddMinutes(-10);
        db.AgentRuns.Add(new AgentRun
        {
            AgentName = "IncidentAnalysisAgent", Objective = "Classify severity",
            IncidentId = incident.Id, Status = AgentRunStatus.Succeeded,
            OutputJson = """{ "severity": "Critical", "recommendedRadiusMeters": 5000 }"""
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewService(db, queue).ApproveAsync(older.Id, null, Coordinator));

        Assert.Contains("newer analysis", ex.Message);
        Assert.Equal(IncidentSeverity.Moderate, (await db.Incidents.SingleAsync()).Severity);
        Assert.Empty(queue.Jobs);
    }

    [Fact]
    public async Task ANewerFailedRun_DoesNotBlockDecidingTheLastGoodProposal()
    {
        await using var db = NewDb();
        var (incident, good) = await AddProposalAsync(db);
        good.StartedAt = DateTime.UtcNow.AddMinutes(-10);
        db.AgentRuns.Add(new AgentRun
        {
            AgentName = "IncidentAnalysisAgent", Objective = "Classify severity",
            IncidentId = incident.Id, Status = AgentRunStatus.Failed
        });
        await db.SaveChangesAsync();

        var result = await NewService(db, new RecordingQueue()).ApproveAsync(good.Id, null, Coordinator);

        Assert.NotNull(result);
    }

    [Fact]
    public void TheDecisionTimestamp_IsAConcurrencyToken_SoTwoCoordinatorsCannotBothDecide()
    {
        using var db = NewDb();

        var property = db.Model.FindEntityType(typeof(AgentRun))!.FindProperty(nameof(AgentRun.ApprovedAt))!;

        Assert.True(property.IsConcurrencyToken);
    }

    private sealed class FailingZones : ISafetyZoneService
    {
        public Task<IReadOnlyList<RescueSriLanka.Api.Features.ComponentA.DTOs.SafetyZoneDto>> GetActiveAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<RescueSriLanka.Api.Features.ComponentA.DTOs.ZoneCheckResultDto> CheckPointAsync(double latitude, double longitude, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> RecomputeAsync(CancellationToken ct = default) => throw new IOException("zone store down");
    }

    [Fact]
    public async Task IfTheZoneRecomputeFails_NoDistrictWarningIsQueued()
    {
        await using var db = NewDb();
        var queue = new RecordingQueue();
        var (_, run) = await AddProposalAsync(db);
        var service = new AgentRunService(db, new FailingZones(), queue, NullLogger<AgentRunService>.Instance);

        await Assert.ThrowsAsync<IOException>(() => service.ApproveAsync(run.Id, null, Coordinator));

        Assert.Empty(queue.Jobs);
    }

    [Fact]
    public void TheAnalyseEndpoint_IsRateLimited()
    {
        var method = typeof(RescueSriLanka.Api.Features.ComponentA.Controllers.IncidentsController)
            .GetMethod("Analyse")!;

        var limit = method.GetCustomAttributes(typeof(Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute), false);

        Assert.NotEmpty(limit);
    }
}
