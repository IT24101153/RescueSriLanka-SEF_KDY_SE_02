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

        // FIX: now distinguishes "not found" (404) from "blocked because
        // the team has active assignments" (409 Conflict) instead of
        // letting a foreign-key violation surface as an unhandled 500.
        [Authorize(Roles = "EmergencyCoordinator")]
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var (success, error) = await _service.DeleteAsync(id);
            if (success) return NoContent();
            return error == "Team not found."
                ? NotFound(error)
                : Conflict(new { error });
        }

        [Authorize(Roles = "EmergencyCoordinator")]
        [HttpPost("{id:guid}/members")]
        public async Task<ActionResult<TeamMemberDto>> AddMember(Guid id, CreateTeamMemberDto dto)
        {
            var member = await _service.AddMemberAsync(id, dto);
            return member is null ? NotFound("Team not found.") : Ok(member);
        }

        [Authorize(Roles = "EmergencyCoordinator")]
        [HttpPatch("{teamId:guid}/members/{memberId:guid}/availability")]
        public async Task<IActionResult> SetMemberAvailability(Guid teamId, Guid memberId, UpdateTeamMemberAvailabilityDto dto)
        {
            var (success, error) = await _service.SetMemberAvailabilityAsync(teamId, memberId, dto.IsAvailable);
            return success ? NoContent() : error == "Team member not found." ? NotFound(error) : Conflict(new { error });
        }

        [Authorize(Roles = "EmergencyCoordinator")]
        [HttpPut("{teamId:guid}/members/{memberId:guid}")]
        public async Task<ActionResult<TeamMemberDto>> UpdateMember(Guid teamId, Guid memberId, UpdateTeamMemberDto dto)
        {
            var (member, error) = await _service.UpdateMemberAsync(teamId, memberId, dto);
            if (member is not null) return Ok(member);
            return error == "Team member not found." ? NotFound(error) : Conflict(new { error });
        }

        [Authorize(Roles = "EmergencyCoordinator")]
        [HttpDelete("{teamId:guid}/members/{memberId:guid}")]
        public async Task<IActionResult> DeleteMember(Guid teamId, Guid memberId)
        {
            var (success, error) = await _service.DeleteMemberAsync(teamId, memberId);
            if (success) return NoContent();
            return error == "Team member not found." ? NotFound(error) : Conflict(new { error });
        }

        [Authorize(Roles = "EmergencyCoordinator")]
        [HttpPost("{id:guid}/vehicles")]
        public async Task<ActionResult<VehicleDto>> AddVehicle(Guid id, CreateVehicleDto dto)
        {
            var vehicle = await _service.AddVehicleAsync(id, dto);
            return vehicle is null ? NotFound("Team not found.") : Ok(vehicle);
        }

        [Authorize(Roles = "EmergencyCoordinator")]
        [HttpPatch("{teamId:guid}/vehicles/{vehicleId:guid}/status")]
        public async Task<IActionResult> SetVehicleStatus(Guid teamId, Guid vehicleId, UpdateVehicleStatusDto dto)
        {
            var (success, error) = await _service.SetVehicleStatusAsync(teamId, vehicleId, dto.Status);
            return success ? NoContent() : error == "Vehicle not found." ? NotFound(error) : Conflict(new { error });
        }

        [Authorize(Roles = "EmergencyCoordinator")]
        [HttpPut("{teamId:guid}/vehicles/{vehicleId:guid}")]
        public async Task<ActionResult<VehicleDto>> UpdateVehicle(Guid teamId, Guid vehicleId, UpdateVehicleDto dto)
        {
            var (vehicle, error) = await _service.UpdateVehicleAsync(teamId, vehicleId, dto);
            if (vehicle is not null) return Ok(vehicle);
            return error == "Vehicle not found." ? NotFound(error) : Conflict(new { error });
        }

        [Authorize(Roles = "EmergencyCoordinator")]
        [HttpDelete("{teamId:guid}/vehicles/{vehicleId:guid}")]
        public async Task<IActionResult> DeleteVehicle(Guid teamId, Guid vehicleId)
        {
            var (success, error) = await _service.DeleteVehicleAsync(teamId, vehicleId);
            if (success) return NoContent();
            return error == "Vehicle not found." ? NotFound(error) : Conflict(new { error });
        }
    }
}
