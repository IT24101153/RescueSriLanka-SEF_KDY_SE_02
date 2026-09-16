using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RescueSriLanka.Api.DTOs;
using RescueSriLanka.Api.Services;

namespace RescueSriLanka.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HelpRequestsController : ControllerBase
    {
        private readonly IHelpRequestService _service;

        public HelpRequestsController(IHelpRequestService service)
        {
            _service = service;
        }

        // POST /api/helprequests
        // Citizen/tourist submits a new help request (Flutter app)
        [HttpPost]
        public async Task<ActionResult<HelpRequestResponseDto>> Create([FromBody] CreateHelpRequestDto dto)
        {
            // TODO once JWT auth is wired up: read the real citizen id from the authenticated user's claims
            // e.g. var citizenId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var citizenId = Guid.NewGuid(); // placeholder until auth is in place

            var result = await _service.CreateAsync(citizenId, dto);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }

        // GET /api/helprequests
        // Coordinator's review screen (React) — all requests, most urgent first
        [HttpGet]
        public async Task<ActionResult<List<HelpRequestResponseDto>>> GetAll()
        {
            var result = await _service.GetAllAsync();
            return Ok(result);
        }

        // GET /api/helprequests/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<HelpRequestResponseDto>> GetById(Guid id)
        {
            var result = await _service.GetByIdAsync(id);
            if (result is null) return NotFound();
            return Ok(result);
        }

        // PATCH /api/helprequests/{id}/status
        // Coordinator changes status (or system/agent does, post-approval)
        [HttpPatch("{id}/status")]
        public async Task<ActionResult<HelpRequestResponseDto>> UpdateStatus(Guid id, [FromBody] UpdateHelpRequestStatusDto dto)
        {
            // TODO once JWT auth is wired up: read the real user id making the change
            var changedByUserId = Guid.NewGuid(); // placeholder until auth is in place

            var result = await _service.UpdateStatusAsync(id, changedByUserId, dto);
            if (result is null) return NotFound();
            return Ok(result);
        }

        // GET /api/helprequests/{id}/history
        // Citizen tracking screen (Flutter) — full status timeline
        [HttpGet("{id}/history")]
        public async Task<ActionResult<List<StatusHistoryDto>>> GetHistory(Guid id)
        {
            var result = await _service.GetHistoryAsync(id);
            return Ok(result);
        }
    }
}