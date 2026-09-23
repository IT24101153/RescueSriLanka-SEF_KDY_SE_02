using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RescueSriLanka.Api.DTOs.Incidents;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Services;

namespace RescueSriLanka.Api.Controllers;

/// <summary>Component A's canonical incident reads; no mutation or analysis endpoints.</summary>
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class IncidentsController(IIncidentReadService incidentService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<IncidentDto>>> List(
        [FromQuery] IncidentStatus? status = null,
        [FromQuery] IncidentSeverity? severity = null,
        [FromQuery] IncidentType? type = null,
        [FromQuery] string? district = null,
        [FromQuery] bool activeOnly = true,
        CancellationToken ct = default) =>
        Ok(await incidentService.QueryAsync(status, severity, type, district, activeOnly, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<IncidentDto>> Get(Guid id, CancellationToken ct = default)
    {
        var incident = await incidentService.GetAsync(id, ct);
        return incident is null ? NotFound() : Ok(incident);
    }
}
