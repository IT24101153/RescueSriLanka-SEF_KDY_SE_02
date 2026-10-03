using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Features.ComponentA.Models;
using RescueSriLanka.Api.Features.ComponentD.DTOs;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentD.Data;
using RescueSriLanka.Api.Features.ComponentD.Models;
using RescueSriLanka.Api.Features.ComponentD.Services;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Xunit;

namespace RescueSriLanka.Api.Tests;

public class AssignmentServiceTests : IDisposable
{
    private readonly AppDbContext _incidentDb = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Incident _incident = new() { Title = "Flood in Kandy", Description = "Existing incident", IsActive = true };

    public AssignmentServiceTests()
    {
        _incidentDb.Incidents.Add(_incident);
        _incidentDb.SaveChanges();
    }

    public void Dispose() => _incidentDb.Dispose();

    [Fact]
    public async Task RejectsMissingIncidentWithoutSavingAssignment()
    {
        using var db = TestDbFactory.Create();
        var (team, vehicle) = await AddTeamAsync(db, "Alpha", SkillType.FirstAid);
        var request = CreateDto(team.Id, vehicle.Id, SkillType.FirstAid) with { IncidentId = Guid.NewGuid() };
        var (assignment, error) = await new AssignmentService(db, new IncidentReadService(_incidentDb), new HelpRequestCandidateService(db, new HelpRequestReadService(_incidentDb)), new RescueSriLanka.Api.Features.ComponentB.Services.HelpRequestResponseStatusService(_incidentDb)).CreateAsync(request);
        Assert.Null(assignment);
        Assert.Contains("no longer exists", error);
        Assert.Empty(db.Assignments);
    }

    [Theory]
    [InlineData(false, IncidentStatus.Verified)]
    [InlineData(true, IncidentStatus.Resolved)]
    [InlineData(true, IncidentStatus.Rejected)]
    public async Task RejectsIneligibleIncident(bool active, IncidentStatus status)
    {
        using var db = TestDbFactory.Create();
        var (team, vehicle) = await AddTeamAsync(db, "Alpha", SkillType.FirstAid);
        _incident.IsActive = active;
        _incident.Status = status;
        await _incidentDb.SaveChangesAsync();
        var (assignment, error) = await new AssignmentService(db, new IncidentReadService(_incidentDb), new HelpRequestCandidateService(db, new HelpRequestReadService(_incidentDb)), new RescueSriLanka.Api.Features.ComponentB.Services.HelpRequestResponseStatusService(_incidentDb))
            .CreateAsync(CreateDto(team.Id, vehicle.Id, SkillType.FirstAid));
        Assert.Null(assignment);
        Assert.Contains("no longer active", error);
        Assert.Empty(db.Assignments);
    }

    [Theory]
    [InlineData(IncidentStatus.Reported)]
    [InlineData(IncidentStatus.Verified)]
    [InlineData(IncidentStatus.InProgress)]
    public async Task PreservesActiveIncidentIdAcrossCreateAndRevision(IncidentStatus status)
    {
        using var db = TestDbFactory.Create();
        var (team, vehicle) = await AddTeamAsync(db, "Alpha", SkillType.FirstAid);
        _incident.Status = status;
        await _incidentDb.SaveChangesAsync();
        var service = new AssignmentService(db, new IncidentReadService(_incidentDb), new HelpRequestCandidateService(db, new HelpRequestReadService(_incidentDb)), new RescueSriLanka.Api.Features.ComponentB.Services.HelpRequestResponseStatusService(_incidentDb));
        var (created, createError) = await service.CreateAsync(CreateDto(team.Id, vehicle.Id, SkillType.FirstAid));
        Assert.Null(createError);
        Assert.Equal(_incident.Id, created!.IncidentId);
        var (revised, reviseError) = await service.ReviseAsync(created.Id, ReviseDto(team.Id, vehicle.Id, SkillType.FirstAid, 2));
        Assert.Null(reviseError);
        Assert.Equal(_incident.Id, revised!.IncidentId);
        Assert.Equal(2, revised.PlanVersion);
    }

    [Fact]
    public async Task RejectsRevisionWhenIncidentBecameInactive()
    {
        using var db = TestDbFactory.Create();
        var (team, vehicle) = await AddTeamAsync(db, "Alpha", SkillType.FirstAid);
        var service = new AssignmentService(db, new IncidentReadService(_incidentDb), new HelpRequestCandidateService(db, new HelpRequestReadService(_incidentDb)), new RescueSriLanka.Api.Features.ComponentB.Services.HelpRequestResponseStatusService(_incidentDb));
        var created = (await service.CreateAsync(CreateDto(team.Id, vehicle.Id, SkillType.FirstAid))).Assignment!;
        _incident.IsActive = false;
        await _incidentDb.SaveChangesAsync();
        var (revised, error) = await service.ReviseAsync(created.Id, ReviseDto(team.Id, vehicle.Id, SkillType.FirstAid, 2));
        Assert.Null(revised);
        Assert.Contains("no longer active", error);
        Assert.Equal(1, (await db.Assignments.SingleAsync()).PlanVersion);
    }

    [Fact]
    public void AssignmentRequestValidationAttributesAreAppliedToRecordConstructorParameters()
    {
        var createParameters = typeof(CreateAssignmentDto).GetConstructors().Single().GetParameters();
        var reviseParameters = typeof(ReviseAssignmentDto).GetConstructors().Single().GetParameters();

        Assert.NotNull(createParameters.Single(p => string.Equals(p.Name, "requiredCapacity", StringComparison.OrdinalIgnoreCase)).GetCustomAttribute<RangeAttribute>());
        Assert.NotNull(createParameters.Single(p => string.Equals(p.Name, "notes", StringComparison.OrdinalIgnoreCase)).GetCustomAttribute<MaxLengthAttribute>());
        Assert.NotNull(reviseParameters.Single(p => string.Equals(p.Name, "requiredCapacity", StringComparison.OrdinalIgnoreCase)).GetCustomAttribute<RangeAttribute>());
        Assert.NotNull(reviseParameters.Single(p => string.Equals(p.Name, "notes", StringComparison.OrdinalIgnoreCase)).GetCustomAttribute<MaxLengthAttribute>());
    }

    [Fact]
    public async Task CreatesAssignmentWithValidTeamAndVehicleAtPlanVersionOne()
    {
        using var db = TestDbFactory.Create();
        var (team, vehicle) = await AddTeamAsync(db, "Alpha", SkillType.FirstAid);

        var (assignment, error) = await new AssignmentService(db, new IncidentReadService(_incidentDb), new HelpRequestCandidateService(db, new HelpRequestReadService(_incidentDb)), new RescueSriLanka.Api.Features.ComponentB.Services.HelpRequestResponseStatusService(_incidentDb)).CreateAsync(CreateDto(team.Id, vehicle.Id, SkillType.FirstAid));

        Assert.Null(error);
        Assert.NotNull(assignment);
        Assert.Equal(vehicle.Id, assignment!.VehicleId);
        Assert.Equal(1, assignment.RequiredCapacity);
        Assert.Equal(AssignmentStatus.Proposed, assignment.Status);
        Assert.Equal(1, assignment.PlanVersion);
    }

    [Fact]
    public async Task RejectsVehicleThatBelongsToAnotherTeam()
    {
        using var db = TestDbFactory.Create();
        var (teamA, _) = await AddTeamAsync(db, "Alpha", SkillType.FirstAid);
        var (_, vehicleB) = await AddTeamAsync(db, "Bravo", SkillType.FirstAid);

        var (assignment, error) = await new AssignmentService(db, new IncidentReadService(_incidentDb), new HelpRequestCandidateService(db, new HelpRequestReadService(_incidentDb)), new RescueSriLanka.Api.Features.ComponentB.Services.HelpRequestResponseStatusService(_incidentDb)).CreateAsync(CreateDto(teamA.Id, vehicleB.Id, SkillType.FirstAid));

        Assert.Null(assignment);
        Assert.Contains("belong", error);
    }

    [Fact]
    public async Task RejectsUnavailableVehicle()
    {
        using var db = TestDbFactory.Create();
        var (team, vehicle) = await AddTeamAsync(db, "Alpha", SkillType.FirstAid, vehicleStatus: VehicleStatus.UnderMaintenance);

        var (assignment, error) = await new AssignmentService(db, new IncidentReadService(_incidentDb), new HelpRequestCandidateService(db, new HelpRequestReadService(_incidentDb)), new RescueSriLanka.Api.Features.ComponentB.Services.HelpRequestResponseStatusService(_incidentDb)).CreateAsync(CreateDto(team.Id, vehicle.Id, SkillType.FirstAid));

        Assert.Null(assignment);
        Assert.Contains("Vehicle must be available", error);
    }

    [Fact]
    public async Task RejectsUnavailableTeam()
    {
        using var db = TestDbFactory.Create();
        var (team, vehicle) = await AddTeamAsync(db, "Alpha", SkillType.FirstAid, teamStatus: TeamStatus.OnMission);

        var (assignment, error) = await new AssignmentService(db, new IncidentReadService(_incidentDb), new HelpRequestCandidateService(db, new HelpRequestReadService(_incidentDb)), new RescueSriLanka.Api.Features.ComponentB.Services.HelpRequestResponseStatusService(_incidentDb)).CreateAsync(CreateDto(team.Id, vehicle.Id, SkillType.FirstAid));

        Assert.Null(assignment);
        Assert.Contains("team must be available", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsNonPositiveRequiredCapacity()
    {
        using var db = TestDbFactory.Create();
        var (team, vehicle) = await AddTeamAsync(db, "Alpha", SkillType.FirstAid);

        var (assignment, error) = await new AssignmentService(db, new IncidentReadService(_incidentDb), new HelpRequestCandidateService(db, new HelpRequestReadService(_incidentDb)), new RescueSriLanka.Api.Features.ComponentB.Services.HelpRequestResponseStatusService(_incidentDb)).CreateAsync(CreateDto(team.Id, vehicle.Id, SkillType.FirstAid, capacity: 0));

        Assert.Null(assignment);
        Assert.Contains("greater than zero", error);
    }

    [Fact]
    public async Task RejectsTeamWithoutRequiredSkill()
    {
        using var db = TestDbFactory.Create();
        var (team, vehicle) = await AddTeamAsync(db, "Alpha", SkillType.FirstAid);

        var (assignment, error) = await new AssignmentService(db, new IncidentReadService(_incidentDb), new HelpRequestCandidateService(db, new HelpRequestReadService(_incidentDb)), new RescueSriLanka.Api.Features.ComponentB.Services.HelpRequestResponseStatusService(_incidentDb)).CreateAsync(CreateDto(team.Id, vehicle.Id, SkillType.Paramedic));

        Assert.Null(assignment);
        Assert.Contains("required skill", error);
    }

    [Fact]
    public async Task RejectsVehicleAlreadyCommittedToAnotherActiveAssignment()
    {
        using var db = TestDbFactory.Create();
        var (team, vehicle) = await AddTeamAsync(db, "Alpha", SkillType.FirstAid);
        var service = new AssignmentService(db, new IncidentReadService(_incidentDb), new HelpRequestCandidateService(db, new HelpRequestReadService(_incidentDb)), new RescueSriLanka.Api.Features.ComponentB.Services.HelpRequestResponseStatusService(_incidentDb));
        Assert.NotNull((await service.CreateAsync(CreateDto(team.Id, vehicle.Id, SkillType.FirstAid))).Assignment);

        var (assignment, error) = await service.CreateAsync(CreateDto(team.Id, vehicle.Id, SkillType.FirstAid));

        Assert.Null(assignment);
        Assert.Contains("Vehicle is already committed", error);
    }

    [Fact]
    public async Task RejectsTeamAlreadyCommittedToAnotherActiveAssignment()
    {
        using var db = TestDbFactory.Create();
        var (team, vehicleOne) = await AddTeamAsync(db, "Alpha", SkillType.FirstAid);
        var vehicleTwo = new Vehicle
        {
            PlateNumber = "Alpha-2",
            Type = VehicleType.Ambulance,
            Status = VehicleStatus.Available,
            Capacity = 4,
            RescueTeamId = team.Id
        };
        db.Vehicles.Add(vehicleTwo);
        await db.SaveChangesAsync();

        var service = new AssignmentService(db, new IncidentReadService(_incidentDb), new HelpRequestCandidateService(db, new HelpRequestReadService(_incidentDb)), new RescueSriLanka.Api.Features.ComponentB.Services.HelpRequestResponseStatusService(_incidentDb));
        Assert.NotNull((await service.CreateAsync(CreateDto(team.Id, vehicleOne.Id, SkillType.FirstAid))).Assignment);

        var (assignment, error) = await service.CreateAsync(CreateDto(team.Id, vehicleTwo.Id, SkillType.FirstAid));

        Assert.Null(assignment);
        Assert.Contains("Rescue team is already committed", error);
    }

    [Fact]
    public async Task RevisingTeamIncrementsPlanVersion()
    {
        using var db = TestDbFactory.Create();
        var (firstTeam, firstVehicle) = await AddTeamAsync(db, "Alpha", SkillType.FirstAid);
        var (secondTeam, secondVehicle) = await AddTeamAsync(db, "Bravo", SkillType.FirstAid);
        var service = new AssignmentService(db, new IncidentReadService(_incidentDb), new HelpRequestCandidateService(db, new HelpRequestReadService(_incidentDb)), new RescueSriLanka.Api.Features.ComponentB.Services.HelpRequestResponseStatusService(_incidentDb));
        var created = (await service.CreateAsync(CreateDto(firstTeam.Id, firstVehicle.Id, SkillType.FirstAid))).Assignment!;

        var (revised, error) = await service.ReviseAsync(created.Id, ReviseDto(secondTeam.Id, secondVehicle.Id, SkillType.FirstAid));

        Assert.Null(error);
        Assert.Equal(2, revised!.PlanVersion);
        Assert.Equal(secondTeam.Id, revised.RescueTeamId);
    }

    [Fact]
    public async Task RevisingVehicleIncrementsPlanVersion()
    {
        using var db = TestDbFactory.Create();
        var (team, vehicleOne) = await AddTeamAsync(db, "Alpha", SkillType.FirstAid);
        var vehicleTwo = await AddVehicleAsync(db, team.Id, "Alpha-2");
        var service = new AssignmentService(db, new IncidentReadService(_incidentDb), new HelpRequestCandidateService(db, new HelpRequestReadService(_incidentDb)), new RescueSriLanka.Api.Features.ComponentB.Services.HelpRequestResponseStatusService(_incidentDb));
        var created = (await service.CreateAsync(CreateDto(team.Id, vehicleOne.Id, SkillType.FirstAid))).Assignment!;

        var (revised, error) = await service.ReviseAsync(created.Id, ReviseDto(team.Id, vehicleTwo.Id, SkillType.FirstAid));

        Assert.Null(error);
        Assert.Equal(2, revised!.PlanVersion);
        Assert.Equal(vehicleTwo.Id, revised.VehicleId);
    }

    [Fact]
    public async Task RevisingRequiredSkillIncrementsPlanVersion()
    {
        using var db = TestDbFactory.Create();
        var (team, vehicle) = await AddTeamAsync(db, "Alpha", SkillType.FirstAid, SkillType.Paramedic);
        var service = new AssignmentService(db, new IncidentReadService(_incidentDb), new HelpRequestCandidateService(db, new HelpRequestReadService(_incidentDb)), new RescueSriLanka.Api.Features.ComponentB.Services.HelpRequestResponseStatusService(_incidentDb));
        var created = (await service.CreateAsync(CreateDto(team.Id, vehicle.Id, SkillType.FirstAid))).Assignment!;

        var (revised, error) = await service.ReviseAsync(created.Id, ReviseDto(team.Id, vehicle.Id, SkillType.Paramedic));

        Assert.Null(error);
        Assert.Equal(2, revised!.PlanVersion);
        Assert.Equal(SkillType.Paramedic, revised.RequiredSkill);
    }

    [Fact]
    public async Task RevisingRequiredCapacityIncrementsPlanVersion()
    {
        using var db = TestDbFactory.Create();
        var (team, vehicle) = await AddTeamAsync(db, "Alpha", SkillType.FirstAid, capacity: 4);
        var service = new AssignmentService(db, new IncidentReadService(_incidentDb), new HelpRequestCandidateService(db, new HelpRequestReadService(_incidentDb)), new RescueSriLanka.Api.Features.ComponentB.Services.HelpRequestResponseStatusService(_incidentDb));
        var created = (await service.CreateAsync(CreateDto(team.Id, vehicle.Id, SkillType.FirstAid))).Assignment!;

        var (revised, error) = await service.ReviseAsync(created.Id, ReviseDto(team.Id, vehicle.Id, SkillType.FirstAid, capacity: 2));

        Assert.Null(error);
        Assert.Equal(2, revised!.PlanVersion);
        Assert.Equal(2, revised.RequiredCapacity);
    }

    [Fact]
    public void ClientDtosCannotSetPlanVersion()
    {
        Assert.Null(typeof(CreateAssignmentDto).GetProperty("PlanVersion"));
        Assert.Null(typeof(ReviseAssignmentDto).GetProperty("PlanVersion"));
    }

    [Fact]
    public async Task ApprovedAssignmentCannotBeRevised()
    {
        using var db = TestDbFactory.Create();
        var (team, vehicle) = await AddTeamAsync(db, "Alpha", SkillType.FirstAid);
        var service = new AssignmentService(db, new IncidentReadService(_incidentDb), new HelpRequestCandidateService(db, new HelpRequestReadService(_incidentDb)), new RescueSriLanka.Api.Features.ComponentB.Services.HelpRequestResponseStatusService(_incidentDb));
        var created = (await service.CreateAsync(CreateDto(team.Id, vehicle.Id, SkillType.FirstAid))).Assignment!;
        var entity = await db.Assignments.FindAsync(created.Id);
        entity!.Status = AssignmentStatus.Approved;
        await db.SaveChangesAsync();

        var (revised, error) = await service.ReviseAsync(created.Id, ReviseDto(team.Id, vehicle.Id, SkillType.FirstAid, capacity: 2));

        Assert.Null(revised);
        Assert.Contains("cannot be revised", error);
    }

    [Theory]
    [InlineData(DispatchStatus.Resolved)]
    [InlineData(DispatchStatus.Cancelled)]
    [InlineData(DispatchStatus.OnScene)]
    public async Task DispatchedAssignmentCannotBeRevisedEvenWhenRejected(DispatchStatus status)
    {
        using var db = TestDbFactory.Create();
        var (team, vehicle) = await AddTeamAsync(db, "History", SkillType.FirstAid);
        var service = new AssignmentService(db, new IncidentReadService(_incidentDb), new HelpRequestCandidateService(db, new HelpRequestReadService(_incidentDb)), new RescueSriLanka.Api.Features.ComponentB.Services.HelpRequestResponseStatusService(_incidentDb));
        var created = (await service.CreateAsync(CreateDto(team.Id, vehicle.Id, SkillType.FirstAid))).Assignment!;
        var entity = await db.Assignments.FindAsync(created.Id);
        entity!.Status = AssignmentStatus.Rejected;
        db.Dispatches.Add(new Dispatch { AssignmentId = created.Id, Status = status });
        await db.SaveChangesAsync();
        var (revised, error) = await service.ReviseAsync(created.Id, ReviseDto(team.Id, vehicle.Id, SkillType.FirstAid, capacity: 2));
        Assert.Null(revised);
        Assert.Contains("cannot be revised", error);
        Assert.Equal(created.PlanVersion, entity.PlanVersion);
        Assert.Equal(1, entity.RequiredCapacity);
        Assert.Equal(status, (await db.Dispatches.SingleAsync()).Status);
    }

    private CreateAssignmentDto CreateDto(Guid teamId, Guid vehicleId, SkillType skill, int capacity = 1) =>
        new(_incident.Id, null, teamId, vehicleId, skill, capacity, null);

    private static ReviseAssignmentDto ReviseDto(Guid teamId, Guid vehicleId, SkillType skill, int capacity = 1) =>
        new(teamId, vehicleId, skill, capacity, null);

    private static async Task<(RescueTeam Team, Vehicle Vehicle)> AddTeamAsync(
        ComponentDDbContext db,
        string name,
        SkillType firstSkill,
        SkillType? secondSkill = null,
        TeamStatus teamStatus = TeamStatus.Available,
        VehicleStatus vehicleStatus = VehicleStatus.Available,
        int capacity = 4)
    {
        var team = new RescueTeam { Name = name, Status = teamStatus };
        team.Members.Add(new TeamMember { FullName = $"{name}-member", Phone = "0770000000", Skill = firstSkill, IsAvailable = true });
        if (secondSkill.HasValue)
            team.Members.Add(new TeamMember { FullName = $"{name}-member-2", Phone = "0770000001", Skill = secondSkill.Value, IsAvailable = true });
        db.RescueTeams.Add(team);
        await db.SaveChangesAsync();

        var vehicle = new Vehicle
        {
            PlateNumber = $"{name}-1",
            Type = VehicleType.Ambulance,
            Status = vehicleStatus,
            Capacity = capacity,
            RescueTeamId = team.Id
        };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();
        return (team, vehicle);
    }

    private static async Task<Vehicle> AddVehicleAsync(ComponentDDbContext db, Guid teamId, string plateNumber)
    {
        var vehicle = new Vehicle
        {
            PlateNumber = plateNumber,
            Type = VehicleType.Ambulance,
            Status = VehicleStatus.Available,
            Capacity = 4,
            RescueTeamId = teamId
        };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();
        return vehicle;
    }
}
