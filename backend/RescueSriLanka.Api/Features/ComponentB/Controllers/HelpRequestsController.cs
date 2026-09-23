using System;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Features.ComponentB.Agents.PlannerAgent;
using RescueSriLanka.Api.Features.ComponentB.DTOs;
using RescueSriLanka.Api.Features.ComponentB.Models;
using RescueSriLanka.Api.Features.ComponentB.Services;

namespace RescueSriLanka.Api.Features.ComponentB.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HelpRequestsController(
        IHelpRequestService service,
        IAiAnalysisService aiAnalysis,
        AppDbContext db,
        IPlannerAgentService plannerAgent) : ControllerBase
    {
        // Either coordinator may triage help requests; the web console sends
        // HelpRequestManager accounts to the Help request dashboard.
        private const string Coordinators =
            nameof(UserRole.EmergencyCoordinator) + "," + nameof(UserRole.HelpRequestManager);

        private readonly IHelpRequestService _service = service;
        private readonly IAiAnalysisService _aiAnalysis = aiAnalysis;
        private readonly AppDbContext _db = db;
        private readonly IPlannerAgentService _plannerAgent = plannerAgent;

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
        [Authorize(Roles = Coordinators)]
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

        // Pre-submission guidance improves a report but never prevents submission.
        [HttpPost("ai-draft-analysis")]
        [Authorize]
        public async Task<ActionResult<AiAnalysisResult>> AnalyzeDraft([FromBody] AnalyzeRequestDraftDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Description))
                return BadRequest(new { message = "Add a short description before requesting AI guidance." });

            var urgencyScore = dto.Type switch
            {
                HelpRequestType.Medical => 80,
                HelpRequestType.Rescue => 90,
                HelpRequestType.Shelter => 50,
                HelpRequestType.Water => 40,
                HelpRequestType.Food => 30,
                _ => 20
            };
            var result = await _aiAnalysis.AnalyzeHelpRequestAsync(dto.Type.ToString(), dto.Description, urgencyScore);
            return result is null
                ? StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "AI guidance is temporarily unavailable. You can still submit your request." })
                : Ok(result);
        }

        // AI guidance is generated server-side; the API key is never exposed to Flutter.
        // Only the citizen who created the request may view its analysis.
        [HttpPost("{id}/ai-analysis")]
        [Authorize]
        public async Task<ActionResult<AiAnalysisResult>> Analyze(Guid id)
        {
            var citizenId = GetUserId();
            if (citizenId is null) return Unauthorized();

            var request = await _service.GetByIdAsync(id);
            if (request is null) return NotFound();
            if (request.CitizenId != citizenId.Value) return Forbid();

            // The workflow persists the Incident Analysis result, allowing the request
            // list to show its priority badge later without calling Gemini again.
            var workflow = await _plannerAgent.TriggerAsync(new TriggerWorkflowDto
            {
                ObjectiveType = WorkflowObjectiveType.HelpRequest,
                ObjectiveId = id
            });
            var step = workflow.Steps.FirstOrDefault(s => s.TargetAgent == AgentType.IncidentAnalysisAgent);
            if (step?.ToolResultJson is null)
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "AI guidance is temporarily unavailable. Your request has still been submitted." });

            try
            {
                using var document = JsonDocument.Parse(step.ToolResultJson);
                var root = document.RootElement;
                var available = root.TryGetProperty("aiAnalysisAvailable", out var aiAvailable) && aiAvailable.GetBoolean();
                if (!available)
                    return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "AI guidance is temporarily unavailable. Your request has still been submitted." });

                return Ok(new AiAnalysisResult
                {
                    Reasoning = root.TryGetProperty("aiReasoning", out var reasoning) ? reasoning.GetString() ?? string.Empty : string.Empty,
                    SuggestedAction = root.TryGetProperty("aiSuggestedAction", out var suggestedAction) ? suggestedAction.GetString() ?? string.Empty : string.Empty,
                    CredibilitySignal = root.TryGetProperty("aiCredibilitySignal", out var credibility) ? credibility.GetString() ?? string.Empty : string.Empty
                });
            }
            catch (JsonException)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "AI guidance is temporarily unavailable. Your request has still been submitted." });
            }
        }

        // Returns an existing workflow result only; it never invokes Gemini from the list screen.
        [HttpGet("{id}/ai-priority")]
        [Authorize]
        public async Task<ActionResult<AiPriorityResponseDto>> GetAiPriority(Guid id)
        {
            var citizenId = GetUserId();
            if (citizenId is null) return Unauthorized();

            var request = await _service.GetByIdAsync(id);
            if (request is null) return NotFound();
            if (request.CitizenId != citizenId.Value) return Forbid();

            var step = await _db.AgentWorkflows
                .Where(w => w.ObjectiveType == WorkflowObjectiveType.HelpRequest && w.ObjectiveId == id)
                .OrderByDescending(w => w.CreatedAt)
                .SelectMany(w => w.Steps)
                .Where(s => s.TargetAgent == AgentType.IncidentAnalysisAgent && s.ToolResultJson != null)
                .OrderByDescending(s => s.CompletedAt)
                .FirstOrDefaultAsync();

            if (step?.ToolResultJson is null)
                return Ok(new AiPriorityResponseDto { Priority = "Analysis pending", AiAnalysisAvailable = false });

            try
            {
                using var document = JsonDocument.Parse(step.ToolResultJson);
                var root = document.RootElement;
                var priority = root.TryGetProperty("severity", out var severity) ? severity.GetString() : null;
                var available = root.TryGetProperty("aiAnalysisAvailable", out var aiAvailable) && aiAvailable.GetBoolean();
                return Ok(new AiPriorityResponseDto
                {
                    Priority = string.IsNullOrWhiteSpace(priority) ? "Analysis pending" : priority,
                    AiAnalysisAvailable = available
                });
            }
            catch (JsonException)
            {
                return Ok(new AiPriorityResponseDto { Priority = "Analysis pending", AiAnalysisAvailable = false });
            }
        }

        private Guid? GetUserId()
        {
            var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(idClaim, out var id) ? id : null;
        }

        // GET /api/helprequests/{id}
        [HttpGet("{id}")]
        [Authorize]
        public async Task<ActionResult<HelpRequestResponseDto>> GetById(Guid id)
        {
            var result = await _service.GetByIdAsync(id);
            if (result is null) return NotFound();
            if (!CanAccess(result)) return Forbid();
            return Ok(result);
        }

        // PATCH /api/helprequests/{id}/status
        // Coordinator changes status (or system/agent does, post-approval)
        [HttpPatch("{id}/status")]
        [Authorize(Roles = Coordinators)]
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
        [Authorize]
        public async Task<ActionResult<List<StatusHistoryDto>>> GetHistory(Guid id)
        {
            var request = await _service.GetByIdAsync(id);
            if (request is null) return NotFound();
            if (!CanAccess(request)) return Forbid();
            var result = await _service.GetHistoryAsync(id);
            return Ok(result);
        }

        // PATCH /api/helprequests/{id}/verify
        // Admin marks a citizen report as real or fake before it's treated as legitimate.
        [HttpPatch("{id}/verify")]
        [Authorize(Roles = Coordinators)]
        public async Task<ActionResult<HelpRequestResponseDto>> Verify(Guid id, [FromBody] VerifyHelpRequestDto dto)
        {
            // TODO once JWT auth is wired in the frontend: read the real admin user id from claims
            var verifiedByUserId = Guid.NewGuid(); // placeholder until auth claims are read here

            var result = await _service.VerifyAsync(id, verifiedByUserId, dto);
            if (result is null) return NotFound();
            return Ok(result);
        }

        private bool CanAccess(HelpRequestResponseDto request)
        {
            var citizenId = GetUserId();
            return User.IsInRole(nameof(UserRole.EmergencyCoordinator))
                || User.IsInRole(nameof(UserRole.HelpRequestManager))
                || citizenId == request.CitizenId;
        }
    }
}
