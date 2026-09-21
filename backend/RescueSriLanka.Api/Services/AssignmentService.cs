using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.DTOs;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Services;

public interface IAssignmentService
{
    Task<List<AssignmentDto>> GetAllAsync();
    Task<AssignmentDto?> GetByIdAsync(Guid id);
    Task<(AssignmentDto? Assignment, string? Error)> CreateAsync(CreateAssignmentDto dto);
    Task<(AssignmentDto? Assignment, string? Error)> ReviseAsync(Guid id, ReviseAssignmentDto dto);
}

public class AssignmentService : IAssignmentService
{
    private readonly ComponentDDbContext _db;

    public AssignmentService(ComponentDDbContext db)
    {
        _db = db;
    }

    public async Task<List<AssignmentDto>> GetAllAsync()
    {
        var assignments = await AssignmentQuery().ToListAsync();
        return assignments.Select(ToDto).ToList();
    }

    public async Task<AssignmentDto?> GetByIdAsync(Guid id)
    {
        var assignment = await AssignmentQuery().FirstOrDefaultAsync(a => a.Id == id);
        return assignment is null ? null : ToDto(assignment);
    }

    public async Task<(AssignmentDto? Assignment, string? Error)> CreateAsync(CreateAssignmentDto dto)
    {
        var error = await ValidateProposalAsync(
            dto.IncidentId,
            dto.HelpRequestId,
            dto.RescueTeamId,
            dto.VehicleId,
            dto.RequiredSkill,
            dto.RequiredCapacity,
            excludedAssignmentId: null);
        if (error is not null) return (null, error);

        var assignment = new Assignment
        {
            IncidentId = dto.IncidentId,
            HelpRequestId = dto.HelpRequestId,
            RescueTeamId = dto.RescueTeamId,
            VehicleId = dto.VehicleId,
            RequiredSkill = dto.RequiredSkill,
            RequiredCapacity = dto.RequiredCapacity,
            Status = AssignmentStatus.Proposed,
            PlanVersion = 1,
            Notes = dto.Notes
        };

        _db.Assignments.Add(assignment);
        await _db.SaveChangesAsync();

        var created = await AssignmentQuery().FirstAsync(a => a.Id == assignment.Id);
        return (ToDto(created), null);
    }

    public async Task<(AssignmentDto? Assignment, string? Error)> ReviseAsync(Guid id, ReviseAssignmentDto dto)
    {
        var assignment = await _db.Assignments
            .Include(a => a.Dispatch)
            .FirstOrDefaultAsync(a => a.Id == id);
        if (assignment is null) return (null, "Assignment not found.");

        if (assignment.Status is not AssignmentStatus.Proposed
            and not AssignmentStatus.PendingApproval
            and not AssignmentStatus.Rejected)
        {
            return (null, "Assignment cannot be revised unless it is proposed, pending approval, or rejected.");
        }

        if (assignment.Dispatch is not null
            && assignment.Dispatch.Status is not DispatchStatus.Cancelled
            and not DispatchStatus.Resolved)
        {
            return (null, "An assignment with an active dispatch cannot be revised.");
        }

        var error = await ValidateProposalAsync(
            assignment.IncidentId,
            assignment.HelpRequestId,
            dto.RescueTeamId,
            dto.VehicleId,
            dto.RequiredSkill,
            dto.RequiredCapacity,
            assignment.Id);
        if (error is not null) return (null, error);

        var safetyRelevantChange = assignment.RescueTeamId != dto.RescueTeamId
            || assignment.VehicleId != dto.VehicleId
            || assignment.RequiredSkill != dto.RequiredSkill
            || assignment.RequiredCapacity != dto.RequiredCapacity;

        assignment.RescueTeamId = dto.RescueTeamId;
        assignment.VehicleId = dto.VehicleId;
        assignment.RequiredSkill = dto.RequiredSkill;
        assignment.RequiredCapacity = dto.RequiredCapacity;
        assignment.Notes = dto.Notes;

        if (safetyRelevantChange)
        {
            assignment.PlanVersion++;
            assignment.Status = AssignmentStatus.Proposed;
        }

        await _db.SaveChangesAsync();
        var revised = await AssignmentQuery().FirstAsync(a => a.Id == assignment.Id);
        return (ToDto(revised), null);
    }

    private IQueryable<Assignment> AssignmentQuery() => _db.Assignments
        .Include(a => a.RescueTeam)
        .Include(a => a.Vehicle)
        .Include(a => a.Dispatch);

    private async Task<string?> ValidateProposalAsync(
        Guid? incidentId,
        Guid? helpRequestId,
        Guid rescueTeamId,
        Guid vehicleId,
        SkillType requiredSkill,
        int requiredCapacity,
        Guid? excludedAssignmentId)
    {
        if ((incidentId.HasValue && helpRequestId.HasValue) || (!incidentId.HasValue && !helpRequestId.HasValue))
            return "Assignment must reference exactly one incident or help request.";

        if (requiredCapacity <= 0)
            return "Required capacity must be greater than zero.";

        var team = await _db.RescueTeams
            .Include(t => t.Members)
            .FirstOrDefaultAsync(t => t.Id == rescueTeamId);
        if (team is null)
            return "Rescue team not found.";

        if (team.Status != TeamStatus.Available)
            return "Rescue team must be available.";

        if (!team.Members.Any(m => m.IsAvailable && m.Skill == requiredSkill))
            return $"Rescue team has no available member with required skill '{requiredSkill}'.";

        var vehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.Id == vehicleId);
        if (vehicle is null)
            return "Vehicle not found.";

        if (vehicle.RescueTeamId != rescueTeamId)
            return "Vehicle must belong to the selected rescue team.";

        if (vehicle.Status != VehicleStatus.Available)
            return "Vehicle must be available.";

        if (vehicle.Capacity < requiredCapacity)
            return "Vehicle does not meet the required capacity.";

        var candidateAssignments = _db.Assignments
            .Where(a => a.Status != AssignmentStatus.Rejected);
        if (excludedAssignmentId.HasValue)
            candidateAssignments = candidateAssignments.Where(a => a.Id != excludedAssignmentId.Value);

        var hasVehicleConflict = await candidateAssignments.AnyAsync(a =>
            a.VehicleId == vehicleId
            && (a.Dispatch == null
                || (a.Dispatch.Status != DispatchStatus.Resolved
                    && a.Dispatch.Status != DispatchStatus.Cancelled)));
        if (hasVehicleConflict)
            return "Vehicle is already committed to another active assignment or dispatch.";

        var hasTeamConflict = await candidateAssignments.AnyAsync(a =>
            a.RescueTeamId == rescueTeamId
            && (a.Dispatch == null
                || (a.Dispatch.Status != DispatchStatus.Resolved
                    && a.Dispatch.Status != DispatchStatus.Cancelled)));
        if (hasTeamConflict)
            return "Rescue team is already committed to another active assignment or dispatch.";

        return null;
    }

    private static AssignmentDto ToDto(Assignment assignment) => new(
        assignment.Id,
        assignment.IncidentId,
        assignment.HelpRequestId,
        assignment.RescueTeamId,
        assignment.RescueTeam?.Name ?? string.Empty,
        assignment.VehicleId,
        assignment.Vehicle?.PlateNumber ?? string.Empty,
        assignment.RequiredSkill,
        assignment.RequiredCapacity,
        assignment.Status,
        assignment.PlanVersion,
        assignment.AssignedAt,
        assignment.Notes,
        assignment.Dispatch?.Id);
}
