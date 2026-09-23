using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RescueSriLanka.Api.Features.ComponentB.DTOs;
using RescueSriLanka.Api.Features.ComponentB.Services;

namespace RescueSriLanka.Api.Features.ComponentB.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TravelAdvisoriesController(ITravelAdvisoryService service) : ControllerBase
    {
        private readonly ITravelAdvisoryService _service = service;

        // POST /api/traveladvisories
        // Coordinator registers a new advisory (e.g. "flooding near Kalutara town centre")
        [HttpPost]
        public async Task<ActionResult<TravelAdvisoryResponseDto>> Create([FromBody] CreateTravelAdvisoryDto dto)
        {
            var result = await _service.CreateAsync(dto);
            return Ok(result);
        }

        // GET /api/traveladvisories
        // All currently active advisories — used for the map/advisory list screens
        [HttpGet]
        public async Task<ActionResult<List<TravelAdvisoryResponseDto>>> GetActive()
        {
            var result = await _service.GetActiveAsync();
            return Ok(result);
        }

        // POST /api/traveladvisories/check-safety
        // The core business operation: tourist/citizen checks if a location or route is safe
        // before travelling. Body: { "points": [ { "latitude": ..., "longitude": ... }, ... ] }
        [HttpPost("check-safety")]
        public async Task<ActionResult<SafetyCheckResponseDto>> CheckSafety([FromBody] SafetyCheckRequestDto dto)
        {
            var result = await _service.CheckSafetyAsync(dto);
            return Ok(result);
        }
    }
}