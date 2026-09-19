using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RescueSriLanka.Api.DTOs;
using RescueSriLanka.Api.Services;

namespace RescueSriLanka.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class RescueTeamsController : ControllerBase
    {
        private readonly IRescueTeamService _service;

        public RescueTeamsController(IRescueTeamService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<ActionResult<List<RescueTeamDto>>> GetAll()
            => Ok(await _service.GetAllAsync());

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<RescueTeamDto>> GetById(Guid id)
        {
            var team = await _service.GetByIdAsync(id);
            return team is null ? NotFound() : Ok(team);
        }

        [Authorize(Roles = "EmergencyCoordinator")]
        [HttpPost]
        public async Task<ActionResult<RescueTeamDto>> Create(CreateRescueTeamDto dto)
        {
            var created = await _service.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }

        [Authorize(Roles = "EmergencyCoordinator")]
        [HttpPut("{id:guid}")]
        public async Task<ActionResult<RescueTeamDto>> Update(Guid id, UpdateRescueTeamDto dto)
        {
            var updated = await _service.UpdateAsync(id, dto);
            return updated is null ? NotFound() : Ok(updated);
        }

        [Authorize(Roles = "EmergencyCoordinator")]
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var deleted = await _service.DeleteAsync(id);
            return deleted ? NoContent() : NotFound();
        }

        [Authorize(Roles = "EmergencyCoordinator")]
        [HttpPost("{id:guid}/members")]
        public async Task<ActionResult<TeamMemberDto>> AddMember(Guid id, CreateTeamMemberDto dto)
        {
            var member = await _service.AddMemberAsync(id, dto);
            return member is null ? NotFound("Team not found.") : Ok(member);
        }

        [Authorize(Roles = "EmergencyCoordinator,RescueTeam")]
        [HttpPatch("{teamId:guid}/members/{memberId:guid}/availability")]
        public async Task<IActionResult> SetMemberAvailability(Guid teamId, Guid memberId, UpdateTeamMemberAvailabilityDto dto)
        {
            var success = await _service.SetMemberAvailabilityAsync(teamId, memberId, dto.IsAvailable);
            return success ? NoContent() : NotFound();
        }

        [Authorize(Roles = "EmergencyCoordinator")]
        [HttpPost("{id:guid}/vehicles")]
        public async Task<ActionResult<VehicleDto>> AddVehicle(Guid id, CreateVehicleDto dto)
        {
            var vehicle = await _service.AddVehicleAsync(id, dto);
            return vehicle is null ? NotFound("Team not found.") : Ok(vehicle);
        }

        [Authorize(Roles = "EmergencyCoordinator,RescueTeam")]
        [HttpPatch("{teamId:guid}/vehicles/{vehicleId:guid}/status")]
        public async Task<IActionResult> SetVehicleStatus(Guid teamId, Guid vehicleId, UpdateVehicleStatusDto dto)
        {
            var success = await _service.SetVehicleStatusAsync(teamId, vehicleId, dto.Status);
            return success ? NoContent() : NotFound();
        }
    }
}
