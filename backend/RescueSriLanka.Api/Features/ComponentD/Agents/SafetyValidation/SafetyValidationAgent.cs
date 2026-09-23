using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.DTOs;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Features.ComponentD.DTOs;
using RescueSriLanka.Api.Features.ComponentD.Data;
using RescueSriLanka.Api.Features.ComponentD.Models;

namespace RescueSriLanka.Api.Features.ComponentD.Agents.SafetyValidation;
public interface ISafetyValidationAgent
{
    Task<SafetyValidationResultDto> ValidateAsync(Guid assignmentId);
}

// Deterministic guardrail for a persisted Component D assignment plan.
public class SafetyValidationAgent : ISafetyValidationAgent
{
    private readonly ComponentDDbContext _db;

    public SafetyValidationAgent(ComponentDDbContext db)
    {
        _db = db;
    }

    public async Task<SafetyValidationResultDto> ValidateAsync(Guid assignmentId)
    {
        var issues = new List<string>();

        var assignment = await _db.Assignments
            .Include(a => a.RescueTeam)
                .ThenInclude(t => t!.Members)
            .Include(a => a.Vehicle)
            .FirstOrDefaultAsync(a => a.Id == assignmentId);

        if (assignment is null || assignment.RescueTeam is null)
        {
            issues.Add("Assignment or its rescue team could not be found.");
            return new SafetyValidationResultDto(false, issues, DateTime.UtcNow);
        }

        var team = assignment.RescueTeam;
        if (!team.Members.Any(m => m.IsAvailable && m.Skill == assignment.RequiredSkill))
            issues.Add($"No available team member with required skill '{assignment.RequiredSkill}'.");

        if (team.Status != TeamStatus.Available)
            issues.Add("Rescue team is not currently available.");

        var hasTeamConflict = await _db.Dispatches
            .Where(d => d.Assignment != null
                        && d.Assignment.RescueTeamId == team.Id
                        && d.AssignmentId != assignment.Id)
            .AnyAsync(d => d.Status != DispatchStatus.Resolved && d.Status != DispatchStatus.Cancelled);
        if (hasTeamConflict)
            issues.Add("Rescue team already has an active dispatch in progress (double-booking).");

        var vehicle = assignment.Vehicle;
        if (vehicle is null)
        {
            issues.Add("Assignment has no selected vehicle.");
        }
        else
        {
            if (vehicle.RescueTeamId != team.Id)
                issues.Add("Selected vehicle does not belong to the rescue team.");
            if (vehicle.Status != VehicleStatus.Available)
                issues.Add("Selected vehicle is not available.");
            if (vehicle.Capacity < assignment.RequiredCapacity)
                issues.Add("Selected vehicle does not meet the required capacity.");

            var hasVehicleConflict = await _db.Dispatches
                .Where(d => d.Assignment != null
                            && d.Assignment.VehicleId == vehicle.Id
                            && d.AssignmentId != assignment.Id)
                .AnyAsync(d => d.Status != DispatchStatus.Resolved && d.Status != DispatchStatus.Cancelled);
            if (hasVehicleConflict)
                issues.Add("Selected vehicle already has an active dispatch in progress (double-booking).");
        }

        return new SafetyValidationResultDto(issues.Count == 0, issues, DateTime.UtcNow);
    }
}
