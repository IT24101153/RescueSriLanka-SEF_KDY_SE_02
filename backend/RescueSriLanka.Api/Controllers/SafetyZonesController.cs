using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RescueSriLanka.Api.DTOs.Incidents;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Services;

namespace RescueSriLanka.Api.Controllers;

/// <summary>Component A — the safe / caution / danger layer.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SafetyZonesController(ISafetyZoneService zoneService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SafetyZoneDto>>> List(CancellationToken ct) =>
        Ok(await zoneService.GetActiveAsync(ct));

    /// <summary>
    /// Is this point safe? Consumed by the tourist app and by Student B's
    /// travel advisory — treat the response shape as a published contract.
    /// </summary>
    [HttpGet("check")]
    public async Task<ActionResult<ZoneCheckResultDto>> Check(
        [FromQuery] double lat, [FromQuery] double lng, CancellationToken ct)
    {
        if (lat is < -90 or > 90 || lng is < -180 or > 180)
        {
            return BadRequest(new { message = "Latitude or longitude is out of range." });
        }

        return Ok(await zoneService.CheckPointAsync(lat, lng, ct));
    }

    /// <summary>Force a rebuild of the derived zones.</summary>
    [HttpPost("recompute")]
    [Authorize(Roles = nameof(UserRole.EmergencyCoordinator))]
    public async Task<ActionResult<object>> Recompute(CancellationToken ct) =>
        Ok(new { activeZones = await zoneService.RecomputeAsync(ct) });
}
