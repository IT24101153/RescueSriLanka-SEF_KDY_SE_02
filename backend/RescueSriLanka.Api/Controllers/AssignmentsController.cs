using Microsoft.AspNetCore.Mvc;
using RescueSriLanka.Api.DTOs;
using RescueSriLanka.Api.Services;

namespace RescueSriLanka.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
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
        [HttpPost("match")]
        public async Task<ActionResult<List<TeamMatchResultDto>>> Match(MatchRequestDto request)
            => Ok(await _matchingService.FindMatchesAsync(request));

        [HttpPost]
        public async Task<ActionResult<AssignmentDto>> Create(CreateAssignmentDto dto)
        {
            var created = await _assignmentService.CreateAsync(dto);
            return created is null
                ? NotFound("Rescue team not found.")
                : CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
    }
}
