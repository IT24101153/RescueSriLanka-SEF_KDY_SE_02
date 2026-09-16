using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RescueSriLanka.Api.DTOs.Agents;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Services;

namespace RescueSriLanka.Api.Controllers;

/// <summary>
/// Agent workflow monitoring and the approve / reject / revise gate.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AgentRunsController(IAgentRunService agentRunService) : ControllerBase
{
    private const string Coordinator = nameof(UserRole.EmergencyCoordinator);

    /// <summary>Execution summaries, newest first. Filter by incident when needed.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AgentRunDto>>> List(
        [FromQuery] Guid? incidentId,
        [FromQuery] int take = 50,
        CancellationToken ct = default) =>
        Ok(await agentRunService.QueryAsync(incidentId, take, ct));

    /// <summary>
    /// Approve the proposal, optionally revising the severity. This is the only
    /// path by which an agent's assessment reaches the live incident.
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = Coordinator)]
    public async Task<ActionResult<AgentRunDto>> Approve(
        Guid id, [FromBody] ApproveAgentRunRequest request, CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId) return Unauthorized();

        var run = await agentRunService.ApproveAsync(id, request.Severity, userId, ct);
        return run is null ? NotFound() : Ok(run);
    }

    /// <summary>Reject the proposal. The incident is left exactly as it was.</summary>
    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = Coordinator)]
    public async Task<ActionResult<AgentRunDto>> Reject(
        Guid id, [FromBody] RejectAgentRunRequest request, CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId) return Unauthorized();

        var run = await agentRunService.RejectAsync(id, request.Reason, userId, ct);
        return run is null ? NotFound() : Ok(run);
    }

    private Guid? CurrentUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
