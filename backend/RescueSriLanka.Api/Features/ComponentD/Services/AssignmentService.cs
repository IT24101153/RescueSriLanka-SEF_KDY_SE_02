using Microsoft.EntityFrameworkCore.Storage;
using HelpRequestStatus = RescueSriLanka.Api.Features.ComponentB.Models.HelpRequestStatus;
using RescueSriLanka.Api.Features.ComponentB.Services;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.DTOs;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Features.ComponentD.DTOs;
using RescueSriLanka.Api.Features.ComponentD.Data;
using RescueSriLanka.Api.Features.ComponentD.Models;

namespace RescueSriLanka.Api.Features.ComponentD.Services;
public interface IAssignmentService
{
    Task<List<AssignmentDto>> GetAllAsync();
    Task<AssignmentDto?> GetByIdAsync(Guid id);
    Task<(AssignmentDto? Assignment, string? Error)> CreateAsync(CreateAssignmentDto dto);
    Task<(AssignmentDto? Assignment, string? Error)> CancelAsync(Guid id);
    Task<(AssignmentDto? Assignment, string? Error)> ReviseAsync(Guid id, ReviseAssignmentDto dto);
}

public class AssignmentService : IAssignmentService
{
    private readonly ComponentDDbContext _db;
    private readonly IHelpRequestResponseStatusService _responseStatus;
    private readonly IIncidentReadService _incidents;
    private readonly HelpRequestCandidateService _helpRequests;

    public AssignmentService(ComponentDDbContext db, IIncidentReadService incidents, HelpRequestCandidateService helpRequests, IHelpRequestResponseStatusService responseStatus)
    {
        _db = db;
        _responseStatus = responseStatus;
        _incidents = incidents;
        _helpRequests = helpRequests;
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
        await using var transaction = dto.HelpRequestId.HasValue && _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable) : null;
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
        try
        {
            await _db.SaveChangesAsync();
            if (assignment.HelpRequestId is Guid requestId)
                await _responseStatus.SynchronizeAsync(requestId, HelpRequestStatus.Assigned, transaction?.GetDbTransaction());
            if (transaction is not null) await transaction.CommitAsync();
        }
        catch (DbUpdateException) when (dto.HelpRequestId.HasValue)
        {
            if (transaction is not null) await transaction.RollbackAsync();
            return (null, "Concurrent response work changed. Refresh the queue before creating a response plan.");
        }
        catch (Npgsql.PostgresException ex) when (dto.HelpRequestId.HasValue && ex.SqlState == "40001")
        {
            if (transaction is not null) await transaction.RollbackAsync();
            return (null, "Concurrent response work changed. Refresh the queue before creating a response plan.");
        }

        var created = await AssignmentQuery().FirstAsync(a => a.Id == assignment.Id);
        return (ToDto(created), null);
    }

    public async Task<(AssignmentDto? Assignment, string? Error)> ReviseAsync(Guid id, ReviseAssignmentDto dto)
    {
        await using var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable) : null;
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

        if (assignment.Dispatch is not null)
        {
            return (null, "An assignment with a dispatch cannot be revised.");
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

        assignment.RescueTeamId = dto.RescueTeamId;
        assignment.VehicleId = dto.VehicleId;
        assignment.RequiredSkill = dto.RequiredSkill;
        assignment.RequiredCapacity = dto.RequiredCapacity;
        assignment.Notes = dto.Notes;

        // Every saved revision requires a fresh safety review, including notes-only updates.
        assignment.PlanVersion++;
        assignment.Status = AssignmentStatus.Proposed;

        await _db.SaveChangesAsync();
        if (transaction is not null) await transaction.CommitAsync();
        var revised = await AssignmentQuery().FirstAsync(a => a.Id == assignment.Id);
        return (ToDto(revised), null);
    }

    public async Task<(AssignmentDto? Assignment, string? Error)> CancelAsync(Guid id)
    {
        // Serialize against approval/dispatch and revision, which also write this plan.
        await using var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable) : null;
        try
        {
            var assignment = await AssignmentQuery().SingleOrDefaultAsync(a => a.Id == id);
            if (assignment is null) return (null, "Assignment not found.");
            if (assignment.Dispatch is not null)
                return (null, "This assignment already has a dispatch. Cancel the dispatch instead.");
            if (assignment.Status is not (AssignmentStatus.Proposed or AssignmentStatus.PendingApproval
                or AssignmentStatus.Rejected or AssignmentStatus.Approved))
                return (null, "Assignment is already cancelled or cannot be cancelled.");
            if (assignment.HelpRequestId is Guid linkedRequestId && await _db.Assignments.Active()
                .AnyAsync(a => a.Id != assignment.Id && a.HelpRequestId == linkedRequestId))
                return (null, "Help Request has other active response work. Resolve that work before cancelling this plan.");
            assignment.Status = AssignmentStatus.Cancelled;
            assignment.PlanVersion++; // Invalidate any previously captured review without deleting it.
            await _db.SaveChangesAsync(); // Component D audit hook updates UpdatedAt.
            if (assignment.HelpRequestId is Guid requestId)
                await _responseStatus.ReturnToPendingForRecoordinationAsync(requestId, assignment.Id, transaction?.GetDbTransaction());
            if (transaction is not null) await transaction.CommitAsync();
            return (ToDto(assignment), null);
        }
        catch (DbUpdateException)
        {
            if (transaction is not null) await transaction.RollbackAsync();
            return (null, "Assignment changed concurrently. Refresh before cancelling.");
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "40001")
        {
            if (transaction is not null) await transaction.RollbackAsync();
            return (null, "Assignment changed concurrently. Refresh before cancelling.");
        }
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

        if (incidentId.HasValue)
        {
            var incident = await _incidents.GetAsync(incidentId.Value);
            if (incident is null)
                return "Selected incident no longer exists. Refresh incidents and select an existing incident.";
            if (!incident.IsActive || incident.Status is "Resolved" or "Rejected")
                return "Selected incident is no longer active. Refresh incidents before creating or revising an assignment.";
        }

        if (helpRequestId.HasValue)
        {
            try
            {
                var eligible = await _helpRequests.FindAsync(helpRequestId.Value, requiredSkill, requiredCapacity, excludedAssignmentId);
                if (!eligible.Any(c => c.TeamId == rescueTeamId && c.VehicleId == vehicleId))
                    return "Selected team and vehicle are no longer eligible. Request a fresh recommendation.";
            }
            catch (ArgumentException ex) { return ex.Message; }
        }

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

        var candidateAssignments = _db.Assignments.Active();
        if (excludedAssignmentId.HasValue)
            candidateAssignments = candidateAssignments.Where(a => a.Id != excludedAssignmentId.Value);
        if (await candidateAssignments.AnyAsync(a => a.VehicleId == vehicleId))
            return "Vehicle is already committed to another active assignment or dispatch.";
        if (await candidateAssignments.AnyAsync(a => a.RescueTeamId == rescueTeamId))
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
