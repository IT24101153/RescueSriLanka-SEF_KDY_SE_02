using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RescueSriLanka.Api.DTOs;
using RescueSriLanka.Api.Services;

namespace RescueSriLanka.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class DispatchesController : ControllerBase
    {
        private readonly IDispatchService _service;

        public DispatchesController(IDispatchService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<ActionResult<List<DispatchDto>>> GetAll()
            => Ok(await _service.GetAllAsync());

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<DispatchDto>> GetById(Guid id)
        {
            var d = await _service.GetByIdAsync(id);
            return d is null ? NotFound() : Ok(d);
        }

        // FIX: now surfaces a specific error message (assignment not
        // found / already resolved / already has an active dispatch)
        // instead of a generic validation-issues blob, so the frontend
        // can show the coordinator exactly why creation was blocked.
        [Authorize(Roles = "EmergencyCoordinator")]
        [HttpPost]
        public async Task<IActionResult> Create(CreateDispatchDto dto)
        {
            var (dispatch, validation, error) = await _service.CreateAsync(dto);
            if (dispatch is null)
                return BadRequest(new { error });

            return CreatedAtAction(nameof(GetById), new { id = dispatch.Id },
                new { dispatch, validation });
        }

        [Authorize(Roles = "EmergencyCoordinator")]
        [HttpPost("{id:guid}/approve")]
        public async Task<ActionResult<DispatchDto>> Approve(Guid id, ApproveDispatchDto dto)
        {
            var approvedByUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value
                ?? User.Identity?.Name;
            if (string.IsNullOrWhiteSpace(approvedByUserId))
                return Unauthorized(new { error = "Authenticated coordinator identity is missing." });

            var result = await _service.ApproveAsync(id, approvedByUserId, dto);
            return result is null ? NotFound() : Ok(result);
        }

        [Authorize(Roles = "EmergencyCoordinator,RescueTeam")]
        [HttpPatch("{id:guid}/status")]
        public async Task<ActionResult<DispatchDto>> TransitionStatus(Guid id, TransitionDispatchStatusDto dto)
        {
            var (success, error, dispatch) = await _service.TransitionStatusAsync(id, dto);
            if (!success) return BadRequest(new { error });
            return Ok(dispatch);
        }
    }
}
