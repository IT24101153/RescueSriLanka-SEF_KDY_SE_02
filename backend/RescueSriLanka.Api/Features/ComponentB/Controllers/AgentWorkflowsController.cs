using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RescueSriLanka.Api.Features.ComponentB.Agents.PlannerAgent;
using RescueSriLanka.Api.Features.ComponentB.DTOs;

namespace RescueSriLanka.Api.Features.ComponentB.Controllers
{
    [ApiController]
    [Route("api/agentworkflows")]
    public class AgentWorkflowsController(IPlannerAgentService plannerAgent) : ControllerBase
    {
        private readonly IPlannerAgentService _plannerAgent = plannerAgent;

        // POST /api/agentworkflows/trigger
        // Starts a new workflow for a given HelpRequest (or Incident, once that exists).
        // Source: Proposal Section 9 example workflow.
        [HttpPost("trigger")]
        public async Task<ActionResult<AgentWorkflowResponseDto>> Trigger([FromBody] TriggerWorkflowDto dto)
        {
            var result = await _plannerAgent.TriggerAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }

        // GET /api/agentworkflows/{id}
        [HttpGet("{id}")]
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
        public async Task<ActionResult<AgentWorkflowResponseDto>> Decide(Guid id, [FromBody] ApprovalDecisionDto dto)
        {
            // TODO once JWT auth is wired up: read the real coordinator user id from claims
            var coordinatorUserId = Guid.NewGuid(); // placeholder until auth is in place

            var result = await _plannerAgent.DecideApprovalAsync(id, coordinatorUserId, dto);
            if (result is null) return NotFound();
            return Ok(result);
        }
    }
}