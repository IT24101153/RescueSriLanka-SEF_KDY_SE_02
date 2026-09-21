using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RescueSriLanka.Api.DTOs;
using RescueSriLanka.Api.Services;

namespace RescueSriLanka.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AssignmentsController : ControllerBase
    {
        private readonly IAssignmentService _assignmentService;
        private readonly ITeamMatchingService _matchingService;

        public AssignmentsController(IAssignmentService assignmentService, ITeamMatchingService matchingService)
        {
            _assignmentService = assignmentService;
            _matchingService = matchingService;
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
        [Authorize(Roles = "EmergencyCoordinator")]
        [HttpPost("match")]
        public async Task<ActionResult<List<TeamMatchResultDto>>> Match(MatchRequestDto request)
            => Ok(await _matchingService.FindMatchesAsync(request));

        [Authorize(Roles = "EmergencyCoordinator")]
        [HttpPost]
        public async Task<ActionResult<AssignmentDto>> Create(CreateAssignmentDto dto)
        {
            var (created, error) = await _assignmentService.CreateAsync(dto);
            return created is null
                ? ValidationProblem(error)
                : CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }

        [Authorize(Roles = "EmergencyCoordinator")]
        [HttpPost("{id:guid}/revise")]
        public async Task<ActionResult<AssignmentDto>> Revise(Guid id, ReviseAssignmentDto dto)
        {
            var (revised, error) = await _assignmentService.ReviseAsync(id, dto);
            if (revised is not null) return Ok(revised);
            return error == "Assignment not found." ? NotFound(error) : ValidationProblem(error);
        }
    }
}
