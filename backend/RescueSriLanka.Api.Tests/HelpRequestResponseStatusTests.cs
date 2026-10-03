using RescueSriLanka.Api.Features.ComponentD.Agents.SafetyValidation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentB.Models;
using RescueSriLanka.Api.Features.ComponentB.Services;
using RescueSriLanka.Api.Features.ComponentD.Data;
using RescueSriLanka.Api.Features.ComponentD.Models;
using RescueSriLanka.Api.Features.ComponentD.Services;

namespace RescueSriLanka.Api.Tests;

public sealed class HelpRequestResponseStatusTests
{
    [Theory]
    [InlineData(HelpRequestStatus.Pending, HelpRequestStatus.Assigned, 1)]
    [InlineData(HelpRequestStatus.Assigned, HelpRequestStatus.InProgress, 1)]
    [InlineData(HelpRequestStatus.InProgress, HelpRequestStatus.InProgress, 0)]
    [InlineData(HelpRequestStatus.InProgress, HelpRequestStatus.Resolved, 1)]
    [InlineData(HelpRequestStatus.Resolved, HelpRequestStatus.Resolved, 0)]
    [InlineData(HelpRequestStatus.Pending, HelpRequestStatus.Resolved, 3)]
    public async Task SynchronizationIsIdempotentAndLegacyPlansFollowLegalHistory(HelpRequestStatus initial, HelpRequestStatus target, int changes)
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var request = new HelpRequest { Status = initial, UpdatedAt = DateTime.UtcNow.AddDays(-1) };
        db.HelpRequests.Add(request); await db.SaveChangesAsync();
        var oldUpdated = request.UpdatedAt;
        var service = new HelpRequestResponseStatusService(db);
        await service.SynchronizeAsync(request.Id, target, null);
        var updated = request.UpdatedAt;
        await service.SynchronizeAsync(request.Id, target, null);
        Assert.Equal(target, request.Status);
        Assert.Equal(changes, await db.RequestStatusHistories.CountAsync());
        Assert.Equal(updated, request.UpdatedAt);
        Assert.Equal(changes > 0, updated > oldUpdated);
    }

    [Theory]
    [InlineData(HelpRequestStatus.Cancelled, HelpRequestStatus.Assigned)]
    [InlineData(HelpRequestStatus.Cancelled, HelpRequestStatus.InProgress)]
    [InlineData(HelpRequestStatus.Resolved, HelpRequestStatus.InProgress)]
    [InlineData(HelpRequestStatus.InProgress, HelpRequestStatus.Assigned)]
    public async Task InvalidCurrentStateIsNotOverwritten(HelpRequestStatus initial, HelpRequestStatus target)
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var request = new HelpRequest { Status = initial };
        db.HelpRequests.Add(request); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new HelpRequestResponseStatusService(db).SynchronizeAsync(request.Id, target, null));
        Assert.Equal(initial, request.Status);
        Assert.Empty(db.RequestStatusHistories);
    }

    // Real relational transactions exercise the two-context enlistment; these
    // tests create only a disposable in-memory SQLite schema, never migrations.
    private sealed class RelationalFixture : IAsyncDisposable
    {
        public SqliteConnection Connection { get; } = new("Data Source=:memory:;Foreign Keys=False");
        public AppDbContext Shared { get; }
        public ComponentDDbContext Response { get; }
        public RelationalFixture()
        {
            Shared = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(Connection).Options);
            Response = new(new DbContextOptionsBuilder<ComponentDDbContext>().UseSqlite(Connection).Options);
        }
        public async Task Init()
        {
            await Connection.OpenAsync();
            await Shared.Database.EnsureCreatedAsync();
            await Response.Database.ExecuteSqlRawAsync(Response.Database.GenerateCreateScript());
        }
        public async ValueTask DisposeAsync()
        { await Shared.DisposeAsync(); await Response.DisposeAsync(); await Connection.DisposeAsync(); }
    }

    [Theory]
    [InlineData("none")]
    [InlineData("history")]
    [InlineData("assignment")]
    public async Task AssignmentAndStatusShareCommitOrRollback(string failure)
    {
        await using var fixture = new RelationalFixture(); await fixture.Init();
        var shared = fixture.Shared; var db = fixture.Response;
        var failHistory = failure != "none";
        var request = new HelpRequest { Latitude = 7, Longitude = 80, VerificationStatus = VerificationStatus.Verified };
        shared.Add(request); await shared.SaveChangesAsync();
        var team = new RescueTeam { Name = "Rescue", BaseLatitude = 7, BaseLongitude = 80 };
        team.Members.Add(new TeamMember { FullName = "Medic", Phone = "0712345678", Skill = SkillType.FirstAid });
        team.Vehicles.Add(new Vehicle { PlateNumber = "SYNC", Capacity = 4 });
        db.Add(team); await db.SaveChangesAsync();
        if (failHistory)
            await shared.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_history BEFORE INSERT ON RequestStatusHistories BEGIN SELECT RAISE(ABORT, 'Injected history failure'); END;");
        if (failure == "assignment")
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_assignment BEFORE INSERT ON Assignments BEGIN SELECT RAISE(ABORT, 'Injected assignment failure'); END;");
        var service = new AssignmentService(db, new IncidentReadService(shared),
            new HelpRequestCandidateService(db, new HelpRequestReadService(shared)), new HelpRequestResponseStatusService(shared));
        var result = await service.CreateAsync(new(null, request.Id, team.Id, team.Vehicles.Single().Id, SkillType.FirstAid, 2, null));
        Assert.Equal(!failHistory, result.Assignment is not null);
        db.ChangeTracker.Clear(); shared.ChangeTracker.Clear();
        Assert.Equal(failHistory ? 0 : 1, await db.Assignments.CountAsync());
        Assert.Equal(failHistory ? HelpRequestStatus.Pending : HelpRequestStatus.Assigned, (await shared.HelpRequests.SingleAsync()).Status);
        Assert.Equal(failHistory ? 0 : 1, await shared.RequestStatusHistories.CountAsync());
    }

    [Theory]
    [InlineData(false, DispatchStatus.OnScene, DispatchStatus.Resolved, HelpRequestStatus.InProgress, HelpRequestStatus.Resolved)]
    [InlineData(true, DispatchStatus.OnScene, DispatchStatus.Resolved, HelpRequestStatus.InProgress, HelpRequestStatus.Resolved)]
    [InlineData(false, DispatchStatus.Pending, DispatchStatus.Dispatched, HelpRequestStatus.Assigned, HelpRequestStatus.InProgress)]
    [InlineData(true, DispatchStatus.Pending, DispatchStatus.Dispatched, HelpRequestStatus.Assigned, HelpRequestStatus.InProgress)]
    public async Task DispatchAndHistoryShareCommitOrRollback(bool failHistory, DispatchStatus previous, DispatchStatus next,
        HelpRequestStatus requestBefore, HelpRequestStatus requestAfter)
    {
        await using var fixture = new RelationalFixture(); await fixture.Init();
        var shared = fixture.Shared; var db = fixture.Response;
        var request = new HelpRequest { Status = requestBefore };
        shared.Add(request); await shared.SaveChangesAsync();
        var team = new RescueTeam { Name = "Rescue", Status = TeamStatus.OnMission };
        var vehicle = new Vehicle { RescueTeamId = team.Id, PlateNumber = "SYNC", Capacity = 4, Status = VehicleStatus.InUse };
        var assignment = new Assignment { HelpRequestId = request.Id, RescueTeamId = team.Id, VehicleId = vehicle.Id, Status = AssignmentStatus.Approved };
        var dispatch = new Dispatch { AssignmentId = assignment.Id, Status = previous, ApprovalStatus = ApprovalStatus.Approved };
        db.AddRange(team, vehicle, assignment, dispatch); await db.SaveChangesAsync();
        if (failHistory)
            await shared.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_history BEFORE INSERT ON RequestStatusHistories BEGIN SELECT RAISE(ABORT, 'Injected history failure'); END;");
        var service = new DispatchService(db, new SafetyValidationAgent(db), new HelpRequestResponseStatusService(shared));
        if (failHistory)
            await Assert.ThrowsAsync<DbUpdateException>(() => service.TransitionStatusAsync(dispatch.Id, new(next, null)));
        else
            Assert.True((await service.TransitionStatusAsync(dispatch.Id, new(next, null))).Success);
        db.ChangeTracker.Clear(); shared.ChangeTracker.Clear();
        Assert.Equal(failHistory ? previous : next, (await db.Dispatches.SingleAsync()).Status);
        Assert.Equal(failHistory ? requestBefore : requestAfter, (await shared.HelpRequests.SingleAsync()).Status);
        Assert.Equal(failHistory || next != DispatchStatus.Resolved ? TeamStatus.OnMission : TeamStatus.Available, (await db.RescueTeams.SingleAsync()).Status);
        Assert.Equal(failHistory ? 0 : 1, await shared.RequestStatusHistories.CountAsync());
    }

    [Theory]
    [InlineData(HelpRequestStatus.Pending)]
    [InlineData(HelpRequestStatus.Assigned)]
    [InlineData(HelpRequestStatus.InProgress)]
    [InlineData(HelpRequestStatus.Resolved)]
    [InlineData(HelpRequestStatus.Cancelled)]
    public async Task RecoordinationValidatesStateAndIsIdempotent(HelpRequestStatus initial)
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var request = new HelpRequest { Status = initial, VerificationStatus = VerificationStatus.Verified, UpdatedAt = DateTime.UtcNow.AddDays(-1) };
        db.Add(request); await db.SaveChangesAsync();
        var before = request.UpdatedAt;
        var service = new HelpRequestResponseStatusService(db);
        var assignmentId = Guid.NewGuid();
        if (initial is HelpRequestStatus.Pending or HelpRequestStatus.Assigned)
        {
            await service.ReturnToPendingForRecoordinationAsync(request.Id, assignmentId, null);
            var updated = request.UpdatedAt;
            await service.ReturnToPendingForRecoordinationAsync(request.Id, assignmentId, null);
            Assert.Equal(HelpRequestStatus.Pending, request.Status);
            Assert.Equal(initial == HelpRequestStatus.Assigned ? 1 : 0, await db.RequestStatusHistories.CountAsync());
            Assert.Equal(updated, request.UpdatedAt);
            Assert.Equal(initial == HelpRequestStatus.Assigned, updated > before);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReturnToPendingForRecoordinationAsync(request.Id, assignmentId, null));
            Assert.Equal(initial, request.Status);
            Assert.Equal(before, request.UpdatedAt);
            Assert.Empty(db.RequestStatusHistories);
        }
        Assert.Equal(VerificationStatus.Verified, request.VerificationStatus);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("assignment")]
    [InlineData("history")]
    [InlineData("invalidStatus")]
    [InlineData("otherPlan")]
    [InlineData("incident")]
    public async Task CancellationAndRecoordinationAreAtomicAndGuarded(string scenario)
    {
        await using var fixture = new RelationalFixture(); await fixture.Init();
        var shared = fixture.Shared; var db = fixture.Response;
        var request = new HelpRequest { Status = scenario == "invalidStatus" ? HelpRequestStatus.InProgress : HelpRequestStatus.Assigned,
            VerificationStatus = VerificationStatus.Verified, UpdatedAt = DateTime.UtcNow.AddDays(-1) };
        var initialStatus = request.Status;
        var initialUpdated = request.UpdatedAt;
        shared.Add(request); await shared.SaveChangesAsync();
        var team = new RescueTeam { Name = "Rescue" };
        var vehicle = new Vehicle { RescueTeamId = team.Id, PlateNumber = "CANCEL", Capacity = 4 };
        var assignment = new Assignment { HelpRequestId = scenario == "incident" ? null : request.Id,
            IncidentId = scenario == "incident" ? Guid.NewGuid() : null, RescueTeamId = team.Id, VehicleId = vehicle.Id };
        db.AddRange(team, vehicle, assignment); await db.SaveChangesAsync();
        if (scenario == "history")
            await shared.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_reset_history BEFORE INSERT ON RequestStatusHistories BEGIN SELECT RAISE(ABORT, 'Injected reset history failure'); END;");
        if (scenario == "assignment")
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_cancel BEFORE UPDATE ON Assignments BEGIN SELECT RAISE(ABORT, 'Injected cancellation failure'); END;");
        if (scenario == "otherPlan")
        {
            db.Add(new Assignment { HelpRequestId = request.Id, RescueTeamId = team.Id, VehicleId = vehicle.Id });
            await db.SaveChangesAsync();
        }
        IHelpRequestResponseStatusService status = scenario == "incident" ? new RejectHelpRequestResponseStatusService() : new HelpRequestResponseStatusService(shared);
        var service = new AssignmentService(db, new IncidentReadService(shared), new HelpRequestCandidateService(db, new HelpRequestReadService(shared)), status);
        if (scenario == "invalidStatus")
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CancelAsync(assignment.Id));
        else
        {
            var result = await service.CancelAsync(assignment.Id);
            Assert.Equal(scenario is "none" or "incident", result.Assignment is not null);
        }
        db.ChangeTracker.Clear(); shared.ChangeTracker.Clear();
        Assert.Equal(scenario is "none" or "incident" ? AssignmentStatus.Cancelled : AssignmentStatus.Proposed,
            (await db.Assignments.SingleAsync(a => a.Id == assignment.Id)).Status);
        var saved = await shared.HelpRequests.SingleAsync();
        Assert.Equal(scenario == "none" ? HelpRequestStatus.Pending : initialStatus, saved.Status);
        Assert.Equal(VerificationStatus.Verified, saved.VerificationStatus);
        Assert.Equal(scenario == "none", saved.UpdatedAt > initialUpdated);
        Assert.Equal(scenario == "none" ? 1 : 0, await shared.RequestStatusHistories.CountAsync());
    }
}
