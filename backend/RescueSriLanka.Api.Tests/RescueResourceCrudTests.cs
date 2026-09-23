using System.ComponentModel.DataAnnotations;
using System.Reflection;
using RescueSriLanka.Api.Features.ComponentD.DTOs;
using RescueSriLanka.Api.Features.ComponentD.Models;
using RescueSriLanka.Api.Features.ComponentD.Services;
using Xunit;

namespace RescueSriLanka.Api.Tests;

public class RescueResourceCrudTests
{
    [Fact]
    public async Task CoordinatorResourceServiceCanCreateUpdateAndRemoveAnUnusedTeamMemberAndVehicle()
    {
        using var db = TestDbFactory.Create();
        var service = new RescueTeamService(db);
        var team = await service.CreateAsync(new CreateRescueTeamDto("CRUD Test", null, null));
        var updatedTeam = await service.UpdateAsync(team.Id, new UpdateRescueTeamDto("CRUD Test Updated", TeamStatus.Available, null, null));
        var member = await service.AddMemberAsync(team.Id, new CreateTeamMemberDto("Member One", "+94111234567", SkillType.FirstAid));
        var vehicle = await service.AddVehicleAsync(team.Id, new CreateVehicleDto("CRUD-1", VehicleType.Ambulance, 2));
        var updatedMember = await service.UpdateMemberAsync(team.Id, member!.Id, new UpdateTeamMemberDto("Member Two", "+94111234567", SkillType.Paramedic, true));
        var updatedVehicle = await service.UpdateVehicleAsync(team.Id, vehicle!.Id, new UpdateVehicleDto("CRUD-2", VehicleType.Boat, VehicleStatus.Available, 3));

        Assert.Equal("CRUD Test Updated", updatedTeam!.Name);
        Assert.Equal(SkillType.Paramedic, updatedMember.Member!.Skill);
        Assert.Equal(3, updatedVehicle.Vehicle!.Capacity);
        Assert.True((await service.DeleteMemberAsync(team.Id, member.Id)).Success);
        Assert.True((await service.DeleteVehicleAsync(team.Id, vehicle.Id)).Success);
        Assert.True((await service.DeleteAsync(team.Id)).Success);
    }

    [Fact]
    public async Task CannotRemoveVehicleOrMemberWhenTeamHasAnActiveAssignment()
    {
        using var db = TestDbFactory.Create();
        var team = new RescueTeam { Name = "Reserved", Status = TeamStatus.Available };
        var member = new TeamMember { RescueTeamId = team.Id, FullName = "Member", Phone = "+94111234567", Skill = SkillType.FirstAid };
        var vehicle = new Vehicle { RescueTeamId = team.Id, PlateNumber = "RES-1", Type = VehicleType.Ambulance, Capacity = 2, Status = VehicleStatus.Available };
        var assignment = new Assignment { IncidentId = Guid.NewGuid(), RescueTeamId = team.Id, VehicleId = vehicle.Id, RequiredSkill = SkillType.FirstAid, RequiredCapacity = 1 };
        db.AddRange(team, member, vehicle, assignment); await db.SaveChangesAsync();
        var service = new RescueTeamService(db);

        Assert.Contains("active", (await service.DeleteMemberAsync(team.Id, member.Id)).Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("active", (await service.DeleteVehicleAsync(team.Id, vehicle.Id)).Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResourceRecordValidationUsesConstructorParameterTargets()
    {
        var createVehicle = typeof(CreateVehicleDto).GetConstructors().Single().GetParameters();
        var createMember = typeof(CreateTeamMemberDto).GetConstructors().Single().GetParameters();
        Assert.NotNull(createVehicle.Single(p => string.Equals(p.Name, "Capacity", StringComparison.OrdinalIgnoreCase)).GetCustomAttribute<RangeAttribute>());
        Assert.NotNull(createMember.Single(p => string.Equals(p.Name, "FullName", StringComparison.OrdinalIgnoreCase)).GetCustomAttribute<RequiredAttribute>());
    }
}
