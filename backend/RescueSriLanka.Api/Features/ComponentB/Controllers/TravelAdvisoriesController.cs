using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RescueSriLanka.Api.Features.ComponentB.DTOs;
using RescueSriLanka.Api.Features.ComponentB.Services;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Features.ComponentB.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TravelAdvisoriesController(ITravelAdvisoryService service) : ControllerBase
    {
        private const string Coordinators =
            nameof(UserRole.EmergencyCoordinator) + "," + nameof(UserRole.HelpRequestManager);
        private readonly ITravelAdvisoryService _service = service;

        // POST /api/traveladvisories
        // Coordinator registers a new advisory (e.g. "flooding near Kalutara town centre")
        [HttpPost]
        [Authorize(Roles = Coordinators)]
        public async Task<ActionResult<TravelAdvisoryResponseDto>> Create([FromBody] CreateTravelAdvisoryDto dto)
        {
            var result = await _service.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }

        // GET /api/traveladvisories
        // All currently active advisories — used for the map/advisory list screens
        [HttpGet]
        [Authorize]
        public async Task<ActionResult<List<TravelAdvisoryResponseDto>>> GetActive()
        {
            var result = await _service.GetActiveAsync();
            return Ok(result);
        }

        // GET /api/traveladvisories/{id}
        // A coordinator can still retrieve an expired advisory to correct or remove it.
        [HttpGet("{id:guid}")]
        [Authorize(Roles = Coordinators)]
        public async Task<ActionResult<TravelAdvisoryResponseDto>> GetById(Guid id)
        {
            var result = await _service.GetByIdAsync(id);
            return result is null ? NotFound() : Ok(result);
        }

        // PUT /api/traveladvisories/{id}
        [HttpPut("{id:guid}")]
        [Authorize(Roles = Coordinators)]
        public async Task<ActionResult<TravelAdvisoryResponseDto>> Update(Guid id, [FromBody] UpdateTravelAdvisoryDto dto)
        {
            var result = await _service.UpdateAsync(id, dto);
            return result is null ? NotFound() : Ok(result);
        }

        // DELETE /api/traveladvisories/{id}
        [HttpDelete("{id:guid}")]
        [Authorize(Roles = Coordinators)]
        public async Task<IActionResult> Delete(Guid id)
        {
            return await _service.DeleteAsync(id) ? NoContent() : NotFound();
        }

        // POST /api/traveladvisories/check-safety
        // The core business operation: tourist/citizen checks if a location or route is safe
        // before travelling. Body: { "points": [ { "latitude": ..., "longitude": ... }, ... ] }
        [HttpPost("check-safety")]
        [Authorize]
        public async Task<ActionResult<SafetyCheckResponseDto>> CheckSafety([FromBody] SafetyCheckRequestDto dto)
        {
            var result = await _service.CheckSafetyAsync(dto);
            return Ok(result);
        }
    }
}
