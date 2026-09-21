using System.Text.Json;
using RescueSriLanka.Api.Agents.SafetyValidation;
using RescueSriLanka.Api.DTOs;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Models.Agents;
using RescueSriLanka.Api.Services;
using Xunit;

namespace RescueSriLanka.Api.Tests;

public class SafeDispatchDecisionTests
{
    [Fact]
    public async Task Approve_CommitsDispatchAndReservations_AndIsIdempotent()
    {
        using var db = TestDbFactory.Create(); var (a, w) = await SeedAsync(db); var service = new DispatchService(db, new SafetyValidationAgent(db));
        var dto = new CoordinatorDecisionDto(w.Id, a.PlanVersion, CoordinatorDecision.APPROVE, "ok");
        var first = await service.DecideAsync(a.Id, "coordinator", dto); var second = await service.DecideAsync(a.Id, "coordinator", dto);
        if (!first.Success) throw new InvalidOperationException(first.Error);
        Assert.True(second.Idempotent); Assert.Single(db.Dispatches);
        Assert.Equal(AssignmentStatus.Approved, (await db.Assignments.FindAsync(a.Id))!.Status);
        Assert.Equal(TeamStatus.OnMission, (await db.RescueTeams.FindAsync(a.RescueTeamId))!.Status);
        Assert.Equal(VehicleStatus.InUse, (await db.Vehicles.FindAsync(a.VehicleId))!.Status);
        Assert.Equal(WorkflowStatus.Executing, (await db.AgentWorkflows.FindAsync(w.Id))!.Status);
    }

    [Fact]
    public async Task InvalidBindingOrDecision_CreatesNoDispatch()
    {
        using var db = TestDbFactory.Create(); var (a, w) = await SeedAsync(db); var service = new DispatchService(db, new SafetyValidationAgent(db));
        var stale = await service.DecideAsync(a.Id, "c", new(w.Id, a.PlanVersion + 1, CoordinatorDecision.APPROVE, null));
        var wrong = await service.DecideAsync(a.Id, "c", new(Guid.NewGuid(), a.PlanVersion, CoordinatorDecision.APPROVE, null));
        Assert.False(stale.Success); Assert.False(wrong.Success); Assert.Empty(db.Dispatches);
    }

    [Theory]
    [InlineData(CoordinatorDecision.REJECT, AssignmentStatus.Rejected, WorkflowStatus.Rejected)]
    [InlineData(CoordinatorDecision.REVISE, AssignmentStatus.Proposed, WorkflowStatus.AwaitingApproval)]
    public async Task RejectOrRevise_NeverReservesResources(CoordinatorDecision decision, AssignmentStatus status, WorkflowStatus workflowStatus)
    {
        using var db = TestDbFactory.Create(); var (a, w) = await SeedAsync(db); var service = new DispatchService(db, new SafetyValidationAgent(db));
        var result = await service.DecideAsync(a.Id, "c", new(w.Id, a.PlanVersion, decision, null));
        Assert.True(result.Success); Assert.Empty(db.Dispatches); Assert.Equal(status, (await db.Assignments.FindAsync(a.Id))!.Status);
        Assert.Equal(workflowStatus, (await db.AgentWorkflows.FindAsync(w.Id))!.Status);
        Assert.Equal(TeamStatus.Available, (await db.RescueTeams.FindAsync(a.RescueTeamId))!.Status);
    }

    [Fact]
    public async Task Resolved_ReleasesResourcesAndCompletesMatchingWorkflow()
    {
        using var db = TestDbFactory.Create(); var (a, w) = await SeedAsync(db); var service = new DispatchService(db, new SafetyValidationAgent(db));
        var approved = await service.DecideAsync(a.Id, "c", new(w.Id, a.PlanVersion, CoordinatorDecision.APPROVE, null));
        var transitioned = await service.TransitionStatusAsync(approved.Dispatch!.Id, new(DispatchStatus.EnRoute, null));
        Assert.True(transitioned.Success); Assert.Equal(TeamStatus.OnMission, (await db.RescueTeams.FindAsync(a.RescueTeamId))!.Status);
        var resolved = await service.TransitionStatusAsync(approved.Dispatch.Id, new(DispatchStatus.OnScene, null)); Assert.True(resolved.Success);
        resolved = await service.TransitionStatusAsync(approved.Dispatch.Id, new(DispatchStatus.Resolved, null)); Assert.True(resolved.Success);
        Assert.Equal(TeamStatus.Available, (await db.RescueTeams.FindAsync(a.RescueTeamId))!.Status);
        Assert.Equal(VehicleStatus.Available, (await db.Vehicles.FindAsync(a.VehicleId))!.Status);
        Assert.Equal(WorkflowStatus.Completed, (await db.AgentWorkflows.FindAsync(w.Id))!.Status);
    }

    private static async Task<(Assignment Assignment, AgentWorkflow Workflow)> SeedAsync(Data.ComponentDDbContext db)
    {
        var team = new RescueTeam { Name = "T", Status = TeamStatus.Available };
        team.Members.Add(new TeamMember { FullName = "M", Phone = "1", Skill = SkillType.Paramedic, IsAvailable = true });
        var vehicle = new Vehicle { PlateNumber = Guid.NewGuid().ToString()[..8], Capacity = 2, Status = VehicleStatus.Available, RescueTeamId = team.Id };
        var assignment = new Assignment { IncidentId = Guid.NewGuid(), RescueTeamId = team.Id, VehicleId = vehicle.Id, RequiredSkill = SkillType.Paramedic, RequiredCapacity = 1, PlanVersion = 2 };
        db.AddRange(team, vehicle, assignment); await db.SaveChangesAsync();
        var checks = new[] { "ASSIGNMENT_EXISTS", "PLAN_VERSION_MATCHES", "TEAM_AVAILABLE", "REQUIRED_SKILL_PRESENT", "TEAM_CONFLICT", "VEHICLE_EXISTS", "VEHICLE_OWNERSHIP", "VEHICLE_AVAILABLE", "VEHICLE_CAPACITY", "VEHICLE_CONFLICT" }.Select(n => new SafetyValidationCheckDto(n, true, "ok")).ToList();
        var outcome = new SafetyValidationWorkflowResultDto(null, assignment.Id, assignment.PlanVersion, SafetyValidationDecision.APPROVE, "ok", checks, [], [], WorkflowStatus.AwaitingApproval, false);
        var workflow = new AgentWorkflow { ObjectiveType = WorkflowObjectiveType.Incident, ObjectiveId = assignment.IncidentId!.Value, Status = WorkflowStatus.AwaitingApproval, FinalOutcomeJson = JsonSerializer.Serialize(outcome) };
        db.AgentWorkflows.Add(workflow); await db.SaveChangesAsync(); return (assignment, workflow);
    }
}
