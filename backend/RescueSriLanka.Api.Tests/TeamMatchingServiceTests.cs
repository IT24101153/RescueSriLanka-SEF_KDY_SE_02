using RescueSriLanka.Api.DTOs;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Services;
using Xunit;

namespace RescueSriLanka.Api.Tests
{
    public class TeamMatchingServiceTests
    {
        [Fact]
        public async Task ExcludesTeamsWithNoAvailableSkilledMember()
        {
            using var db = TestDbFactory.Create();

            var team = new RescueTeam { Name = "Alpha", Status = TeamStatus.Available };
            team.Members.Add(new TeamMember
            {
                FullName = "Nimal",
                Phone = "0770000000",
                Skill = SkillType.FirstAid,
                IsAvailable = false // not available — should be excluded
            });
            db.RescueTeams.Add(team);
            await db.SaveChangesAsync();

            var service = new TeamMatchingService(db);
            var results = await service.FindMatchesAsync(
                new MatchRequestDto(SkillType.FirstAid, null, null, null));

            Assert.Empty(results);
        }

        [Fact]
        public async Task IncludesTeamWithAvailableSkilledMember()
        {
            using var db = TestDbFactory.Create();

            var team = new RescueTeam { Name = "Bravo", Status = TeamStatus.Available };
            team.Members.Add(new TeamMember
            {
                FullName = "Kamal",
                Phone = "0771111111",
                Skill = SkillType.WaterRescue,
                IsAvailable = true
            });
            db.RescueTeams.Add(team);
            await db.SaveChangesAsync();

            var service = new TeamMatchingService(db);
            var results = await service.FindMatchesAsync(
                new MatchRequestDto(SkillType.WaterRescue, null, null, null));

            Assert.Single(results);
            Assert.Equal("Bravo", results[0].TeamName);
            Assert.Equal(1, results[0].MatchingAvailableMembers);
        }

        [Theory]
        [InlineData(TeamStatus.OnMission)]
        [InlineData(TeamStatus.OffDuty)]
        public async Task ExcludesTeamsThatAreNotAvailable(TeamStatus status)
        {
            using var db = TestDbFactory.Create();

            var team = new RescueTeam { Name = "Unavailable", Status = status };
            team.Members.Add(new TeamMember
            {
                FullName = "Kusal",
                Phone = "0773333333",
                Skill = SkillType.WaterRescue,
                IsAvailable = true
            });
            db.RescueTeams.Add(team);
            await db.SaveChangesAsync();

            var results = await new TeamMatchingService(db).FindMatchesAsync(
                new MatchRequestDto(SkillType.WaterRescue, null, null, null));

            Assert.Empty(results);
        }

        [Fact]
        public async Task ExcludesTeamWithoutVehicleMeetingMinCapacity()
        {
            using var db = TestDbFactory.Create();

            var team = new RescueTeam { Name = "Charlie", Status = TeamStatus.Available };
            team.Members.Add(new TeamMember
            {
                FullName = "Saman",
                Phone = "0772222222",
                Skill = SkillType.Paramedic,
                IsAvailable = true
            });
            team.Vehicles.Add(new Vehicle
            {
                PlateNumber = "ABC-1234",
                Type = VehicleType.Ambulance,
                Status = VehicleStatus.Available,
                Capacity = 2 // requested min capacity will be 4
            });
            db.RescueTeams.Add(team);
            await db.SaveChangesAsync();

            var service = new TeamMatchingService(db);
            var results = await service.FindMatchesAsync(
                new MatchRequestDto(SkillType.Paramedic, null, null, MinCapacity: 4));

            Assert.Empty(results);
        }

        [Fact]
        public async Task RanksTeamWithMoreAvailableSkilledMembersHigher()
        {
            using var db = TestDbFactory.Create();

            var small = new RescueTeam { Name = "SmallTeam", Status = TeamStatus.Available };
            small.Members.Add(new TeamMember { FullName = "A", Phone = "1", Skill = SkillType.FireResponse, IsAvailable = true });

            var big = new RescueTeam { Name = "BigTeam", Status = TeamStatus.Available };
            big.Members.Add(new TeamMember { FullName = "B", Phone = "2", Skill = SkillType.FireResponse, IsAvailable = true });
            big.Members.Add(new TeamMember { FullName = "C", Phone = "3", Skill = SkillType.FireResponse, IsAvailable = true });

            db.RescueTeams.AddRange(small, big);
            await db.SaveChangesAsync();

            var service = new TeamMatchingService(db);
            var results = await service.FindMatchesAsync(
                new MatchRequestDto(SkillType.FireResponse, null, null, null));

            Assert.Equal("BigTeam", results[0].TeamName); // higher score comes first
        }
    }
}
