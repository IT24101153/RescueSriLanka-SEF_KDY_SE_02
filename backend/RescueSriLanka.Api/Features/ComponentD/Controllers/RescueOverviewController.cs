using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Features.ComponentD.Data;
using RescueSriLanka.Api.Features.ComponentD.Models;

namespace RescueSriLanka.Api.Features.ComponentD.Controllers
{
    public record RescueOverviewTeamDto(
        string Name,
        string Status,
        int MemberCount,
        int AvailableMemberCount,
        List<string> Skills,
        List<RescueOverviewVehicleDto> Vehicles,
        double? BaseLatitude,
        double? BaseLongitude);

    public record RescueOverviewVehicleDto(string Type, int Count);

    public record RescueOverviewDto(
        int TeamsAvailable,
        int TeamsOnMission,
        int ActiveDispatches,
        List<RescueOverviewTeamDto> Teams);

    /// <summary>
    /// What the mobile Rescue tab shows any signed-in user: which teams exist,
    /// their status and capability. Deliberately carries no member names,
    /// phone numbers, plate numbers, assignments or dispatch details — those
    /// stay with the Rescue Team account.
    /// </summary>
    [ApiController]
    [Route("api/rescue/overview")]
    [Authorize]
    public class RescueOverviewController(ComponentDDbContext db) : ControllerBase
    {
        [HttpGet]
        public async Task<ActionResult<RescueOverviewDto>> Get(CancellationToken cancellationToken)
        {
            var teams = await db.RescueTeams
                .Include(t => t.Members)
                .Include(t => t.Vehicles)
                .OrderBy(t => t.Name)
                .ToListAsync(cancellationToken);

            var activeDispatches = await db.Dispatches.CountAsync(d =>
                d.Status == DispatchStatus.Dispatched ||
                d.Status == DispatchStatus.EnRoute ||
                d.Status == DispatchStatus.OnScene, cancellationToken);

            var dto = new RescueOverviewDto(
                teams.Count(t => t.Status == TeamStatus.Available),
                teams.Count(t => t.Status == TeamStatus.OnMission),
                activeDispatches,
                teams.Select(t => new RescueOverviewTeamDto(
                    t.Name,
                    t.Status.ToString(),
                    t.Members.Count,
                    t.Members.Count(m => m.IsAvailable),
                    t.AvailableSkills.Select(s => s.ToString()).ToList(),
                    t.Vehicles.GroupBy(v => v.Type)
                        .Select(g => new RescueOverviewVehicleDto(g.Key.ToString(), g.Count()))
                        .ToList(),
                    t.BaseLatitude,
                    t.BaseLongitude)).ToList());

            return Ok(dto);
        }
    }
}
