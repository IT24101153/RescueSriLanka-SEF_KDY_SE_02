using System;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Features.ComponentA.Services;
using RescueSriLanka.Api.Features.ComponentB.Agents.PlannerAgent;
using RescueSriLanka.Api.Features.ComponentB.DTOs;
using RescueSriLanka.Api.Features.ComponentB.Models;
using RescueSriLanka.Api.Features.ComponentB.Services;
using RescueSriLanka.Api.Services.Email;
using RescueSriLanka.Api.Services.Storage;

namespace RescueSriLanka.Api.Features.ComponentB.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HelpRequestsController(
        IHelpRequestService service,
        AppDbContext db,
        IPlannerAgentService plannerAgent,
        IActionEmailService emails,
        IImageStore images,
        IEmergencyContactService emergencyContacts,
        IHelpRequestMessageService messages) : ControllerBase
    {
        // Either coordinator may triage help requests; the web console sends
        // HelpRequestManager accounts to the Help request dashboard.
        private const string Coordinators =
            nameof(UserRole.EmergencyCoordinator) + "," + nameof(UserRole.HelpRequestManager);

        private readonly IHelpRequestService _service = service;
        private readonly AppDbContext _db = db;
        private readonly IPlannerAgentService _plannerAgent = plannerAgent;
        private readonly IImageStore _images = images;

        // POST /api/helprequests
        // Citizen/tourist submits a new help request (Flutter app). Requires login.
        [HttpPost]
        [Authorize(Roles = nameof(UserRole.Citizen))]
        public async Task<ActionResult<HelpRequestResponseDto>> Create([FromBody] CreateHelpRequestDto dto)
        {
            var citizenId = GetUserId();
            if (citizenId is null) return Unauthorized();

            try
            {
                var result = await _service.CreateAsync(citizenId.Value, dto);
                await emails.HelpRequestSubmittedAsync(result.Id);
                return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // POST /api/helprequests/photo
        // A citizen's photo for a help request. It is uploaded to Cloudinary and the
        // URL comes back for the request to carry in ImageUrl; the API keeps no copy.
        [HttpPost("photo")]
        [Authorize(Roles = nameof(UserRole.Citizen))]
        public async Task<ActionResult<HelpRequestPhotoResponseDto>> UploadPhoto(IFormFile? photo, CancellationToken ct)
        {
            var citizenId = GetUserId();
            if (citizenId is null) return Unauthorized();

            if (photo is null)
            {
                return BadRequest(new { message = "Attach the photo as the form field 'photo'." });
            }

            try
            {
                ImageStorageService.Validate(photo);
                var stored = await _images.SaveAsync(citizenId.Value, "helprequests", photo, ct);
                return Ok(new HelpRequestPhotoResponseDto { Url = stored.Location });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                // The store refused the upload — report it rather than return a URL that points at nothing.
                return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message });
            }
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
        [Authorize(Roles = nameof(UserRole.Citizen))]
        public async Task<ActionResult<List<HelpRequestResponseDto>>> GetMine()
        {
            var citizenId = GetUserId();
            if (citizenId is null) return Unauthorized();

            var result = await _service.GetByCitizenAsync(citizenId.Value);
            return Ok(result);
        }

        // AI assessment is a staff tool: it is generated server-side, the API key is
        // never exposed to clients, and citizens neither see nor trigger it.
        [HttpPost("{id}/ai-analysis")]
        [EnableRateLimiting("ai")]
        [Authorize(Roles = Coordinators)]
        public async Task<ActionResult<AiAnalysisResult>> Analyze(Guid id)
        {
            var request = await _service.GetByIdAsync(id);
            if (request is null) return NotFound();
            if (!CanAccess(request)) return Forbid();

            // The workflow persists the Incident Analysis result, allowing the request
            // list to show its priority badge later without calling Gemini again.
            var workflow = await _plannerAgent.TriggerAsync(new TriggerWorkflowDto
            {
                ObjectiveType = PlannerWorkflowObjectiveType.HelpRequest,
                ObjectiveId = id
            });
            var step = workflow.Steps.FirstOrDefault(s => s.TargetAgent == PlannerAgentType.IncidentAnalysisAgent);
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
        // Also carries the workflow id/status so the review screen can offer an
        // Approve/Reject decision on it via POST /api/agentworkflows/{id}/decision
        // without a second round-trip to look the workflow up by help-request id.
        [HttpGet("{id}/ai-priority")]
        [Authorize(Roles = Coordinators)]
        public async Task<ActionResult<AiPriorityResponseDto>> GetAiPriority(Guid id)
        {
            var request = await _service.GetByIdAsync(id);
            if (request is null) return NotFound();
            if (!CanAccess(request)) return Forbid();

            // A re-run that failed validation, or a plan a newer run replaced, must not
            // hide the plan the manager can actually act on, so those come last.
            var workflow = await _db.AgentWorkflows
                .Include(w => w.Steps)
                .Where(w => w.ObjectiveType == PlannerWorkflowObjectiveType.HelpRequest && w.ObjectiveId == id)
                .OrderBy(w => w.Status == PlannerWorkflowStatus.Failed || w.Status == PlannerWorkflowStatus.Superseded)
                .ThenByDescending(w => w.CreatedAt)
                .FirstOrDefaultAsync();

            if (workflow is null)
                return Ok(new AiPriorityResponseDto { Priority = "Analysis pending", AiAnalysisAvailable = false });

            var response = new AiPriorityResponseDto
            {
                Priority = "Analysis pending",
                AiAnalysisAvailable = false,
                WorkflowId = workflow.Id,
                WorkflowStatus = workflow.Status
            };

            var step = workflow.Steps
                .Where(s => s.TargetAgent == PlannerAgentType.IncidentAnalysisAgent && s.ToolResultJson != null)
                .OrderByDescending(s => s.CompletedAt)
                .FirstOrDefault();

            if (step?.ToolResultJson is null) return Ok(response);

            try
            {
                using var document = JsonDocument.Parse(step.ToolResultJson);
                var root = document.RootElement;
                var priority = root.TryGetProperty("severity", out var severity) ? severity.GetString() : null;
                var available = root.TryGetProperty("aiAnalysisAvailable", out var aiAvailable) && aiAvailable.GetBoolean();
                response.Priority = string.IsNullOrWhiteSpace(priority) ? "Analysis pending" : priority;
                response.AiAnalysisAvailable = available;
                response.Reasoning = available && root.TryGetProperty("aiReasoning", out var reasoning) ? reasoning.GetString() : null;
                response.SuggestedAction = available && root.TryGetProperty("aiSuggestedAction", out var suggestedAction) ? suggestedAction.GetString() : null;
                response.CredibilitySignal = available && root.TryGetProperty("aiCredibilitySignal", out var credibility) ? credibility.GetString() : null;
                response.ModelPriority = root.TryGetProperty("modelPriority", out var modelPriority) && modelPriority.ValueKind == JsonValueKind.String ? modelPriority.GetString() : null;
                response.UnavailableReason = !available && root.TryGetProperty("modelUnavailableReason", out var reason) && reason.ValueKind == JsonValueKind.String ? reason.GetString() : null;
                ReadRecommendedTeam(workflow, response);
                return Ok(response);
            }
            catch (JsonException)
            {
                return Ok(response);
            }
        }

        // Step 3 records which team approving would assign, and whether the model or a rule chose it.
        private static void ReadRecommendedTeam(AgentWorkflow workflow, AiPriorityResponseDto response)
        {
            var validation = workflow.Steps.FirstOrDefault(s => s.StepNumber == 3)?.ValidationResultJson;
            if (validation is null) return;

            try
            {
                using var document = JsonDocument.Parse(validation);
                var root = document.RootElement;
                response.RecommendedTeam = root.TryGetProperty("recommendedTeam", out var team) && team.ValueKind == JsonValueKind.String ? team.GetString() : null;
                response.RecommendedTeamSource = response.RecommendedTeam is not null && root.TryGetProperty("recommendedTeamSource", out var source) && source.ValueKind == JsonValueKind.String ? source.GetString() : null;
            }
            catch (JsonException)
            {
                // A malformed validation record only means no team is shown.
            }
        }

        private Guid? GetUserId()
        {
            var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(idClaim, out var id) ? id : null;
        }

        // GET /api/helprequests/emergency-contacts?latitude=&longitude=&district=
        // Public emergency numbers for a place: the national ones plus the district
        // disaster-management unit nearest the coordinates (or named by district).
        // No personal data, and wanted most when sign-in is the last thing on anyone's mind.
        [HttpGet("emergency-contacts")]
        [AllowAnonymous]
        public async Task<ActionResult<EmergencyContactsResponseDto>> GetEmergencyContacts(
            [FromQuery] double? latitude, [FromQuery] double? longitude, [FromQuery] string? district,
            CancellationToken ct)
        {
            if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
                return BadRequest(new { message = "Latitude or longitude is out of range." });

            return Ok(await emergencyContacts.GetForAreaAsync(latitude, longitude, district, ct));
        }

        // GET /api/helprequests/{id}/emergency-contacts
        // The numbers for where this request was made, matched on its coordinates.
        [HttpGet("{id}/emergency-contacts")]
        [Authorize]
        public async Task<ActionResult<EmergencyContactsResponseDto>> GetRequestEmergencyContacts(
            Guid id, CancellationToken ct)
        {
            var request = await _service.GetByIdAsync(id);
            if (request is null) return NotFound();
            if (!CanAccess(request)) return Forbid();

            return Ok(await emergencyContacts.GetForAreaAsync(
                request.Latitude, request.Longitude, request.District, ct));
        }

        // POST /api/helprequests/{id}/messages
        // The response team sends the citizen a note and/or Do/Don't safety guidance.
        [HttpPost("{id}/messages")]
        [Authorize(Roles = Coordinators)]
        public async Task<ActionResult<HelpRequestMessageDto>> AddMessage(
            Guid id, [FromBody] CreateHelpRequestMessageDto dto, CancellationToken ct)
        {
            var authorId = GetUserId();
            if (authorId is null) return Unauthorized();

            try
            {
                var result = await messages.AddAsync(id, authorId.Value, dto, ct);
                return result is null ? NotFound() : Created($"/api/helprequests/{id}/messages", result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // GET /api/helprequests/{id}/messages
        // What the response team has told the citizen, for them and for staff.
        [HttpGet("{id}/messages")]
        [Authorize]
        public async Task<ActionResult<List<HelpRequestMessageDto>>> GetMessages(Guid id, CancellationToken ct)
        {
            var request = await _service.GetByIdAsync(id);
            if (request is null) return NotFound();
            if (!CanAccess(request)) return Forbid();

            return Ok(await messages.GetAsync(id, ct));
        }

        // DELETE /api/helprequests/{id}/messages/{messageId}
        // Takes back a message sent by mistake.
        [HttpDelete("{id}/messages/{messageId:guid}")]
        [Authorize(Roles = Coordinators)]
        public async Task<IActionResult> DeleteMessage(Guid id, Guid messageId, CancellationToken ct)
        {
            return await messages.DeleteAsync(id, messageId, ct) ? NoContent() : NotFound();
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

        // PUT /api/helprequests/{id}
        // The requester can correct a pending, unverified request before triage starts.
        [HttpPut("{id}")]
        [Authorize(Roles = nameof(UserRole.Citizen))]
        public async Task<ActionResult<HelpRequestResponseDto>> Update(Guid id, [FromBody] UpdateHelpRequestDto dto)
        {
            var citizenId = GetUserId();
            if (citizenId is null) return Unauthorized();

            try
            {
                var result = await _service.UpdateAsync(id, citizenId.Value, dto);
                return result is null ? NotFound() : Ok(result);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }

        // DELETE /api/helprequests/{id}
        // Requests are never hard-deleted: DELETE safely changes a pending request to Cancelled
        // and keeps the audit trail required for emergency coordination.
        [HttpDelete("{id}")]
        [Authorize(Roles = nameof(UserRole.Citizen))]
        public async Task<IActionResult> Delete(Guid id)
        {
            var citizenId = GetUserId();
            if (citizenId is null) return Unauthorized();

            var request = await _service.GetByIdAsync(id);
            if (request is null) return NotFound();
            if (request.CitizenId != citizenId.Value) return Forbid();
            if (request.Status != HelpRequestStatus.Pending) return Conflict(new
            {
                message = "Only a pending request can be cancelled by its requester."
            });

            var result = await _service.UpdateStatusAsync(id, citizenId.Value,
                new UpdateHelpRequestStatusDto
                {
                    NewStatus = HelpRequestStatus.Cancelled,
                    Notes = "Cancelled by requester."
                });
            if (result is null) return NotFound();
            await emails.HelpRequestCancelledAsync(id);
            return NoContent();
        }

        // PATCH /api/helprequests/{id}/status
        // Coordinator changes status for Water, Food, Medical, Shelter and Other requests.
        // Rescue requests belong to the Rescue Coordinator in Component D, whose assignments
        // and dispatches drive the status, so it cannot be changed by hand here.
        [HttpPatch("{id}/status")]
        [Authorize(Roles = Coordinators)]
        public async Task<ActionResult<HelpRequestResponseDto>> UpdateStatus(Guid id, [FromBody] UpdateHelpRequestStatusDto dto)
        {
            var changedByUserId = GetUserId();
            if (changedByUserId is null) return Unauthorized();

            var current = await _service.GetByIdAsync(id);
            if (current is { Type: HelpRequestType.Rescue })
                return Conflict(new { message = "Rescue requests are managed by the Rescue Coordinator. Their status follows the rescue response." });

            try
            {
                var result = await _service.UpdateStatusAsync(id, changedByUserId.Value, dto);
                if (result is null) return NotFound();
                await emails.HelpRequestStatusChangedAsync(id, dto.NewStatus.ToString());
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
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
            var verifiedByUserId = GetUserId();
            if (verifiedByUserId is null) return Unauthorized();

            var result = await _service.VerifyAsync(id, verifiedByUserId.Value, dto);
            if (result is null) return NotFound();
            await emails.HelpRequestVerifiedAsync(id, dto.IsReal);
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
