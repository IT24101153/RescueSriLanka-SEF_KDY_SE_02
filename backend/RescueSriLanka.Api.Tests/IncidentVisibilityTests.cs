using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentA.Models;
using RescueSriLanka.Api.Features.ComponentA.Services;
using RescueSriLanka.Api.Features.ComponentA.Services.Notifications;

namespace RescueSriLanka.Api.Tests;

/// <summary>
/// Who may see a report. "My reports" comes from the database so it is the
/// same on every device; a report nobody has approved stays private to the
/// person who filed it and to staff.
/// </summary>
public class IncidentVisibilityTests
{
    private static readonly Guid Reporter = Guid.NewGuid();
    private static readonly Guid SomeoneElse = Guid.NewGuid();

    private sealed class NullQueue : INotificationQueue
    {
        public void Enqueue(NotificationJob job) { }
    }

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"visibility-{Guid.NewGuid()}")
            .Options);

    // Reads never touch the analysis queue or photo storage.
    private static IncidentService NewService(AppDbContext db) =>
        new(db,
            new SafetyZoneService(db, NullLogger<SafetyZoneService>.Instance),
            analysisQueue: null!,
            new NullQueue(),
            imageStorage: null!,
            NullLogger<IncidentService>.Instance);

    private static Incident Report(
        Guid? reporter,
        IncidentStatus status = IncidentStatus.Reported,
        DateTime? reportedAt = null) => new()
    {
        Title = "Flood in Kaduwela",
        Description = "Water rising.",
        Type = IncidentType.Flood,
        Status = status,
        Latitude = 6.9271,
        Longitude = 79.8612,
        District = "Colombo",
        ReportedByUserId = reporter,
        ReportedAt = reportedAt ?? DateTime.UtcNow,
        IsActive = status is not (IncidentStatus.Rejected or IncidentStatus.Resolved),
    };

    [Fact]
    public async Task Mine_ReturnsOnlyTheUsersReports_InEveryState_NewestFirst()
    {
        await using var db = NewDb();
        var older = Report(Reporter, IncidentStatus.Rejected, DateTime.UtcNow.AddHours(-2));
        var newer = Report(Reporter, IncidentStatus.Reported, DateTime.UtcNow);
        db.Incidents.AddRange(older, newer, Report(SomeoneElse), Report(null));
        await db.SaveChangesAsync();

        var mine = await NewService(db).MineAsync(Reporter);

        Assert.Equal([newer.Id, older.Id], mine.Select(report => report.Id));
    }

    [Theory]
    [InlineData(IncidentStatus.Reported)]
    [InlineData(IncidentStatus.Rejected)]
    public async Task UnapprovedReport_IsHiddenFromThePublic_ButNotItsReporterOrStaff(
        IncidentStatus status)
    {
        await using var db = NewDb();
        var report = Report(Reporter, status);
        db.Incidents.Add(report);
        await db.SaveChangesAsync();
        var service = NewService(db);

        Assert.Null(await service.GetForViewerAsync(report.Id, null, viewerIsStaff: false));
        Assert.Null(await service.GetForViewerAsync(report.Id, SomeoneElse, viewerIsStaff: false));
        Assert.NotNull(await service.GetForViewerAsync(report.Id, Reporter, viewerIsStaff: false));
        Assert.NotNull(await service.GetForViewerAsync(report.Id, SomeoneElse, viewerIsStaff: true));
    }

    [Fact]
    public async Task ApprovedReport_IsVisibleToEveryone()
    {
        await using var db = NewDb();
        var report = Report(Reporter, IncidentStatus.Verified);
        db.Incidents.Add(report);
        await db.SaveChangesAsync();

        Assert.NotNull(await NewService(db).GetForViewerAsync(report.Id, null, viewerIsStaff: false));
    }
}
