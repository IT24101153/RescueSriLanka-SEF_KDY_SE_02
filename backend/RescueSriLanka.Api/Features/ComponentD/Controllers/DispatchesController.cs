using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RescueSriLanka.Api.DTOs;
using RescueSriLanka.Api.Services;
using RescueSriLanka.Api.Features.ComponentD.DTOs;
using RescueSriLanka.Api.Features.ComponentD.Services;

namespace RescueSriLanka.Api.Features.ComponentD.Controllers
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
            return Conflict(new { error = "Legacy dispatch creation is disabled. Use POST /api/assignments/{id}/decision with a validated workflow." });
        }

        [Authorize(Roles = "EmergencyCoordinator")]
        [HttpPost("{id:guid}/approve")]
        public async Task<ActionResult<DispatchDto>> Approve(Guid id, ApproveDispatchDto dto)
        {
            return Conflict(new { error = "Legacy dispatch approval is disabled. Use POST /api/assignments/{id}/decision." });
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
