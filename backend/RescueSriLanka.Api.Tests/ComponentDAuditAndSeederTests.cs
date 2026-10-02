using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentA.Models;
using RescueSriLanka.Api.Features.ComponentD.Data;
using RescueSriLanka.Api.Features.ComponentD.Models;

namespace RescueSriLanka.Api.Tests;

public class ComponentDAuditAndSeederTests
{
    public static IEnumerable<object[]> EntitySavePaths()
    {
        foreach (var kind in new[] { "team", "member", "vehicle", "assignment", "dispatch" })
            foreach (var async in new[] { false, true })
                yield return new object[] { kind, async };
    }

    private static IComponentDAuditable Entity(string kind) => kind switch
    {
        "team" => new RescueTeam { Name = "Test" },
        "member" => new TeamMember { FullName = "Test", Phone = "0000000000" },
        "vehicle" => new Vehicle { PlateNumber = "TEST", Capacity = 2 },
        "assignment" => new Assignment(),
        _ => new Dispatch()
    };

    private static Task<int> Save(ComponentDDbContext db, bool async) =>
        async ? db.SaveChangesAsync() : Task.FromResult(db.SaveChanges());

    [Theory]
    [MemberData(nameof(EntitySavePaths))]
    public async Task NewOwnedEntityReceivesEqualUtcAuditFields(string kind, bool async)
    {
        using var db = TestDbFactory.Create();
        var entity = Entity(kind);
        entity.CreatedAt = entity.UpdatedAt = DateTime.UnixEpoch;
        db.Add(entity);
        var before = DateTime.UtcNow;
        await Save(db, async);
        Assert.InRange(entity.CreatedAt, before, DateTime.UtcNow);
        Assert.Equal(entity.CreatedAt, entity.UpdatedAt);
        Assert.Equal(DateTimeKind.Utc, entity.CreatedAt.Kind);
    }

    [Theory]
    [MemberData(nameof(EntitySavePaths))]
    public async Task ModificationPreservesCreationAndAdvancesUpdate(string kind, bool async)
    {
        using var db = TestDbFactory.Create();
        var entity = Entity(kind);
        db.Add(entity);
        await Save(db, async);
        var created = entity.CreatedAt;
        // Ensure a measurable update interval without timing-dependent sleeps.
        entity.UpdatedAt = DateTime.UnixEpoch;
        db.Entry(entity).Property(nameof(IComponentDAuditable.UpdatedAt)).OriginalValue = DateTime.UnixEpoch;
        entity.CreatedAt = DateTime.UnixEpoch; // An attempted edit must not rewrite creation history.
        var before = DateTime.UtcNow;
        await Save(db, async);
        Assert.Equal(created, entity.CreatedAt);
        Assert.InRange(entity.UpdatedAt, before, DateTime.UtcNow);
        Assert.True(entity.UpdatedAt > DateTime.UnixEpoch);
        db.ChangeTracker.Clear();
        var saved = Assert.Single(db.SetForAudit(kind));
        Assert.Equal(created, saved.CreatedAt);
        Assert.Equal(entity.UpdatedAt, saved.UpdatedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnchangedAndDeletedEntitiesAreNotStamped(bool async)
    {
        using var db = TestDbFactory.Create();
        var team = new RescueTeam { Name = "Test" };
        db.Add(team);
        await Save(db, async);
        var created = team.CreatedAt;
        var updated = team.UpdatedAt;
        Assert.Equal(0, await Save(db, async));
        Assert.Equal(updated, team.UpdatedAt);
        db.Remove(team);
        await Save(db, async);
        Assert.Equal(created, team.CreatedAt);
        Assert.Equal(updated, team.UpdatedAt);
    }

    [Fact]
    public async Task SharedWorkflowAndStepTimestampsRemainUntouched()
    {
        using var db = TestDbFactory.Create();
        var workflow = new AgentWorkflow { CreatedAt = DateTime.UnixEpoch, UpdatedAt = DateTime.UnixEpoch };
        var step = new AgentStep { Workflow = workflow, CreatedAt = DateTime.UnixEpoch };
        db.AddRange(workflow, step);
        await db.SaveChangesAsync();
        workflow.Status = WorkflowStatus.Failed;
        step.Action = "Test";
        db.SaveChanges();
        Assert.Equal(DateTime.UnixEpoch, workflow.CreatedAt);
        Assert.Equal(DateTime.UnixEpoch, workflow.UpdatedAt);
        Assert.Equal(DateTime.UnixEpoch, step.CreatedAt);
    }

    private static AppDbContext Shared() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Task Seed(ComponentDDbContext db, AppDbContext shared, string? reference = null,
        string environment = "Development", string? enabled = "true") =>
        ComponentDDataSeeder.SeedAsync(db, shared, new TestEnvironment { EnvironmentName = environment },
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ComponentD:SeedDemoData"] = enabled,
                ["ComponentD:DemoReferenceIncidentId"] = reference
            }).Build(), NullLogger.Instance);

    [Fact]
    public async Task SeederCreatesFixedIdleGraphAndIsIdempotent()
    {
        using var db = TestDbFactory.Create();
        using var shared = Shared();
        await Seed(db, shared);
        var created = (await db.RescueTeams.SingleAsync()).CreatedAt;
        await Seed(db, shared);
        Assert.Equal(ComponentDDataSeeder.TeamId, (await db.RescueTeams.SingleAsync()).Id);
        Assert.Equal(created, (await db.RescueTeams.SingleAsync()).CreatedAt);
        Assert.Equal(TeamStatus.Available, (await db.RescueTeams.SingleAsync()).Status);
        Assert.Equal(2, await db.TeamMembers.CountAsync());
        Assert.All(await db.TeamMembers.ToListAsync(), m => Assert.Equal(ComponentDDataSeeder.TeamId, m.RescueTeamId));
        var vehicle = await db.Vehicles.SingleAsync();
        Assert.Equal(ComponentDDataSeeder.VehicleId, vehicle.Id);
        Assert.Equal(VehicleStatus.Available, vehicle.Status);
        Assert.Empty(db.Assignments);
        Assert.Empty(db.Dispatches);
    }

    [Fact]
    public async Task OccupiedFixtureIdNeverOverwritesOperatorOrAddsChildren()
    {
        using var db = TestDbFactory.Create();
        using var shared = Shared();
        db.RescueTeams.Add(new RescueTeam { Id = ComponentDDataSeeder.TeamId, Name = "Operator team", Status = TeamStatus.OffDuty });
        await db.SaveChangesAsync();
        await Seed(db, shared);
        var team = await db.RescueTeams.SingleAsync();
        Assert.Equal("Operator team", team.Name);
        Assert.Equal(TeamStatus.OffDuty, team.Status);
        Assert.Empty(db.TeamMembers);
        Assert.Empty(db.Vehicles);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid-guid")]
    [InlineData("11111111-1111-4111-8111-111111111111")]
    public async Task MissingOrInvalidIncidentSkipsHistoricalFixture(string? reference)
    {
        using var db = TestDbFactory.Create();
        using var shared = Shared();
        await Seed(db, shared, reference);
        Assert.Single(db.RescueTeams);
        Assert.Empty(db.Assignments);
        Assert.Empty(db.Dispatches);
        Assert.Empty(shared.Incidents);
    }

    [Fact]
    public async Task ValidIncidentSeedsTerminalFixtureWithoutApprovalIdentityAndWithoutChangingIncident()
    {
        using var db = TestDbFactory.Create();
        using var shared = Shared();
        var incident = new Incident { Title = "Test incident", Description = "Isolated test fixture" };
        shared.Incidents.Add(incident);
        await shared.SaveChangesAsync();
        var original = incident.UpdatedAt;
        await Seed(db, shared, incident.Id.ToString());
        await Seed(db, shared, incident.Id.ToString());
        var assignment = await db.Assignments.SingleAsync();
        var dispatch = await db.Dispatches.SingleAsync();
        Assert.Equal(ComponentDDataSeeder.AssignmentId, assignment.Id);
        Assert.Equal(incident.Id, assignment.IncidentId);
        Assert.NotEqual(ComponentDDataSeeder.TeamId, assignment.RescueTeamId);
        Assert.Equal(AssignmentStatus.Approved, assignment.Status);
        Assert.Equal(assignment.Id, dispatch.AssignmentId);
        Assert.Equal(DispatchStatus.Resolved, dispatch.Status);
        Assert.Equal(ApprovalStatus.Approved, dispatch.ApprovalStatus);
        Assert.Null(dispatch.ApprovedByUserId);
        Assert.True(dispatch.ApprovedAt < dispatch.DispatchedAt);
        Assert.True(dispatch.DispatchedAt < dispatch.EnRouteAt);
        Assert.True(dispatch.EnRouteAt < dispatch.OnSceneAt);
        Assert.True(dispatch.OnSceneAt < dispatch.ResolvedAt);
        Assert.Null(dispatch.CancelledAt);
        Assert.Equal(original, incident.UpdatedAt);
        Assert.Equal(EntityState.Unchanged, shared.Entry(incident).State);
        Assert.Equal(2, await db.RescueTeams.CountAsync());
        Assert.Equal(3, await db.TeamMembers.CountAsync());
        Assert.Equal(2, await db.Vehicles.CountAsync());
    }

    [Theory]
    [InlineData("Production", "true")]
    [InlineData("Development", null)]
    [InlineData("Development", "false")]
    public async Task SeederRequiresDevelopmentAndExplicitOptIn(string environment, string? enabled)
    {
        using var db = TestDbFactory.Create();
        using var shared = Shared();
        await Seed(db, shared, environment: environment, enabled: enabled);
        Assert.Empty(db.RescueTeams);
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

internal static class AuditTestQueries
{
    public static IEnumerable<IComponentDAuditable> SetForAudit(this ComponentDDbContext db, string kind) => kind switch
    {
        "team" => db.RescueTeams.ToList(),
        "member" => db.TeamMembers.ToList(),
        "vehicle" => db.Vehicles.ToList(),
        "assignment" => db.Assignments.ToList(),
        _ => db.Dispatches.ToList()
    };
}
