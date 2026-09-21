using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.DTOs;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Agents.SafetyValidation
{
    // This is Component D's agent from the Agentic AI subsystem.
    // Deliberately NOT calling an LLM: the rubric requires "deterministic
    // validation" as a distinct step from the AI-generated plan, so this
    // agent is plain rule-based code. It's still wired in as a step the
    // Coordinator/Planner Agent (Student B) delegates to.
    public interface ISafetyValidationAgent
    {
        Task<SafetyValidationResultDto> ValidateAsync(Guid assignmentId);
    }

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
                .Include(a => a.RescueTeam)
                    .ThenInclude(t => t!.Vehicles)
                .FirstOrDefaultAsync(a => a.Id == assignmentId);

            if (assignment is null || assignment.RescueTeam is null)
            {
                issues.Add("Assignment or its rescue team could not be found.");
                return new SafetyValidationResultDto(false, issues, DateTime.UtcNow);
            }

            var team = assignment.RescueTeam;

            // Check 1: Required-skill match — the team must have at least
            // one currently available member with the required skill.
            var skilledAvailable = team.Members
                .Count(m => m.IsAvailable && m.Skill == assignment.RequiredSkill);
            if (skilledAvailable == 0)
            {
                issues.Add($"No available team member with required skill '{assignment.RequiredSkill}'.");
            }

            // Check 2: Team capacity — team must not be OffDuty.
            if (team.Status == TeamStatus.OffDuty)
            {
                issues.Add("Rescue team is currently off duty.");
            }

            // Check 3: No double-booking — the team should not already have
            // an active (non-resolved/cancelled) dispatch tied to a
            // DIFFERENT assignment. Compared by AssignmentId rather than
            // assignment.Dispatch (which isn't loaded here and may not
            // exist yet at validation time).
            var hasActiveDispatch = await _db.Dispatches
                .Where(d => d.Assignment != null
                            && d.Assignment.RescueTeamId == team.Id
                            && d.AssignmentId != assignment.Id)
                .AnyAsync(d => d.Status != DispatchStatus.Resolved && d.Status != DispatchStatus.Cancelled);

            if (hasActiveDispatch)
            {
                issues.Add("Rescue team already has an active dispatch in progress (double-booking).");
            }

            // Check 4: At least one available vehicle exists for the team.
            var hasAvailableVehicle = team.Vehicles.Any(v => v.Status == VehicleStatus.Available);
            if (!hasAvailableVehicle)
            {
                issues.Add("No available vehicle assigned to this team.");
            }

            return new SafetyValidationResultDto(issues.Count == 0, issues, DateTime.UtcNow);
        }
    }
}
