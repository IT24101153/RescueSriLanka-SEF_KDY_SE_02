using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RescueSriLanka.Api.Features.ComponentB.Agents.PlannerAgent;
using RescueSriLanka.Api.Features.ComponentB.DTOs;
using RescueSriLanka.Api.Features.ComponentB.Models;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Features.ComponentB.Controllers
{
    [ApiController]
    [Route("api/agentworkflows")]
    public class AgentWorkflowsController(IPlannerAgentService plannerAgent) : ControllerBase
    {
        private const string Coordinators =
            nameof(UserRole.EmergencyCoordinator) + "," + nameof(UserRole.HelpRequestManager);
        private readonly IPlannerAgentService _plannerAgent = plannerAgent;

        // POST /api/agentworkflows/trigger
        // Starts a new workflow for a given HelpRequest (or Incident, once that exists).
        // Source: Proposal Section 9 example workflow.
        [HttpPost("trigger")]
        [Authorize(Roles = Coordinators)]
        public async Task<ActionResult<AgentWorkflowResponseDto>> Trigger([FromBody] TriggerWorkflowDto dto)
        {
            // Component B currently plans citizen help requests. Incident workflows
            // belong to the incident/dispatch flow and must not create an empty B plan.
            if (dto.ObjectiveType != PlannerWorkflowObjectiveType.HelpRequest)
            {
                return BadRequest(new { message = "Component B workflows support HelpRequest objectives only." });
            }

            if (dto.ObjectiveId == Guid.Empty)
                return BadRequest(new { message = "A help request ID is required." });

            try
            {
                var result = await _plannerAgent.TriggerAsync(dto);
                return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // GET /api/agentworkflows/{id}
        [HttpGet("{id}")]
        [Authorize(Roles = Coordinators)]
        public async Task<ActionResult<AgentWorkflowResponseDto>> GetById(Guid id)
        {
            var result = await _plannerAgent.GetByIdAsync(id);
            if (result is null) return NotFound();
            return Ok(result);
        }

        // POST /api/agentworkflows/{id}/decision
        // Coordinator approves or rejects the plan before any high-impact action.
        // Source: Proposal Section 6 — human-approval pause requirement.
        [HttpPost("{id}/decision")]
        [Authorize(Roles = Coordinators)]
        public async Task<ActionResult<AgentWorkflowResponseDto>> Decide(Guid id, [FromBody] ApprovalDecisionDto dto)
        {
            var coordinatorId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(coordinatorId, out var coordinatorUserId)) return Unauthorized();

            try
            {
                var result = await _plannerAgent.DecideApprovalAsync(id, coordinatorUserId, dto);
                if (result is null) return NotFound();
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }
    }
}
