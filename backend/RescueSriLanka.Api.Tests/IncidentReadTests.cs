using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Controllers;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.DTOs.Incidents;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Services;
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
        var response = await new IncidentsController(new IncidentReadService(db)).List();
        var rows = Assert.IsAssignableFrom<IReadOnlyList<IncidentDto>>(Assert.IsType<OkObjectResult>(response.Result).Value);
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
        var response = await new IncidentsController(new IncidentReadService(db)).Get(incident.Id);
        var dto = Assert.IsType<IncidentDto>(Assert.IsType<OkObjectResult>(response.Result).Value);
        Assert.Equal(incident.Id, dto.Id);
        Assert.Equal(1, dto.ImageCount);
        Assert.Equal("/uploads/test.jpg", Assert.Single(dto.Images).Url);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task MissingIdReturns404()
    {
        await using var db = CreateDb();
        var response = await new IncidentsController(new IncidentReadService(db)).Get(Guid.NewGuid());
        Assert.IsType<NotFoundResult>(response.Result);
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
