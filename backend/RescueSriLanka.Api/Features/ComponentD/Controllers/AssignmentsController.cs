using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RescueSriLanka.Api.DTOs;
using RescueSriLanka.Api.Features.ComponentD.Agents.SafetyValidation;
using System.Security.Claims;
using RescueSriLanka.Api.Services;
using RescueSriLanka.Api.Features.ComponentD.DTOs;
using RescueSriLanka.Api.Features.ComponentD.Services;
using RescueSriLanka.Api.Services.Email;

namespace RescueSriLanka.Api.Features.ComponentD.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "RescueTeam")]
    public class AssignmentsController : ControllerBase
    {
        private readonly IAssignmentService _assignmentService;
        private readonly ITeamMatchingService _matchingService;
        private readonly IAssignmentSafetyValidationAgent _safetyValidationAgent;
        private readonly IActionEmailService _emails;

        public AssignmentsController(
            IAssignmentService assignmentService,
            ITeamMatchingService matchingService,
            IAssignmentSafetyValidationAgent safetyValidationAgent,
            IActionEmailService emails)
        {
            _assignmentService = assignmentService;
            _matchingService = matchingService;
            _safetyValidationAgent = safetyValidationAgent;
            _emails = emails;
        }

        [HttpGet]
        public async Task<ActionResult<List<AssignmentDto>>> GetAll()
            => Ok(await _assignmentService.GetAllAsync());

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<AssignmentDto>> GetById(Guid id)
        {
            var a = await _assignmentService.GetByIdAsync(id);
            return a is null ? NotFound() : Ok(a);
        }

        // Business-specific operation: skill/availability-based team matching.
        // Called by the Coordinator/Planner Agent (or directly from React)
        // to get ranked candidate teams before creating an Assignment.
        [Authorize(Roles = "RescueTeam")]
        [HttpPost("match")]
        public async Task<ActionResult<List<TeamMatchResultDto>>> Match(MatchRequestDto request)
            => Ok(await _matchingService.FindMatchesAsync(request));

        [Authorize(Roles = "RescueTeam")]
        [HttpPost]
        public async Task<ActionResult<AssignmentDto>> Create(CreateAssignmentDto dto)
        {
            var (created, error) = await _assignmentService.CreateAsync(dto);
            if (created is null) return ValidationProblem(error);
            await _emails.AssignmentCreatedAsync(created.Id);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }

        [Authorize(Roles = "RescueTeam")]
        [HttpPost("{id:guid}/revise")]
        public async Task<ActionResult<AssignmentDto>> Revise(Guid id, ReviseAssignmentDto dto)
        {
            var (revised, error) = await _assignmentService.ReviseAsync(id, dto);
            if (revised is not null) return Ok(revised);
            return error == "Assignment not found." ? NotFound(error) : ValidationProblem(error);
        }

        // Produces a safety recommendation only. It never approves or
        // dispatches the assignment; a RescueTeam account remains required.
        [Authorize(Roles = "RescueTeam")]
        [HttpPost("{id:guid}/validate")]
        public async Task<ActionResult<SafetyValidationWorkflowResultDto>> Validate(Guid id, CancellationToken cancellationToken)
        {
            var assignment = await _assignmentService.GetByIdAsync(id);
            if (assignment?.Status == Models.AssignmentStatus.Cancelled)
                return Conflict(new { error = "Cancelled assignments cannot start a safety review." });
            return Ok(await _safetyValidationAgent.ValidateAsync(id, cancellationToken));
        }

        [HttpPost("{id:guid}/cancel")]
        public async Task<ActionResult<AssignmentDto>> Cancel(Guid id)
        {
            var (assignment, error) = await _assignmentService.CancelAsync(id);
            if (assignment is not null) return Ok(assignment);
            return error == "Assignment not found." ? NotFound(new { error }) : Conflict(new { error });
        }

        [Authorize(Roles = "RescueTeam")]
        [HttpPost("{id:guid}/decision")]
        public async Task<IActionResult> Decide(Guid id, CoordinatorDecisionDto dto, [FromServices] IDispatchService dispatchService)
        {
            var coordinatorId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value ?? User.Identity?.Name;
            if (string.IsNullOrWhiteSpace(coordinatorId))
                return Unauthorized(new { error = "Authenticated coordinator identity is missing." });

            var result = await dispatchService.DecideAsync(id, coordinatorId, dto);
            if (!result.Success) return Conflict(result);
            await _emails.AssignmentDecidedAsync(id, dto.Decision.ToString());
            return Ok(result);
        }
    }
}
