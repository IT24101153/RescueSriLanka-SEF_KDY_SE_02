using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentB.Models;
using RescueSriLanka.Api.Features.ComponentD.Data;
using RescueSriLanka.Api.Features.ComponentD.DTOs;
using RescueSriLanka.Api.Features.ComponentD.Models;
using RescueSriLanka.Api.Features.ComponentD.Services;

namespace RescueSriLanka.Api.Tests;

public sealed class HelpRequestCoordinationTests : IDisposable
{
    private readonly AppDbContext shared = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly ComponentDDbContext db = TestDbFactory.Create();
    private readonly HelpRequest request = new() { Type = HelpRequestType.Rescue, Description = "Need transport", Latitude = 7, Longitude = 80, VerificationStatus = VerificationStatus.Verified };
    private HelpRequestCandidateService Candidates => new(db, new HelpRequestReadService(shared));
    private AssignmentService Assignments => new(db, new IncidentReadService(shared), Candidates);

    public HelpRequestCoordinationTests() { shared.HelpRequests.Add(request); shared.SaveChanges(); }
    public void Dispose() { shared.Dispose(); db.Dispose(); }

    private async Task<RescueTeam> AddTeam(double? latitude = 7.01, double? longitude = 80, int capacity = 4)
    {
        var team = new RescueTeam { Name = "Rescue base", BaseLatitude = latitude, BaseLongitude = longitude };
        team.Members.Add(new TeamMember { FullName = "Medic", Phone = "0712345678", Skill = SkillType.FirstAid, IsAvailable = true });
        team.Vehicles.Add(new Vehicle { PlateNumber = Guid.NewGuid().ToString(), Type = VehicleType.Ambulance, Capacity = capacity });
        db.RescueTeams.Add(team); await db.SaveChangesAsync(); return team;
    }
    private CreateAssignmentDto Proposal(RescueTeam team) => new(null, request.Id, team.Id, team.Vehicles.Single().Id, SkillType.FirstAid, 2, "Confirmed transport demand");

    [Theory]
    [InlineData(HelpRequestStatus.Pending, VerificationStatus.Verified, true)]
    [InlineData(HelpRequestStatus.Pending, VerificationStatus.PendingVerification, false)]
    [InlineData(HelpRequestStatus.Pending, VerificationStatus.RejectedFake, false)]
    [InlineData(HelpRequestStatus.Assigned, VerificationStatus.Verified, false)]
    [InlineData(HelpRequestStatus.InProgress, VerificationStatus.Verified, false)]
    [InlineData(HelpRequestStatus.Resolved, VerificationStatus.Verified, false)]
    [InlineData(HelpRequestStatus.Cancelled, VerificationStatus.Verified, false)]
    public async Task QueueAndCreationRespectEligibility(HelpRequestStatus status, VerificationStatus verification, bool eligible)
    {
        request.Status = status; request.VerificationStatus = verification; await shared.SaveChangesAsync();
        Assert.Equal(eligible ? 1 : 0, (await Candidates.GetQueueAsync()).Count);
        var team = await AddTeam();
        var (assignment, error) = await Assignments.CreateAsync(Proposal(team));
        Assert.Equal(eligible, assignment is not null);
        if (eligible) { Assert.Null(error); Assert.Null(assignment!.IncidentId); Assert.Equal(request.Id, assignment.HelpRequestId); }
        else { Assert.Contains("unavailable", error); Assert.Empty(db.Assignments); }
        Assert.Equal(status, (await shared.HelpRequests.AsNoTracking().SingleAsync()).Status);
        Assert.Empty(shared.RequestStatusHistories);
    }

    [Fact]
    public async Task MissingHelpRequestAndDualObjectivesAreRejected()
    {
        var team = await AddTeam();
        var missing = await Assignments.CreateAsync(Proposal(team) with { HelpRequestId = Guid.NewGuid() });
        Assert.Null(missing.Assignment); Assert.Contains("unavailable", missing.Error);
        var both = await Assignments.CreateAsync(Proposal(team) with { IncidentId = Guid.NewGuid() });
        Assert.Null(both.Assignment); Assert.Contains("exactly one", both.Error);
        Assert.Empty(db.Assignments);
    }

    [Fact]
    public async Task CurrentWorkIsExcludedAndRevisionKeepsObjective()
    {
        var team = await AddTeam();
        var created = (await Assignments.CreateAsync(Proposal(team))).Assignment!;
        Assert.Empty(await Candidates.GetQueueAsync());
        var anotherTeam = await AddTeam(7.02);
        var duplicate = await Assignments.CreateAsync(Proposal(anotherTeam));
        Assert.Null(duplicate.Assignment); Assert.Contains("current response work", duplicate.Error);
        var revision = await Assignments.ReviseAsync(created.Id, new(team.Id, team.Vehicles.Single().Id, SkillType.FirstAid, 3, "Updated"));
        Assert.Null(revision.Error); Assert.Equal(request.Id, revision.Assignment!.HelpRequestId); Assert.Null(revision.Assignment.IncidentId);
        Assert.Equal(2, revision.Assignment.PlanVersion);
    }

    [Theory]
    [InlineData("team")]
    [InlineData("skill")]
    [InlineData("member")]
    [InlineData("vehicle")]
    [InlineData("capacity")]
    [InlineData("noVehicle")]
    public async Task UnsuitableResourcesAreExcluded(string fault)
    {
        var team = await AddTeam();
        switch (fault)
        {
            case "team": team.Status = TeamStatus.OffDuty; break;
            case "skill": team.Members.Single().Skill = SkillType.Driving; break;
            case "member": team.Members.Single().IsAvailable = false; break;
            case "vehicle": team.Vehicles.Single().Status = VehicleStatus.UnderMaintenance; break;
            case "capacity": team.Vehicles.Single().Capacity = 1; break;
            case "noVehicle": db.Vehicles.Remove(team.Vehicles.Single()); break;
        }
        await db.SaveChangesAsync();
        Assert.Empty(await Candidates.FindAsync(request.Id, SkillType.FirstAid, 2));
    }

    [Theory]
    [InlineData(null, 80d)]
    [InlineData(7d, null)]
    [InlineData(91d, 80d)]
    [InlineData(7d, 181d)]
    [InlineData(double.NaN, 80d)]
    [InlineData(7d, double.PositiveInfinity)]
    public async Task InvalidBasesAreExcluded(double? latitude, double? longitude)
    {
        await AddTeam(latitude, longitude);
        Assert.Empty(await Candidates.FindAsync(request.Id, SkillType.FirstAid, 2));
    }

    [Theory]
    [InlineData(91d, 80d)]
    [InlineData(7d, 181d)]
    [InlineData(double.NaN, 80d)]
    [InlineData(7d, double.PositiveInfinity)]
    public async Task InvalidRequestLocationFailsSafely(double latitude, double longitude)
    {
        request.Latitude = latitude; request.Longitude = longitude; await shared.SaveChangesAsync();
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => Candidates.FindAsync(request.Id, SkillType.FirstAid, 2));
        Assert.Contains("location", exception.Message);
    }

    [Theory]
    [InlineData(DispatchStatus.Pending, false)]
    [InlineData(DispatchStatus.Dispatched, false)]
    [InlineData(DispatchStatus.EnRoute, false)]
    [InlineData(DispatchStatus.OnScene, false)]
    [InlineData(DispatchStatus.Resolved, true)]
    [InlineData(DispatchStatus.Cancelled, true)]
    public async Task TeamAndVehicleConflictsFollowDispatchLifecycle(DispatchStatus status, bool available)
    {
        var team = await AddTeam();
        var assignment = new Assignment { IncidentId = Guid.NewGuid(), RescueTeamId = team.Id, VehicleId = team.Vehicles.Single().Id };
        db.Assignments.Add(assignment); db.Dispatches.Add(new Dispatch { AssignmentId = assignment.Id, Status = status });
        await db.SaveChangesAsync();
        Assert.Equal(available ? 1 : 0, (await Candidates.FindAsync(request.Id, SkillType.FirstAid, 2)).Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UndispatchedWorkBlocksTeamOrVehicleIndependently(bool teamConflict)
    {
        var team = await AddTeam();
        db.Assignments.Add(new Assignment { IncidentId = Guid.NewGuid(), RescueTeamId = teamConflict ? team.Id : Guid.NewGuid(), VehicleId = teamConflict ? Guid.NewGuid() : team.Vehicles.Single().Id });
        await db.SaveChangesAsync();
        Assert.Empty(await Candidates.FindAsync(request.Id, SkillType.FirstAid, 2));
        db.Assignments.Single().Status = AssignmentStatus.Rejected; await db.SaveChangesAsync();
        Assert.Single(await Candidates.FindAsync(request.Id, SkillType.FirstAid, 2));
    }

    [Fact]
    public async Task NearestWinsRegardlessOfMemberCountAndTiesAreStable()
    {
        var far = await AddTeam(7.5); db.TeamMembers.Add(new TeamMember { RescueTeamId = far.Id, FullName = "Extra", Phone = "0712345678", Skill = SkillType.FirstAid });
        var near = await AddTeam(7.01);
        var tied = await AddTeam(7.01);
        await db.SaveChangesAsync();
        var candidates = await Candidates.FindAsync(request.Id, SkillType.FirstAid, 2);
        Assert.Equal(far.Id, candidates.Last().TeamId);
        Assert.Equal(new[] { near.Id, tied.Id }.Order(), candidates.Take(2).Select(c => c.TeamId));
        Assert.InRange(candidates[0].DistanceKm, 1.1, 1.2);
        Assert.Equal(candidates, await Candidates.FindAsync(request.Id, SkillType.FirstAid, 2));
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("team", false)]
    [InlineData("vehicle", false)]
    [InlineData("otherCandidate", false)]
    [InlineData("failure", false)]
    [InlineData("inventedFact", false)]
    public async Task AiCannotReplaceTrustedFactsOrMutateResources(string mode, bool aiAvailable)
    {
        var team = await AddTeam(); await AddTeam(7.5);
        var service = new HelpRequestRecommendationService(Candidates, new Explanation(mode));
        var result = await service.RecommendAsync(request.Id, SkillType.FirstAid, 2);
        Assert.Equal(aiAvailable, result.AiAvailable);
        Assert.Equal(2, result.Candidates.Count); Assert.Equal(team.Id, result.RecommendedCandidate!.TeamId);
        Assert.Equal(4, result.RecommendedCandidate.VehicleCapacity);
        Assert.Empty(db.Assignments); Assert.Empty(db.Dispatches);
        Assert.All(await db.RescueTeams.AsNoTracking().ToListAsync(), t => Assert.Equal(TeamStatus.Available, t.Status));
        Assert.All(await db.Vehicles.AsNoTracking().ToListAsync(), v => Assert.Equal(VehicleStatus.Available, v.Status));
        Assert.Equal(HelpRequestStatus.Pending, (await shared.HelpRequests.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task ExplicitDemandAndSkillAreRequiredAndNoCandidatesSkipsAi()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Candidates.FindAsync(request.Id, SkillType.FirstAid, 0));
        await Assert.ThrowsAsync<ArgumentException>(() => Candidates.FindAsync(request.Id, (SkillType)99, 2));
        var result = await new HelpRequestRecommendationService(Candidates, new Explanation("failure")).RecommendAsync(request.Id, SkillType.FirstAid, 2);
        Assert.Empty(result.Candidates); Assert.Null(result.RecommendedCandidate); Assert.False(result.AiAvailable);
    }

    private sealed class Explanation(string mode) : IRescueRecommendationExplanation
    {
        public Task<RescueAiExplanation?> ExplainAsync(IReadOnlyList<RescueCandidateDto> candidates, CancellationToken ct)
        {
            if (mode == "failure") throw new InvalidOperationException("Offline");
            var selected = mode == "otherCandidate" ? candidates.Last() : candidates[0];
            return Task.FromResult<RescueAiExplanation?>(new(mode == "team" ? Guid.NewGuid() : selected.TeamId,
                mode == "vehicle" ? Guid.NewGuid() : selected.VehicleId, mode == "inventedFact" ? ["NEAREST_BASE", "Capacity is 999"] : ["NEAREST_BASE", "MATCHING_SKILL", "TRANSPORT_CAPACITY", "AVAILABLE_RESOURCES"]));
        }
    }
}
