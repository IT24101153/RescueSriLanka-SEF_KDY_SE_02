using RescueSriLanka.Api.Features.ComponentD.Agents.SafetyValidation;
using RescueSriLanka.Api.Features.ComponentD.Models;
using Xunit;

namespace RescueSriLanka.Api.Tests
{
    public class SafetyValidationAgentTests
    {
        [Fact]
        public async Task PassesWhenAllConditionsAreMet()
        {
            using var db = TestDbFactory.Create();

            var team = new RescueTeam { Name = "Echo", Status = TeamStatus.Available };
            team.Members.Add(new TeamMember { FullName = "A", Phone = "1", Skill = SkillType.Logistics, IsAvailable = true });
            var vehicle = new Vehicle { PlateNumber = "V1", Type = VehicleType.Truck, Status = VehicleStatus.Available, Capacity = 3 };
            team.Vehicles.Add(vehicle);
            db.RescueTeams.Add(team);

            var assignment = new Assignment { RescueTeamId = team.Id, VehicleId = vehicle.Id, RequiredSkill = SkillType.Logistics };
            db.Assignments.Add(assignment);
            await db.SaveChangesAsync();

            var agent = new SafetyValidationAgent(db);
            var result = await agent.ValidateAsync(assignment.Id);

            Assert.True(result.Passed);
            Assert.Empty(result.Issues);
        }

        [Fact]
        public async Task FailsWhenNoMemberHasRequiredSkill()
        {
            using var db = TestDbFactory.Create();

            var team = new RescueTeam { Name = "Foxtrot", Status = TeamStatus.Available };
            team.Members.Add(new TeamMember { FullName = "A", Phone = "1", Skill = SkillType.Driving, IsAvailable = true });
            var vehicle = new Vehicle { PlateNumber = "V2", Type = VehicleType.Truck, Status = VehicleStatus.Available, Capacity = 3 };
            team.Vehicles.Add(vehicle);
            db.RescueTeams.Add(team);

            var assignment = new Assignment { RescueTeamId = team.Id, VehicleId = vehicle.Id, RequiredSkill = SkillType.Paramedic };
            db.Assignments.Add(assignment);
            await db.SaveChangesAsync();

            var agent = new SafetyValidationAgent(db);
            var result = await agent.ValidateAsync(assignment.Id);

            Assert.False(result.Passed);
            Assert.Contains(result.Issues, i => i.Contains("required skill"));
        }

        [Fact]
        public async Task FailsWhenTeamIsOffDuty()
        {
            using var db = TestDbFactory.Create();

            var team = new RescueTeam { Name = "Golf", Status = TeamStatus.OffDuty };
            team.Members.Add(new TeamMember { FullName = "A", Phone = "1", Skill = SkillType.FirstAid, IsAvailable = true });
            var vehicle = new Vehicle { PlateNumber = "V3", Type = VehicleType.Ambulance, Status = VehicleStatus.Available, Capacity = 2 };
            team.Vehicles.Add(vehicle);
            db.RescueTeams.Add(team);

            var assignment = new Assignment { RescueTeamId = team.Id, VehicleId = vehicle.Id, RequiredSkill = SkillType.FirstAid };
            db.Assignments.Add(assignment);
            await db.SaveChangesAsync();

            var agent = new SafetyValidationAgent(db);
            var result = await agent.ValidateAsync(assignment.Id);

            Assert.False(result.Passed);
            Assert.Contains(result.Issues, i => i.Contains("not currently available"));
        }

        [Fact]
        public async Task FailsWhenNoVehicleIsAvailable()
        {
            using var db = TestDbFactory.Create();

            var team = new RescueTeam { Name = "Hotel", Status = TeamStatus.Available };
            team.Members.Add(new TeamMember { FullName = "A", Phone = "1", Skill = SkillType.FireResponse, IsAvailable = true });
            var vehicle = new Vehicle { PlateNumber = "V4", Type = VehicleType.FireTruck, Status = VehicleStatus.UnderMaintenance, Capacity = 5 };
            team.Vehicles.Add(vehicle);
            db.RescueTeams.Add(team);

            var assignment = new Assignment { RescueTeamId = team.Id, VehicleId = vehicle.Id, RequiredSkill = SkillType.FireResponse };
            db.Assignments.Add(assignment);
            await db.SaveChangesAsync();

            var agent = new SafetyValidationAgent(db);
            var result = await agent.ValidateAsync(assignment.Id);

            Assert.False(result.Passed);
            Assert.Contains(result.Issues, i => i.Contains("vehicle"));
        }

        [Fact]
        public async Task FailsWhenTeamAlreadyHasAnActiveDispatchOnAnotherAssignment()
        {
            using var db = TestDbFactory.Create();

            var team = new RescueTeam { Name = "India", Status = TeamStatus.Available };
            team.Members.Add(new TeamMember { FullName = "A", Phone = "1", Skill = SkillType.WaterRescue, IsAvailable = true });
            var vehicle = new Vehicle { PlateNumber = "V5", Type = VehicleType.Boat, Status = VehicleStatus.Available, Capacity = 4 };
            team.Vehicles.Add(vehicle);
            db.RescueTeams.Add(team);

            var existingAssignment = new Assignment { RescueTeamId = team.Id, VehicleId = vehicle.Id, RequiredSkill = SkillType.WaterRescue };
            var newAssignment = new Assignment { RescueTeamId = team.Id, VehicleId = vehicle.Id, RequiredSkill = SkillType.WaterRescue };
            db.Assignments.AddRange(existingAssignment, newAssignment);
            await db.SaveChangesAsync();

            db.Dispatches.Add(new Dispatch
            {
                AssignmentId = existingAssignment.Id,
                Status = DispatchStatus.EnRoute // active, non-terminal
            });
            await db.SaveChangesAsync();

            var agent = new SafetyValidationAgent(db);
            var result = await agent.ValidateAsync(newAssignment.Id);

            Assert.False(result.Passed);
            Assert.Contains(result.Issues, i => i.Contains("double-booking"));
        }
    }
}
