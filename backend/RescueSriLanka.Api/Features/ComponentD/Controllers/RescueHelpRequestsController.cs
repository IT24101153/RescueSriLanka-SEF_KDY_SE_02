using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RescueSriLanka.Api.Features.ComponentD.DTOs;
using RescueSriLanka.Api.Features.ComponentD.Services;

namespace RescueSriLanka.Api.Features.ComponentD.Controllers;

[ApiController]
[Route("api/rescue/help-requests")]
[Authorize(Roles = "RescueTeam")]
public sealed class RescueHelpRequestsController(HelpRequestCandidateService candidates,
    HelpRequestRecommendationService recommendations) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<RescueHelpRequestDto>>> Get(CancellationToken ct) =>
        Ok(await candidates.GetQueueAsync(ct));

    [HttpPost("{id:guid}/recommend-team")]
    [EnableRateLimiting("ai")]
    public async Task<ActionResult<RescueRecommendationDto>> Recommend(Guid id,
        RecommendRescueTeamRequest request, CancellationToken ct)
    {
        try { return Ok(await recommendations.RecommendAsync(id, request.RequiredSkill!.Value, request.RequiredCapacity!.Value, ct)); }
        catch (ArgumentException ex) { return ValidationProblem(ex.Message); }
    }
}
