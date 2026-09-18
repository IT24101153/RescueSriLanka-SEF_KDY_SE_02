using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.DTOs;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Services
{
    public interface IAssignmentService
    {
        Task<List<AssignmentDto>> GetAllAsync();
        Task<AssignmentDto?> GetByIdAsync(Guid id);
        Task<AssignmentDto?> CreateAsync(CreateAssignmentDto dto);
    }

    public class AssignmentService : IAssignmentService
    {
        private readonly ApplicationDbContext _db;

        public AssignmentService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<List<AssignmentDto>> GetAllAsync()
        {
            var assignments = await _db.Assignments
                .Include(a => a.RescueTeam)
                .Include(a => a.Dispatch)
                .ToListAsync();

            return assignments.Select(ToDto).ToList();
        }

        public async Task<AssignmentDto?> GetByIdAsync(Guid id)
        {
            var a = await _db.Assignments
                .Include(x => x.RescueTeam)
                .Include(x => x.Dispatch)
                .FirstOrDefaultAsync(x => x.Id == id);

            return a is null ? null : ToDto(a);
        }

        public async Task<AssignmentDto?> CreateAsync(CreateAssignmentDto dto)
        {
            var team = await _db.RescueTeams.FindAsync(dto.RescueTeamId);
            if (team is null) return null;

            var assignment = new Assignment
            {
                IncidentId = dto.IncidentId,
                HelpRequestId = dto.HelpRequestId,
                RescueTeamId = dto.RescueTeamId,
                RequiredSkill = dto.RequiredSkill,
                Notes = dto.Notes
            };

            _db.Assignments.Add(assignment);
            await _db.SaveChangesAsync();

            assignment.RescueTeam = team;
            return ToDto(assignment);
        }

        private static AssignmentDto ToDto(Assignment a) => new(
            a.Id,
            a.IncidentId,
            a.HelpRequestId,
            a.RescueTeamId,
            a.RescueTeam?.Name ?? string.Empty,
            a.RequiredSkill,
            a.AssignedAt,
            a.Notes,
            a.Dispatch?.Id);
    }
}
