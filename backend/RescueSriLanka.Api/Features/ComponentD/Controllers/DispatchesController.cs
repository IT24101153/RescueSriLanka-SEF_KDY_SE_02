using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RescueSriLanka.Api.DTOs;
using RescueSriLanka.Api.Services;
using RescueSriLanka.Api.Features.ComponentD.DTOs;
using RescueSriLanka.Api.Features.ComponentD.Services;
using RescueSriLanka.Api.Services.Email;

namespace RescueSriLanka.Api.Features.ComponentD.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "RescueTeam")]
    public class DispatchesController : ControllerBase
    {
        private readonly IDispatchService _service;
        private readonly IActionEmailService _emails;

        public DispatchesController(IDispatchService service, IActionEmailService emails)
        {
            _service = service;
            _emails = emails;
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
        [Authorize(Roles = "RescueTeam")]
        [HttpPost]
        public async Task<IActionResult> Create(CreateDispatchDto dto)
        {
            return Conflict(new { error = "Legacy dispatch creation is disabled. Use POST /api/assignments/{id}/decision with a validated workflow." });
        }

        [Authorize(Roles = "RescueTeam")]
        [HttpPost("{id:guid}/approve")]
        public async Task<ActionResult<DispatchDto>> Approve(Guid id, ApproveDispatchDto dto)
        {
            return Conflict(new { error = "Legacy dispatch approval is disabled. Use POST /api/assignments/{id}/decision." });
        }

        [Authorize(Roles = "RescueTeam")]
        [HttpPatch("{id:guid}/status")]
        public async Task<ActionResult<DispatchDto>> TransitionStatus(Guid id, TransitionDispatchStatusDto dto)
        {
            var (success, error, dispatch) = await _service.TransitionStatusAsync(id, dto);
            if (!success) return BadRequest(new { error });
            await _emails.DispatchStatusChangedAsync(id, dto.NewStatus.ToString());
            return Ok(dispatch);
        }
    }
}
