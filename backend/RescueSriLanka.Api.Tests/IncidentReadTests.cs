using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentA.DTOs;
using RescueSriLanka.Api.Features.ComponentA.Models;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Features.ComponentD.Services;
using Xunit;

namespace RescueSriLanka.Api.Tests;

public class IncidentReadTests
{
    private static AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static Incident CreateIncident(bool active = true, IncidentSeverity severity = IncidentSeverity.High) => new()
    {
        Title = "Flood in Kandy", Description = "Test incident", Type = IncidentType.Flood,
        Severity = severity, Status = active ? IncidentStatus.Verified : IncidentStatus.Resolved,
        District = "Kandy", IsActive = active, ReportedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task DefaultListExcludesInactiveAndDoesNotTrackEntities()
    {
        await using var db = CreateDb();
        db.Incidents.AddRange(CreateIncident(), CreateIncident(false));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var rows = await new IncidentReadService(db).QueryAsync();
        Assert.True(Assert.Single(rows).IsActive);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ActiveOnlyFalseIncludesBothStates()
    {
        await using var db = CreateDb();
        db.Incidents.AddRange(CreateIncident(), CreateIncident(false));
        await db.SaveChangesAsync();
        var rows = await new IncidentReadService(db).QueryAsync(activeOnly: false);
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => !row.IsActive);
    }

    [Fact]
    public async Task GetByIdIncludesInactiveIncidentAndCanonicalImageProjection()
    {
        await using var db = CreateDb();
        var incident = CreateIncident(false);
        incident.Images.Add(new IncidentImage { StoragePath = "/uploads/test.jpg", SizeBytes = 123 });
        db.Incidents.Add(incident);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var dto = await new IncidentReadService(db).GetAsync(incident.Id);
        Assert.NotNull(dto);
        Assert.Equal(incident.Id, dto!.Id);
        Assert.Equal(1, dto.ImageCount);
        Assert.Equal("/uploads/test.jpg", Assert.Single(dto.Images).Url);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task MissingIdReturns404()
    {
        await using var db = CreateDb();
        Assert.Null(await new IncidentReadService(db).GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task FiltersAndSeverityPriorityArePreserved()
    {
        await using var db = CreateDb();
        var high = CreateIncident();
        var critical = CreateIncident(severity: IncidentSeverity.Critical);
        var olderCritical = CreateIncident(severity: IncidentSeverity.Critical);
        olderCritical.ReportedAt = critical.ReportedAt.AddDays(-1);
        db.Incidents.AddRange(high, critical, olderCritical);
        await db.SaveChangesAsync();
        var service = new IncidentReadService(db);
        var ordered = await service.QueryAsync();
        Assert.Equal(new[] { critical.Id, olderCritical.Id, high.Id }, ordered.Select(i => i.Id));
        var filtered = await service.QueryAsync(IncidentStatus.Verified, IncidentSeverity.High, IncidentType.Flood, "Kandy");
        Assert.Equal(high.Id, Assert.Single(filtered).Id);
        Assert.Empty(await service.QueryAsync(district: "Colombo"));
    }
}
