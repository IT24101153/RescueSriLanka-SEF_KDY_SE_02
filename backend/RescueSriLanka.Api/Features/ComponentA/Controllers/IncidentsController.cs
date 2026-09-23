using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Features.ComponentA.Agents.IncidentAnalysisAgent;
using RescueSriLanka.Api.Features.ComponentA.DTOs;
using RescueSriLanka.Api.Features.ComponentA.Models;
using RescueSriLanka.Api.Features.ComponentA.Services;

namespace RescueSriLanka.Api.Features.ComponentA.Controllers;
/// <summary>Component A — incidents and the live disaster map.</summary>
[ApiController]
[Route("api/[controller]")]
// Read endpoints are deliberately anonymous: a tourist must be able to see
// active hazards and safety zones without creating an account first. Writes and
// coordinator actions stay authenticated.
[Authorize]
public class IncidentsController(
    IIncidentService incidentService,
    IIncidentAnalysisAgent analysisAgent,
    IImageStorageService imageStorage) : ControllerBase
{
    private const string Coordinator = nameof(UserRole.EmergencyCoordinator);

    /// <summary>Filtered incident list for the admin table.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<IncidentDto>>> List(
        [FromQuery] IncidentStatus? status,
        [FromQuery] IncidentSeverity? severity,
        [FromQuery] IncidentType? type,
        [FromQuery] string? district,
        [FromQuery] bool activeOnly = true,
        CancellationToken ct = default) =>
        Ok(await incidentService.QueryAsync(status, severity, type, district, activeOnly, ct));

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<IncidentDto>> Get(Guid id, CancellationToken ct)
    {
        var incident = await incidentService.GetAsync(id, ct);
        return incident is null ? NotFound() : Ok(incident);
    }

    /// <summary>"What's near me" — the query the citizen map is built on.</summary>
    [HttpGet("nearby")]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<IncidentDto>>> Nearby(
        [FromQuery] double lat,
        [FromQuery] double lng,
        [FromQuery] double radiusKm = 25,
        CancellationToken ct = default)
    {
        if (lat is < -90 or > 90 || lng is < -180 or > 180)
        {
            return BadRequest(new { message = "Latitude or longitude is out of range." });
        }

        if (radiusKm is <= 0 or > 500)
        {
            return BadRequest(new { message = "radiusKm must be between 0 and 500." });
        }

        return Ok(await incidentService.NearbyAsync(lat, lng, radiusKm, ct));
    }

    /// <summary>Counts and breakdowns for the dashboard header.</summary>
    [HttpGet("statistics")]
    [AllowAnonymous]
    public async Task<ActionResult<DashboardStatisticsDto>> Statistics(CancellationToken ct) =>
        Ok(await incidentService.GetStatisticsAsync(ct));

    /// <summary>Report an incident. Any signed-in user, including citizens.</summary>
    [HttpPost]
    public async Task<ActionResult<IncidentDto>> Create(
        [FromBody] CreateIncidentRequest request, CancellationToken ct)
    {
        var incident = await incidentService.CreateAsync(request, CurrentUserId(), ct);
        return CreatedAtAction(nameof(Get), new { id = incident.Id }, incident);
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize(Roles = Coordinator)]
    public async Task<ActionResult<IncidentDto>> UpdateStatus(
        Guid id, [FromBody] UpdateIncidentStatusRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var incident = await incidentService.UpdateStatusAsync(id, request.Status, userId.Value, ct);
        return incident is null ? NotFound() : Ok(incident);
    }

    /// <summary>Coordinator overrides the severity the AI proposed.</summary>
    [HttpPatch("{id:guid}/severity")]
    [Authorize(Roles = Coordinator)]
    public async Task<ActionResult<IncidentDto>> OverrideSeverity(
        Guid id, [FromBody] UpdateIncidentSeverityRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var incident = await incidentService.OverrideSeverityAsync(id, request.Severity, userId.Value, ct);
        return incident is null ? NotFound() : Ok(incident);
    }

    /// <summary>
    /// Attaches a photo. Any signed-in user may add one to their report — this
    /// is what the Flutter camera feature posts to.
    /// </summary>
    [HttpPost("{id:guid}/images")]
    [ProducesResponseType(typeof(IncidentImageDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<IncidentImageDto>> UploadImage(
        Guid id, IFormFile file, [FromForm] string? caption, CancellationToken ct)
    {
        if (await incidentService.GetAsync(id, ct) is null)
        {
            return NotFound();
        }

        try
        {
            var image = await imageStorage.SaveAsync(id, file, caption, CurrentUserId(), ct);
            return CreatedAtAction(nameof(Get), new { id }, IncidentImageDto.FromImage(image));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            // The storage backend rejected it — report the failure rather than
            // recording an image row that points at nothing.
            return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message });
        }
    }

    /// <summary>
    /// Runs the Incident Analysis Agent. The agent only ever *proposes* — the
    /// severity in force changes solely through the severity endpoint, which a
    /// coordinator drives. This is the human approval gate.
    /// </summary>
    [HttpPost("{id:guid}/analyse")]
    [Authorize(Roles = Coordinator)]
    [ProducesResponseType(typeof(IncidentAnalysisResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IncidentAnalysisResult>> Analyse(Guid id, CancellationToken ct)
    {
        if (await incidentService.GetAsync(id, ct) is null)
        {
            return NotFound();
        }

        return Ok(await analysisAgent.AnalyseAsync(id, ct));
    }

    private Guid? CurrentUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
