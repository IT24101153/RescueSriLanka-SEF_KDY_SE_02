using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentA.Models;
using RescueSriLanka.Api.Features.ComponentA.Services;
using RescueSriLanka.Api.Features.ComponentA.Services.Notifications;

namespace RescueSriLanka.Api.Tests;

/// <summary>
/// A rejection is never final: a coordinator who rejected a true report by
/// mistake can approve it later, and it must come back exactly as if it had
/// been approved first time — on the map, with its safety zone.
/// </summary>
public class IncidentStatusRollbackTests
{
    private static readonly Guid Coordinator = Guid.NewGuid();

    private sealed class RecordingQueue : INotificationQueue
    {
        public List<NotificationJob> Jobs { get; } = [];
        public void Enqueue(NotificationJob job) => Jobs.Add(job);
    }

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"rollback-{Guid.NewGuid()}")
            .Options);

    // Status changes never touch the analysis queue or photo storage.
    private static IncidentService NewService(AppDbContext db, RecordingQueue queue) =>
        new(db,
            new SafetyZoneService(db, NullLogger<SafetyZoneService>.Instance),
            analysisQueue: null!,
            queue,
            imageStorage: null!,
            NullLogger<IncidentService>.Instance);

    private static async Task<Incident> AddReportAsync(AppDbContext db)
    {
        var incident = new Incident
        {
            Title = "Flood in Kaduwela",
            Description = "Water rising.",
            Type = IncidentType.Flood,
            Severity = IncidentSeverity.Critical,
            Latitude = 6.9271,
            Longitude = 79.8612,
            AffectedRadiusMeters = 1000,
            District = "Colombo",
        };
        db.Incidents.Add(incident);
        await db.SaveChangesAsync();
        return incident;
    }

    [Fact]
    public async Task Reject_TakesTheReportOffTheMap()
    {
        await using var db = NewDb();
        var incident = await AddReportAsync(db);

        await NewService(db, new RecordingQueue())
            .UpdateStatusAsync(incident.Id, IncidentStatus.Rejected, Coordinator);

        var saved = await db.Incidents.SingleAsync();
        Assert.False(saved.IsActive);
        Assert.Empty(await db.SafetyZones.ToListAsync());
    }

    [Fact]
    public async Task ApproveAfterReject_BringsTheReportAndItsZoneBack()
    {
        await using var db = NewDb();
        var incident = await AddReportAsync(db);
        var queue = new RecordingQueue();
        var service = NewService(db, queue);

        await service.UpdateStatusAsync(incident.Id, IncidentStatus.Rejected, Coordinator);
        var result = await service.UpdateStatusAsync(incident.Id, IncidentStatus.Verified, Coordinator);

        Assert.NotNull(result);
        var saved = await db.Incidents.SingleAsync();
        Assert.Equal(IncidentStatus.Verified, saved.Status);
        Assert.True(saved.IsActive);
        Assert.Null(saved.ResolvedAt);
        Assert.Equal(Coordinator, saved.VerifiedByUserId);

        var zone = await db.SafetyZones.SingleAsync();
        Assert.Equal(ZoneStatus.Danger, zone.Status);

        // Approving is the moment the district is warned, rollback included.
        Assert.Contains(queue.Jobs, job =>
            job.Kind == NotificationKind.DistrictWarning && job.SubjectId == incident.Id);
    }
}
