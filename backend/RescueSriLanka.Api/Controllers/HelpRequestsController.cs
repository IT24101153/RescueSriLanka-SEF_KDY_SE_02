using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
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
        // Citizen/tourist submits a new help request (Flutter app). Requires login.
        [HttpPost]
        [Authorize]
        public async Task<ActionResult<HelpRequestResponseDto>> Create([FromBody] CreateHelpRequestDto dto)
        {
            var citizenId = GetUserId();
            if (citizenId is null) return Unauthorized();

            var result = await _service.CreateAsync(citizenId.Value, dto);
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

        // GET /api/helprequests/mine
        // Citizen's own tracking screen (Flutter) — only their own requests
        [HttpGet("mine")]
        [Authorize]
        public async Task<ActionResult<List<HelpRequestResponseDto>>> GetMine()
        {
            var citizenId = GetUserId();
            if (citizenId is null) return Unauthorized();

            var result = await _service.GetByCitizenAsync(citizenId.Value);
            return Ok(result);
        }

        private Guid? GetUserId()
        {
            var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(idClaim, out var id) ? id : null;
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

        // PATCH /api/helprequests/{id}/verify
        // Admin marks a citizen report as real or fake before it's treated as legitimate.
        [HttpPatch("{id}/verify")]
        public async Task<ActionResult<HelpRequestResponseDto>> Verify(Guid id, [FromBody] VerifyHelpRequestDto dto)
        {
            // TODO once JWT auth is wired in the frontend: read the real admin user id from claims
            var verifiedByUserId = Guid.NewGuid(); // placeholder until auth claims are read here

            var result = await _service.VerifyAsync(id, verifiedByUserId, dto);
            if (result is null) return NotFound();
            return Ok(result);
        }
    }
}