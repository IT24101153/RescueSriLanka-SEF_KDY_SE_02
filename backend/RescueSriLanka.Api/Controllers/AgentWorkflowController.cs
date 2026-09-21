using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RescueSriLanka.Api.Agents.Orchestration;
using RescueSriLanka.Api.DTOs;

namespace RescueSriLanka.Api.Controllers
{
    [ApiController]
    [Route("api/agents/workflows")]
    [Authorize(Roles = "EmergencyCoordinator")]
    public class AgentWorkflowController : ControllerBase
    {
        private readonly IAgentOrchestrator _orchestrator;

        public AgentWorkflowController(IAgentOrchestrator orchestrator)
        {
            _orchestrator = orchestrator;
        }

        // Triggers analysis, resource recommendation, and safety validation.
        // The workflow produces a recommendation only; dispatch approval and
        // operational execution remain an EmergencyCoordinator responsibility.
        [HttpPost]
        public async Task<ActionResult<AgentWorkflowDto>> Start(StartWorkflowDto dto)
        {
            var result = await _orchestrator.StartWorkflowAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<AgentWorkflowDto>> GetById(Guid id)
        {
            var result = await _orchestrator.GetWorkflowAsync(id);
            return result is null ? NotFound() : Ok(result);
        }
    }
}
