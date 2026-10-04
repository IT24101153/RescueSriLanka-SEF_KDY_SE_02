using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Features.ComponentA.Agents.ZonePlanningAgent;
using RescueSriLanka.Api.Features.ComponentA.DTOs;
using RescueSriLanka.Api.Features.ComponentA.Services;

namespace RescueSriLanka.Api.Features.ComponentA.Controllers;
/// <summary>Component A — the safe / caution / danger layer.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SafetyZonesController(
    ISafetyZoneService zoneService,
    IManualZoneService manualZones,
    IZonePlanningAgent planningAgent) : ControllerBase
{
    private const string Coordinator = nameof(UserRole.EmergencyCoordinator);

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<SafetyZoneDto>>> List(CancellationToken ct) =>
        Ok(await zoneService.GetActiveAsync(ct));

    /// <summary>
    /// Is this point safe? Consumed by the tourist app and by Student B's
    /// travel advisory — treat the response shape as a published contract.
    /// </summary>
    [HttpGet("check")]
    [AllowAnonymous]
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
    [Authorize(Roles = Coordinator)]
    public async Task<ActionResult<object>> Recompute(CancellationToken ct) =>
        Ok(new { activeZones = await zoneService.RecomputeAsync(ct) });

    /// <summary>A coordinator declares a zone by hand — an evacuation area, a closed road, a shelter.</summary>
    [HttpPost]
    [Authorize(Roles = Coordinator)]
    [ProducesResponseType(typeof(SafetyZoneDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SafetyZoneDto>> Create(
        [FromBody] SafetyZoneRequest request, CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId) return Unauthorized();

        try
        {
            var zone = await manualZones.CreateManualAsync(request, userId, ct: ct);
            return CreatedAtAction(nameof(List), null, zone);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Edits a manual zone. Derived zones follow their incident and are refused (409).</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = Coordinator)]
    [ProducesResponseType(typeof(SafetyZoneDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SafetyZoneDto>> Update(
        Guid id, [FromBody] SafetyZoneRequest request, CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId) return Unauthorized();

        try
        {
            var zone = await manualZones.UpdateManualAsync(id, request, userId, ct);
            return zone is null ? NotFound() : Ok(zone);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>Retires a manual zone: off the map, kept on record.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = Coordinator)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Retire(Guid id, CancellationToken ct)
    {
        try
        {
            return await manualZones.RetireManualAsync(id, ct) ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Runs the Zone Planning Agent over every approved incident. The plan is a
    /// proposal on an agent run; nothing reaches the map until a coordinator
    /// approves it there, editing any zone first if they wish.
    /// </summary>
    [HttpPost("plan")]
    [Authorize(Roles = Coordinator)]
    [EnableRateLimiting("ai")]
    [ProducesResponseType(typeof(ZonePlanResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<ZonePlanResult>> Plan(CancellationToken ct)
    {
        try
        {
            return Ok(await planningAgent.PlanAsync(ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                message = "Zone planning failed and was recorded on the agent run. Nothing was changed; try again."
            });
        }
    }

    private Guid? CurrentUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
