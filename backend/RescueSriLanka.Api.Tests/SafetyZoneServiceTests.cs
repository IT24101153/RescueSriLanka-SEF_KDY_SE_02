using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentA.Models;
using RescueSriLanka.Api.Features.ComponentA.Services;

namespace RescueSriLanka.Api.Tests;

/// <summary>
/// The live safe/caution/danger layer — Component A's signature operation.
/// These cover the rules a coordinator would ask about in a demo: which zones
/// appear, which disappear, and what a hand-declared zone is protected from.
/// </summary>
public class SafetyZoneServiceTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            // A fresh database name per test keeps them independent.
            .UseInMemoryDatabase($"zones-{Guid.NewGuid()}")
            .Options);

    private static SafetyZoneService NewService(AppDbContext db) =>
        new(db, NullLogger<SafetyZoneService>.Instance);

    private static Incident NewIncident(
        IncidentSeverity severity = IncidentSeverity.High,
        bool isActive = true,
        double latitude = 6.9271,
        double longitude = 79.8612,
        int radiusMeters = 2000) => new()
        {
            Title = "Test incident",
            Description = "Created by a unit test.",
            Type = IncidentType.Flood,
            Severity = severity,
            Latitude = latitude,
            Longitude = longitude,
            AffectedRadiusMeters = radiusMeters,
            District = "Colombo",
            IsActive = isActive
        };

    [Fact]
    public async Task Recompute_DerivesAZoneFromAnActiveIncident()
    {
        using var db = NewDb();
        db.Incidents.Add(NewIncident());
        await db.SaveChangesAsync();

        await NewService(db).RecomputeAsync();

        var zone = Assert.Single(db.SafetyZones);
        Assert.Equal(ZoneSource.DerivedFromIncident, zone.Source);
        Assert.Equal(2000, zone.RadiusMeters);
    }

    [Theory]
    [InlineData(IncidentSeverity.Critical, ZoneStatus.Danger)]
    [InlineData(IncidentSeverity.High, ZoneStatus.Danger)]
    [InlineData(IncidentSeverity.Moderate, ZoneStatus.Caution)]
    [InlineData(IncidentSeverity.Low, ZoneStatus.Caution)]
    public async Task Recompute_MapsSeverityToZoneStatus(
        IncidentSeverity severity, ZoneStatus expected)
    {
        using var db = NewDb();
        db.Incidents.Add(NewIncident(severity: severity));
        await db.SaveChangesAsync();

        await NewService(db).RecomputeAsync();

        Assert.Equal(expected, Assert.Single(db.SafetyZones).Status);
    }

    [Fact]
    public async Task Recompute_IgnoresClosedIncidents()
    {
        using var db = NewDb();
        db.Incidents.Add(NewIncident(isActive: false));
        await db.SaveChangesAsync();

        await NewService(db).RecomputeAsync();

        Assert.Empty(db.SafetyZones);
    }

    [Fact]
    public async Task Recompute_DropsTheZoneWhenItsIncidentCloses()
    {
        using var db = NewDb();
        var incident = NewIncident();
        db.Incidents.Add(incident);
        await db.SaveChangesAsync();

        var service = NewService(db);
        await service.RecomputeAsync();
        Assert.Single(db.SafetyZones);

        // Resolving an incident must take its hazard off the citizen's map.
        incident.IsActive = false;
        await db.SaveChangesAsync();
        await service.RecomputeAsync();

        Assert.Empty(db.SafetyZones);
    }

    [Fact]
    public async Task Recompute_UpdatesTheExistingZoneRatherThanAddingASecond()
    {
        using var db = NewDb();
        var incident = NewIncident(severity: IncidentSeverity.Moderate);
        db.Incidents.Add(incident);
        await db.SaveChangesAsync();

        var service = NewService(db);
        await service.RecomputeAsync();

        incident.Severity = IncidentSeverity.Critical;
        await db.SaveChangesAsync();
        await service.RecomputeAsync();

        var zone = Assert.Single(db.SafetyZones);
        Assert.Equal(ZoneStatus.Danger, zone.Status);
    }

    [Fact]
    public async Task Recompute_LeavesAManuallyDeclaredZoneAlone()
    {
        using var db = NewDb();
        db.SafetyZones.Add(new SafetyZone
        {
            Name = "Declared by a coordinator",
            Status = ZoneStatus.Danger,
            Source = ZoneSource.ManualOverride,
            CenterLatitude = 7.2906,
            CenterLongitude = 80.6337,
            RadiusMeters = 5000
        });
        await db.SaveChangesAsync();

        // No incidents at all — the recompute still must not touch it.
        await NewService(db).RecomputeAsync();

        var zone = Assert.Single(db.SafetyZones);
        Assert.Equal(ZoneSource.ManualOverride, zone.Source);
        Assert.Equal(ZoneStatus.Danger, zone.Status);
    }

    [Fact]
    public async Task CheckPoint_InsideADangerZone_ReportsDanger()
    {
        using var db = NewDb();
        db.Incidents.Add(NewIncident(severity: IncidentSeverity.Critical));
        await db.SaveChangesAsync();

        var service = NewService(db);
        await service.RecomputeAsync();

        var result = await service.CheckPointAsync(6.9271, 79.8612);

        Assert.Equal(nameof(ZoneStatus.Danger), result.Status);
        Assert.NotEmpty(result.MatchingZones);
    }

    [Fact]
    public async Task CheckPoint_WellOutsideEveryZone_ReportsSafe()
    {
        using var db = NewDb();
        db.Incidents.Add(NewIncident());
        await db.SaveChangesAsync();

        var service = NewService(db);
        await service.RecomputeAsync();

        // Kandy is ~94 km from the Colombo incident's 2 km radius.
        var result = await service.CheckPointAsync(7.2906, 80.6337);

        Assert.Equal(nameof(ZoneStatus.Safe), result.Status);
        Assert.Empty(result.MatchingZones);
    }

    [Fact]
    public async Task CheckPoint_WhereZonesOverlap_ReportsTheWorstOne()
    {
        using var db = NewDb();
        // Same spot, two incidents: caution must never mask danger.
        db.Incidents.Add(NewIncident(severity: IncidentSeverity.Low));
        db.Incidents.Add(NewIncident(severity: IncidentSeverity.Critical));
        await db.SaveChangesAsync();

        var service = NewService(db);
        await service.RecomputeAsync();

        var result = await service.CheckPointAsync(6.9271, 79.8612);

        Assert.Equal(nameof(ZoneStatus.Danger), result.Status);
    }

    [Fact]
    public async Task CheckPoint_JustOutsideTheRadius_IsNotInsideTheZone()
    {
        using var db = NewDb();
        db.Incidents.Add(NewIncident(radiusMeters: 1000));
        await db.SaveChangesAsync();

        var service = NewService(db);
        await service.RecomputeAsync();

        // ~2.2 km north of the centre, comfortably past a 1 km radius.
        var result = await service.CheckPointAsync(6.9471, 79.8612);

        Assert.Equal(nameof(ZoneStatus.Safe), result.Status);
    }
}
