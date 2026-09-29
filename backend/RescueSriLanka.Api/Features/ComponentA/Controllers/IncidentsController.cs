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

    /// <summary>
    /// Filtered incident list for the admin table. Sorting and paging are
    /// opt-in: <paramref name="sortBy"/>/<paramref name="sortDir"/> reorder the
    /// results, and passing both <paramref name="page"/> and
    /// <paramref name="pageSize"/> slices them, with the total match count
    /// returned in the <c>X-Total-Count</c> header. Leaving all four out
    /// returns every match in the original order, unchanged — the disaster
    /// map and other components rely on that.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<IncidentDto>>> List(
        [FromQuery] IncidentStatus? status,
        [FromQuery] IncidentSeverity? severity,
        [FromQuery] IncidentType? type,
        [FromQuery] string? district,
        [FromQuery] bool activeOnly = true,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        CancellationToken ct = default)
    {
        if (page is <= 0)
        {
            return BadRequest(new { message = "page must be at least 1." });
        }

        if (pageSize is <= 0 or > 100)
        {
            return BadRequest(new { message = "pageSize must be between 1 and 100." });
        }

        var result = await incidentService.QueryAsync(
            status, severity, type, district, activeOnly, sortBy, sortDir, page, pageSize,
            approvedOnly: !IsStaff(), ct);

        Response.Headers.Append("X-Total-Count", result.TotalCount.ToString());
        return Ok(result.Items);
    }

    /// <summary>
    /// The signed-in user's own reports, from the database — so "My reports"
    /// shows the same list on the Mac, the APK, or a new phone.
    /// </summary>
    [HttpGet("mine")]
    public async Task<ActionResult<IReadOnlyList<IncidentDto>>> Mine(CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        return Ok(await incidentService.MineAsync(userId.Value, ct: ct));
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<IncidentDto>> Get(Guid id, CancellationToken ct)
    {
        // A report under review or rejected is not public: the same 404 as a
        // missing one, so nobody can tell it exists.
        var incident = await incidentService.GetForViewerAsync(id, CurrentUserId(), IsStaff(), ct);
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

        return Ok(await incidentService.NearbyAsync(lat, lng, radiusKm, approvedOnly: !IsStaff(), ct));
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

    /// <summary>
    /// Report an incident with a photo, in one multipart request. This is what
    /// the Flutter report form posts to when a photo is attached: filing the
    /// two separately let the analysis agent run before the photo arrived, so
    /// it graded the report blind.
    /// </summary>
    [HttpPost("with-photo")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [ProducesResponseType(typeof(CreateIncidentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CreateIncidentResponse>> CreateWithPhoto(
        [FromForm] CreateIncidentRequest request,
        IFormFile photo,
        [FromForm] string? caption,
        CancellationToken ct)
    {
        try
        {
            var result = await incidentService.CreateWithPhotoAsync(
                request, photo, caption, CurrentUserId(), ct);
            return CreatedAtAction(nameof(Get), new { id = result.Incident.Id }, result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
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

    /// <summary>Permanently deletes a report — for a duplicate, spam, or test
    /// report. Rejecting it via the status endpoint is the usual call; this is
    /// for when the report should not exist on record at all.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = Coordinator)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        await incidentService.DeleteAsync(id, ct) ? NoContent() : NotFound();

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

    /// <summary>
    /// Staff review every report; the public (signed out, or a citizen) sees
    /// only the ones a coordinator has approved as true. Reading one report by
    /// id stays open, so a reporter can still follow their own under review.
    /// </summary>
    private bool IsStaff() =>
        User.Identity?.IsAuthenticated == true &&
        !User.IsInRole(nameof(UserRole.Citizen));

    private Guid? CurrentUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
