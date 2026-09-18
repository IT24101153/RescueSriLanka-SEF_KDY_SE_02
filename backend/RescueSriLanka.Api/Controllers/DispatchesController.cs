using Microsoft.AspNetCore.Mvc;
using RescueSriLanka.Api.DTOs;
using RescueSriLanka.Api.Services;

namespace RescueSriLanka.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
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

        // Creating a dispatch immediately runs the Safety Validation Agent.
        // The response includes both the created record and the agent's
        // findings, so the frontend can show them together.
        [HttpPost]
        public async Task<IActionResult> Create(CreateDispatchDto dto)
        {
            var (dispatch, validation) = await _service.CreateAsync(dto);
            if (dispatch is null)
                return BadRequest(new { validation.Issues });

            return CreatedAtAction(nameof(GetById), new { id = dispatch.Id },
                new { dispatch, validation });
        }

        // Emergency Coordinator approves/rejects — required before the
        // dispatch can move out of Pending (see DispatchService).
        [HttpPost("{id:guid}/approve")]
        public async Task<ActionResult<DispatchDto>> Approve(Guid id, ApproveDispatchDto dto)
        {
            var result = await _service.ApproveAsync(id, dto);
            return result is null ? NotFound() : Ok(result);
        }

        // Business-specific operation: dispatch status workflow.
        [HttpPatch("{id:guid}/status")]
        public async Task<ActionResult<DispatchDto>> TransitionStatus(Guid id, TransitionDispatchStatusDto dto)
        {
            var (success, error, dispatch) = await _service.TransitionStatusAsync(id, dto);
            if (!success) return BadRequest(new { error });
            return Ok(dispatch);
        }
    }
}
