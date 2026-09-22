using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.DTOs;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Services
{
    public interface IRescueTeamService
    {
        Task<List<RescueTeamDto>> GetAllAsync();
        Task<RescueTeamDto?> GetByIdAsync(Guid id);
        Task<RescueTeamDto> CreateAsync(CreateRescueTeamDto dto);
        Task<RescueTeamDto?> UpdateAsync(Guid id, UpdateRescueTeamDto dto);
        Task<(bool Success, string? Error)> DeleteAsync(Guid id);

        Task<TeamMemberDto?> AddMemberAsync(Guid teamId, CreateTeamMemberDto dto);
        Task<(bool Success, string? Error)> SetMemberAvailabilityAsync(Guid teamId, Guid memberId, bool isAvailable);
        Task<(TeamMemberDto? Member, string? Error)> UpdateMemberAsync(Guid teamId, Guid memberId, UpdateTeamMemberDto dto);
        Task<(bool Success, string? Error)> DeleteMemberAsync(Guid teamId, Guid memberId);

        Task<VehicleDto?> AddVehicleAsync(Guid teamId, CreateVehicleDto dto);
        Task<(bool Success, string? Error)> SetVehicleStatusAsync(Guid teamId, Guid vehicleId, VehicleStatus status);
        Task<(VehicleDto? Vehicle, string? Error)> UpdateVehicleAsync(Guid teamId, Guid vehicleId, UpdateVehicleDto dto);
        Task<(bool Success, string? Error)> DeleteVehicleAsync(Guid teamId, Guid vehicleId);
    }

    public class RescueTeamService : IRescueTeamService
    {
        private readonly ComponentDDbContext _db;

        public RescueTeamService(ComponentDDbContext db)
        {
            _db = db;
        }

        public async Task<List<RescueTeamDto>> GetAllAsync()
        {
            var teams = await _db.RescueTeams
                .Include(t => t.Members)
                .Include(t => t.Vehicles)
                .ToListAsync();

            return teams.Select(ToDto).ToList();
        }

        public async Task<RescueTeamDto?> GetByIdAsync(Guid id)
        {
            var team = await _db.RescueTeams
                .Include(t => t.Members)
                .Include(t => t.Vehicles)
                .FirstOrDefaultAsync(t => t.Id == id);

            return team is null ? null : ToDto(team);
        }

        public async Task<RescueTeamDto> CreateAsync(CreateRescueTeamDto dto)
        {
            var team = new RescueTeam
            {
                Name = dto.Name,
                BaseLatitude = dto.BaseLatitude,
                BaseLongitude = dto.BaseLongitude
            };

            _db.RescueTeams.Add(team);
            await _db.SaveChangesAsync();
            return ToDto(team);
        }

        public async Task<RescueTeamDto?> UpdateAsync(Guid id, UpdateRescueTeamDto dto)
        {
            var team = await _db.RescueTeams
                .Include(t => t.Members)
                .Include(t => t.Vehicles)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (team is null) return null;

            team.Name = dto.Name;
            team.Status = dto.Status;
            team.BaseLatitude = dto.BaseLatitude;
            team.BaseLongitude = dto.BaseLongitude;

            await _db.SaveChangesAsync();
            return ToDto(team);
        }

        // FIX: previously this let EF Core attempt the delete regardless
        // of existing Assignments/Dispatches referencing the team, which
        // throws an unhandled foreign-key violation (a raw 500) instead
        // of a clean, explainable error. Now it checks first and returns
        // a clear reason so the controller can respond with 409 Conflict.
        public async Task<(bool Success, string? Error)> DeleteAsync(Guid id)
        {
            var team = await _db.RescueTeams.FindAsync(id);
            if (team is null) return (false, "Team not found.");

            if (team.Status == TeamStatus.OnMission)
                return (false, "Cannot delete a team while it is on mission.");

            var hasAssignments = await _db.Assignments.AnyAsync(a => a.RescueTeamId == id);
            if (hasAssignments)
            {
                return (false, "Cannot delete a team with existing assignments. " +
                                "Reassign or remove its assignments first.");
            }

            _db.RescueTeams.Remove(team);
            await _db.SaveChangesAsync();
            return (true, null);
        }

        public async Task<TeamMemberDto?> AddMemberAsync(Guid teamId, CreateTeamMemberDto dto)
        {
            var team = await _db.RescueTeams.FindAsync(teamId);
            if (team is null) return null;

            var member = new TeamMember
            {
                RescueTeamId = teamId,
                FullName = dto.FullName,
                Phone = dto.Phone,
                Skill = dto.Skill
            };

            _db.TeamMembers.Add(member);
            await _db.SaveChangesAsync();

            return new TeamMemberDto(member.Id, member.FullName, member.Phone, member.Skill, member.IsAvailable);
        }

        public async Task<(bool Success, string? Error)> SetMemberAvailabilityAsync(Guid teamId, Guid memberId, bool isAvailable)
        {
            if (await IsTeamOperationallyReservedAsync(teamId)) return (false, "Team members cannot be changed while the team has an active assignment or mission.");
            var member = await _db.TeamMembers
                .FirstOrDefaultAsync(m => m.Id == memberId && m.RescueTeamId == teamId);

            if (member is null) return (false, "Team member not found.");

            member.IsAvailable = isAvailable;
            await _db.SaveChangesAsync();
            return (true, null);
        }

        public async Task<(TeamMemberDto? Member, string? Error)> UpdateMemberAsync(Guid teamId, Guid memberId, UpdateTeamMemberDto dto)
        {
            if (await IsTeamOperationallyReservedAsync(teamId))
                return (null, "Team members cannot be changed while the team has an active assignment or mission.");
            var member = await _db.TeamMembers.FirstOrDefaultAsync(m => m.Id == memberId && m.RescueTeamId == teamId);
            if (member is null) return (null, "Team member not found.");
            member.FullName = dto.FullName; member.Phone = dto.Phone; member.Skill = dto.Skill; member.IsAvailable = dto.IsAvailable;
            await _db.SaveChangesAsync();
            return (new TeamMemberDto(member.Id, member.FullName, member.Phone, member.Skill, member.IsAvailable), null);
        }

        public async Task<(bool Success, string? Error)> DeleteMemberAsync(Guid teamId, Guid memberId)
        {
            if (await IsTeamOperationallyReservedAsync(teamId))
                return (false, "Team members cannot be removed while the team has an active assignment or mission.");
            var member = await _db.TeamMembers.FirstOrDefaultAsync(m => m.Id == memberId && m.RescueTeamId == teamId);
            if (member is null) return (false, "Team member not found.");
            _db.TeamMembers.Remove(member); await _db.SaveChangesAsync(); return (true, null);
        }

        public async Task<VehicleDto?> AddVehicleAsync(Guid teamId, CreateVehicleDto dto)
        {
            var team = await _db.RescueTeams.FindAsync(teamId);
            if (team is null) return null;

            var vehicle = new Vehicle
            {
                RescueTeamId = teamId,
                PlateNumber = dto.PlateNumber,
                Type = dto.Type,
                Capacity = dto.Capacity
            };

            _db.Vehicles.Add(vehicle);
            await _db.SaveChangesAsync();

            return new VehicleDto(vehicle.Id, vehicle.PlateNumber, vehicle.Type, vehicle.Status, vehicle.Capacity);
        }

        public async Task<(bool Success, string? Error)> SetVehicleStatusAsync(Guid teamId, Guid vehicleId, VehicleStatus status)
        {
            if (await IsTeamOperationallyReservedAsync(teamId)) return (false, "Vehicles cannot be changed while the team has an active assignment or mission.");
            var vehicle = await _db.Vehicles
                .FirstOrDefaultAsync(v => v.Id == vehicleId && v.RescueTeamId == teamId);

            if (vehicle is null) return (false, "Vehicle not found.");

            vehicle.Status = status;
            await _db.SaveChangesAsync();
            return (true, null);
        }

        public async Task<(VehicleDto? Vehicle, string? Error)> UpdateVehicleAsync(Guid teamId, Guid vehicleId, UpdateVehicleDto dto)
        {
            if (await IsTeamOperationallyReservedAsync(teamId))
                return (null, "Vehicles cannot be changed while the team has an active assignment or mission.");
            var vehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.Id == vehicleId && v.RescueTeamId == teamId);
            if (vehicle is null) return (null, "Vehicle not found.");
            if (vehicle.Status == VehicleStatus.InUse) return (null, "An in-use vehicle cannot be changed.");
            vehicle.PlateNumber = dto.PlateNumber; vehicle.Type = dto.Type; vehicle.Status = dto.Status; vehicle.Capacity = dto.Capacity;
            await _db.SaveChangesAsync();
            return (new VehicleDto(vehicle.Id, vehicle.PlateNumber, vehicle.Type, vehicle.Status, vehicle.Capacity), null);
        }

        public async Task<(bool Success, string? Error)> DeleteVehicleAsync(Guid teamId, Guid vehicleId)
        {
            if (await IsTeamOperationallyReservedAsync(teamId))
                return (false, "Vehicles cannot be removed while the team has an active assignment or mission.");
            var vehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.Id == vehicleId && v.RescueTeamId == teamId);
            if (vehicle is null) return (false, "Vehicle not found.");
            if (vehicle.Status == VehicleStatus.InUse) return (false, "An in-use vehicle cannot be removed.");
            _db.Vehicles.Remove(vehicle); await _db.SaveChangesAsync(); return (true, null);
        }

        private async Task<bool> IsTeamOperationallyReservedAsync(Guid teamId)
        {
            var status = await _db.RescueTeams.Where(t => t.Id == teamId).Select(t => (TeamStatus?)t.Status).FirstOrDefaultAsync();
            return status == TeamStatus.OnMission || await _db.Assignments.AnyAsync(a => a.RescueTeamId == teamId && a.Status != AssignmentStatus.Rejected);
        }

        private static RescueTeamDto ToDto(RescueTeam t) => new(
            t.Id,
            t.Name,
            t.Status,
            t.BaseLatitude,
            t.BaseLongitude,
            t.Members.Select(m => new TeamMemberDto(m.Id, m.FullName, m.Phone, m.Skill, m.IsAvailable)).ToList(),
            t.Vehicles.Select(v => new VehicleDto(v.Id, v.PlateNumber, v.Type, v.Status, v.Capacity)).ToList());
    }
}
