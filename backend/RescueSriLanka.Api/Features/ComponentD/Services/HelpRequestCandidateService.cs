using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Features.ComponentD.Data;
using RescueSriLanka.Api.Features.ComponentD.DTOs;
using RescueSriLanka.Api.Features.ComponentD.Models;
using RescueSriLanka.Api.Services;

namespace RescueSriLanka.Api.Features.ComponentD.Services;

public sealed class HelpRequestCandidateService(ComponentDDbContext db, IHelpRequestReadService requests)
{
    public async Task<List<RescueHelpRequestDto>> GetQueueAsync(CancellationToken ct = default)
    {
        var occupied = await db.Assignments.AsNoTracking().Active().Where(a => a.HelpRequestId != null)
            .Select(a => a.HelpRequestId!.Value).ToListAsync(ct);
        return (await requests.GetEligibleAsync(ct)).Where(r => !occupied.Contains(r.Id)).ToList();
    }

    public async Task<List<RescueCandidateDto>> FindAsync(Guid id, SkillType skill, int capacity,
        Guid? excludedAssignmentId = null, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(skill) || capacity < 1)
            throw new ArgumentException("Select a required skill and enter a positive people/patient transport demand.");
        var request = await requests.GetEligibleAsync(id, ct)
            ?? throw new ArgumentException("Help Request is unavailable or no longer Pending and Verified. Refresh the queue.");
        if (!ValidCoordinates(request.Latitude, request.Longitude))
            throw new ArgumentException("Help Request location is missing or invalid. A valid location is required for team selection.");

        var active = db.Assignments.AsNoTracking().Active().Where(a => a.Id != excludedAssignmentId);
        if (await active.AnyAsync(a => a.HelpRequestId == id, ct))
            throw new ArgumentException("Help Request already has current response work. Refresh the queue.");
        var occupiedTeams = await active.Select(a => a.RescueTeamId).ToListAsync(ct);
        var occupiedVehicles = await active.Where(a => a.VehicleId != null).Select(a => a.VehicleId!.Value).ToListAsync(ct);
        var teams = await db.RescueTeams.AsNoTracking().Include(t => t.Members).Include(t => t.Vehicles)
            .Where(t => t.Status == TeamStatus.Available && t.Members.Any(m => m.IsAvailable && m.Skill == skill))
            .ToListAsync(ct);
        var candidates = new List<RescueCandidateDto>();
        foreach (var team in teams)
        {
            if (occupiedTeams.Contains(team.Id) || !ValidCoordinates(team.BaseLatitude, team.BaseLongitude)) continue;
            var distance = GeoService.DistanceKm(team.BaseLatitude!.Value, team.BaseLongitude!.Value, request.Latitude!.Value, request.Longitude!.Value);
            foreach (var vehicle in team.Vehicles.Where(v => v.Status == VehicleStatus.Available && v.Capacity >= capacity && !occupiedVehicles.Contains(v.Id)))
                candidates.Add(new(team.Id, team.Name, vehicle.Id, vehicle.Type, distance, skill, vehicle.Capacity,
                    team.Status, vehicle.Status, team.BaseLatitude.Value, team.BaseLongitude.Value));
        }
        return candidates.OrderBy(c => c.DistanceKm).ThenBy(c => c.TeamId).ThenBy(c => c.VehicleCapacity).ThenBy(c => c.VehicleId).ToList();
    }

    public static bool ValidCoordinates(double? latitude, double? longitude) =>
        latitude.HasValue && longitude.HasValue && double.IsFinite(latitude.Value) && double.IsFinite(longitude.Value)
        && latitude.Value is >= -90 and <= 90 && longitude.Value is >= -180 and <= 180;
}
