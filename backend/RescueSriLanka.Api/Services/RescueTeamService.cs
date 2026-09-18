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
        Task<bool> DeleteAsync(Guid id);

        Task<TeamMemberDto?> AddMemberAsync(Guid teamId, CreateTeamMemberDto dto);
        Task<bool> SetMemberAvailabilityAsync(Guid teamId, Guid memberId, bool isAvailable);

        Task<VehicleDto?> AddVehicleAsync(Guid teamId, CreateVehicleDto dto);
        Task<bool> SetVehicleStatusAsync(Guid teamId, Guid vehicleId, VehicleStatus status);
    }

    public class RescueTeamService : IRescueTeamService
    {
        private readonly ApplicationDbContext _db;

        public RescueTeamService(ApplicationDbContext db)
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

        public async Task<bool> DeleteAsync(Guid id)
        {
            var team = await _db.RescueTeams.FindAsync(id);
            if (team is null) return false;

            _db.RescueTeams.Remove(team);
            await _db.SaveChangesAsync();
            return true;
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

        public async Task<bool> SetMemberAvailabilityAsync(Guid teamId, Guid memberId, bool isAvailable)
        {
            var member = await _db.TeamMembers
                .FirstOrDefaultAsync(m => m.Id == memberId && m.RescueTeamId == teamId);

            if (member is null) return false;

            member.IsAvailable = isAvailable;
            await _db.SaveChangesAsync();
            return true;
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

        public async Task<bool> SetVehicleStatusAsync(Guid teamId, Guid vehicleId, VehicleStatus status)
        {
            var vehicle = await _db.Vehicles
                .FirstOrDefaultAsync(v => v.Id == vehicleId && v.RescueTeamId == teamId);

            if (vehicle is null) return false;

            vehicle.Status = status;
            await _db.SaveChangesAsync();
            return true;
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
