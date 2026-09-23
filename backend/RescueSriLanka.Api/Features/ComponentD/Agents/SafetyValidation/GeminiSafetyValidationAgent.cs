using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.DTOs;
using RescueSriLanka.Api.Features.ComponentD.Models;
using RescueSriLanka.Api.Features.ComponentD.DTOs;
using RescueSriLanka.Api.Features.ComponentD.Data;

namespace RescueSriLanka.Api.Features.ComponentD.Agents.SafetyValidation;
public interface IAssignmentSafetyValidationAgent
{
    Task<SafetyValidationWorkflowResultDto> ValidateAsync(Guid assignmentId, CancellationToken cancellationToken = default);
}

// Isolates Gemini's evolving wire protocol so normal tests can use a fake.
public interface IGeminiSafetyValidationClient
{
    Task<GeminiSafetyAgentResponse> GetNextResponseAsync(
        AssignmentValidationContextDto context,
        IReadOnlyList<object> priorToolResults,
        string? previousInteractionId,
        CancellationToken cancellationToken);
}

public sealed class GeminiSafetyValidationAgent : IAssignmentSafetyValidationAgent
{
    private const int MaxAgentSteps = 12;
    private readonly ComponentDDbContext _db;
    private readonly SafetyValidationTools _tools;
    private readonly IGeminiSafetyValidationClient _gemini;
    private readonly ILogger<GeminiSafetyValidationAgent> _logger;

    public GeminiSafetyValidationAgent(
        ComponentDDbContext db,
        SafetyValidationTools tools,
        IGeminiSafetyValidationClient gemini,
        ILogger<GeminiSafetyValidationAgent> logger)
    {
        _db = db;
        _tools = tools;
        _gemini = gemini;
        _logger = logger;
    }

    public async Task<SafetyValidationWorkflowResultDto> ValidateAsync(
        Guid assignmentId, CancellationToken cancellationToken = default)
    {
        var context = await _tools.GetAssignmentContextAsync(assignmentId);
        if (context is null)
            return SafeFailure(null, assignmentId, null, "Assignment was not found.", "ASSIGNMENT_EXISTS");

        var workflow = new AgentWorkflow
        {
            // The shared schema has only Incident/HelpRequest objectives. The
            // assignment being validated is captured explicitly in JSON.
            ObjectiveType = context.IncidentId.HasValue ? WorkflowObjectiveType.Incident : WorkflowObjectiveType.HelpRequest,
            ObjectiveId = context.IncidentId ?? context.HelpRequestId ?? context.AssignmentId,
            ObjectiveSnapshotJson = JsonSerializer.Serialize(context),
            PlanJson = JsonSerializer.Serialize(new
            {
                validation = "GeminiSafetyValidation",
                assignmentId = context.AssignmentId,
                planVersion = context.PlanVersion,
                requiredChecks = SafetyValidationTools.AllowedToolNames
            }),
            Status = WorkflowStatus.Executing
        };
        _db.AgentWorkflows.Add(workflow);
        await _db.SaveChangesAsync(cancellationToken);

        var priorResults = new List<object>();
        SafetyValidationDecision? modelDecision = null;
        string? modelSummary = null;
        IReadOnlyList<string>? modelActions = null;
        string? providerFailure = null;
        string? previousInteractionId = null;
        var stepNumber = 0;

        for (var iteration = 1; iteration <= MaxAgentSteps && modelDecision is null && providerFailure is null; iteration++)
        {
            GeminiSafetyAgentResponse response;
            try
            {
                response = await _gemini.GetNextResponseAsync(context, priorResults, previousInteractionId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gemini safety validation failed for assignment {AssignmentId}", assignmentId);
                providerFailure = "Gemini safety validation is unavailable; human review is required.";
                break;
            }

            if (response.Decision is not null)
            {
                modelDecision = response.Decision;
                modelSummary = response.Summary;
                modelActions = response.SuggestedActions;
                break;
            }

            previousInteractionId = response.InteractionId;

            if (response.ToolCalls.Count == 0)
            {
                providerFailure = "Gemini returned neither a valid decision nor a tool call.";
                break;
            }

            if (string.IsNullOrWhiteSpace(previousInteractionId))
            {
                providerFailure = "Gemini function-call response omitted the required interaction ID.";
                break;
            }

            // Interactions API continuations accept only the results for the
            // immediately preceding interaction, not an accumulated history.
            priorResults.Clear();

            foreach (var call in response.ToolCalls)
            {
                if (++stepNumber > MaxAgentSteps)
                {
                    providerFailure = $"Safety validation exceeded the {MaxAgentSteps}-step limit.";
                    break;
                }

                if (string.IsNullOrWhiteSpace(call.CallId))
                {
                    providerFailure = "Gemini function call omitted the required call ID.";
                    await PersistStepAsync(workflow.Id, stepNumber, call.Name, call.Arguments.GetRawText(),
                        new SafetyValidationCheckDto("TOOL_CALL", false, providerFailure), StepStatus.Failed, cancellationToken);
                    break;
                }

                var check = await _tools.ExecuteAsync(call.Name, call.Arguments, context.AssignmentId, context.PlanVersion);
                if (check is null)
                {
                    providerFailure = "Gemini requested an unknown tool or supplied malformed tool arguments.";
                    await PersistStepAsync(workflow.Id, stepNumber, call.Name, call.Arguments.GetRawText(),
                        new SafetyValidationCheckDto("TOOL_CALL", false, providerFailure), StepStatus.Failed, cancellationToken);
                    break;
                }

                await PersistStepAsync(workflow.Id, stepNumber, call.Name, call.Arguments.GetRawText(), check,
                    StepStatus.Completed, cancellationToken);
                priorResults.Add(new
                {
                    type = "function_result",
                    name = call.Name,
                    call_id = call.CallId,
                    result = check
                });
            }
        }

        if (modelDecision is null && providerFailure is null)
            providerFailure = $"Safety validation exceeded the {MaxAgentSteps}-step limit.";

        // These are mandatory independent deterministic checks. Gemini can ask
        // for them in any order, but can never skip or override them.
        IReadOnlyList<SafetyValidationCheckDto> mandatory;
        try
        {
            mandatory = await _tools.RunMandatoryChecksAsync(context.AssignmentId, context.PlanVersion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mandatory safety checks failed for assignment {AssignmentId}", assignmentId);
            mandatory = [new("MANDATORY_CHECKS_COMPLETED", false, "Deterministic safety checks could not be completed.")];
        }

        foreach (var check in mandatory)
        {
            await PersistStepAsync(workflow.Id, ++stepNumber, "mandatory_" + check.Name.ToLowerInvariant(),
                JsonSerializer.Serialize(new { assignmentId = context.AssignmentId, planVersion = context.PlanVersion }),
                check, StepStatus.Completed, cancellationToken);
        }

        var current = await _tools.GetAssignmentContextAsync(context.AssignmentId);
        var stale = current is null || current.PlanVersion != context.PlanVersion;
        var allMandatoryPassed = mandatory.Count == 10 && mandatory.All(c => c.Passed) && !stale;
        var failedChecks = mandatory.Where(c => !c.Passed).Select(c => c.Name).ToList();
        if (stale) failedChecks.Add("PLAN_VERSION_CURRENT");
        if (providerFailure is not null) failedChecks.Add("GEMINI_PROVIDER");

        var decision = modelDecision == SafetyValidationDecision.REJECT
            ? SafetyValidationDecision.REJECT
            : modelDecision == SafetyValidationDecision.APPROVE && allMandatoryPassed && providerFailure is null
                ? SafetyValidationDecision.APPROVE
                : SafetyValidationDecision.REVISE;
        var summary = providerFailure
            ?? (stale ? "Assignment changed during validation; revalidation is required."
                : decision == SafetyValidationDecision.APPROVE
                    ? modelSummary ?? "All deterministic safety checks passed. Human approval is still required."
                    : modelSummary ?? "Safety validation requires revision or human review.");
        var actions = modelActions?.ToList() ?? new List<string>();
        if (decision != SafetyValidationDecision.APPROVE && actions.Count == 0)
            actions.Add("Review and revise the assignment, then run validation again.");

        var result = new SafetyValidationWorkflowResultDto(
            workflow.Id, context.AssignmentId, context.PlanVersion, decision, summary,
            mandatory, failedChecks, actions, WorkflowStatus.AwaitingApproval, stale);

        workflow.Status = WorkflowStatus.AwaitingApproval;
        workflow.FinalOutcomeJson = JsonSerializer.Serialize(result);
        workflow.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return result;
    }

    private async Task PersistStepAsync(Guid workflowId, int number, string action, string input, SafetyValidationCheckDto result,
        StepStatus status, CancellationToken cancellationToken)
    {
        _db.AgentSteps.Add(new AgentStep
        {
            AgentWorkflowId = workflowId,
            StepNumber = number,
            TargetAgent = AgentType.SafetyValidationAgent,
            Action = action,
            InputParamsJson = input,
            ToolResultJson = JsonSerializer.Serialize(result),
            ValidationResultJson = JsonSerializer.Serialize(result),
            Status = status,
            CompletedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static SafetyValidationWorkflowResultDto SafeFailure(Guid? workflowId, Guid assignmentId, int? planVersion,
        string summary, string failedCheck) => new(
        workflowId, assignmentId, planVersion, SafetyValidationDecision.REVISE, summary,
        [new SafetyValidationCheckDto(failedCheck, false, summary)], [failedCheck],
        ["Review the assignment and retry validation."], WorkflowStatus.Failed, false);
}

public sealed class GeminiSafetyValidationClient : IGeminiSafetyValidationClient
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly string _apiKey;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<GeminiSafetyValidationClient> _logger;

    public GeminiSafetyValidationClient(
        HttpClient httpClient,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<GeminiSafetyValidationClient> logger)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _model = configuration["Gemini:Model"] ?? "gemini-3-flash-preview";
        _apiKey = configuration["Gemini:ApiKey"] ?? throw new InvalidOperationException("Gemini:ApiKey is not configured.");
        _environment = environment;
        _logger = logger;
    }

    public async Task<GeminiSafetyAgentResponse> GetNextResponseAsync(
        AssignmentValidationContextDto context, IReadOnlyList<object> priorToolResults,
        string? previousInteractionId, CancellationToken cancellationToken)
    {
        const string instruction = "You are a safety-validation agent. Use only supplied read-only tools. Operational facts come only from tool results; never assume missing facts. You cannot dispatch or mutate anything. A human EmergencyCoordinator must approve any recommendation. After tool use, return only a JSON object matching the required decision schema; do not return prose or Markdown.";
        var payload = new Dictionary<string, object?>
        {
            ["model"] = _model,
            ["input"] = priorToolResults.Count == 0
                ? (object)$"Validate assignment {context.AssignmentId} at plan version {context.PlanVersion}."
                : priorToolResults,
            ["tools"] = new SafetyValidationToolsSchemas().Schemas,
            ["system_instruction"] = instruction,
            ["response_format"] = new
            {
                type = "text",
                mime_type = "application/json",
                schema = new
                {
                    type = "object",
                    properties = new
                    {
                        decision = new { type = "string", @enum = new[] { "APPROVE", "REVISE", "REJECT" } },
                        summary = new { type = "string" },
                        failedChecks = new { type = "array", items = new { type = "string" } },
                        suggestedActions = new { type = "array", items = new { type = "string" } }
                    },
                    required = new[] { "decision", "summary", "failedChecks", "suggestedActions" },
                    additionalProperties = false
                }
            }
        };
        if (!string.IsNullOrWhiteSpace(previousInteractionId))
            payload["previous_interaction_id"] = previousInteractionId;
        using var request = new HttpRequestMessage(HttpMethod.Post, "interactions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-goog-api-key", _apiKey);
        request.Headers.Add("Api-Revision", "2026-05-20");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            _logger.LogWarning("Gemini safety validation returned HTTP status {StatusCode}.", (int)response.StatusCode);
        response.EnsureSuccessStatusCode();
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        LogDevelopmentResponseMetadata(raw);
        return Parse(raw);
    }

    public static GeminiSafetyAgentResponse Parse(string raw)
    {
        try
        {
            return ParseCore(raw);
        }
        catch (JsonException)
        {
            return new([], null, null, null);
        }
    }

    private static GeminiSafetyAgentResponse ParseCore(string raw)
    {
        using var document = JsonDocument.Parse(raw);
        var calls = new List<GeminiSafetyToolCall>();
        string? text = null;
        var interactionId = document.RootElement.TryGetProperty("id", out var interactionIdElement)
            ? interactionIdElement.GetString()
            : null;
        if (document.RootElement.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Array)
        {
            foreach (var step in steps.EnumerateArray())
            {
                if (step.TryGetProperty("type", out var type) && type.GetString() == "function_call")
                {
                    if (step.TryGetProperty("name", out var name) && step.TryGetProperty("arguments", out var args))
                    {
                        step.TryGetProperty("id", out var id);
                        calls.Add(new GeminiSafetyToolCall(name.GetString() ?? string.Empty, args.Clone(), id.GetString()));
                    }
                }
                else if (step.TryGetProperty("type", out type) && type.GetString() == "model_output"
                    && step.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in content.EnumerateArray())
                    {
                        if (item.TryGetProperty("type", out var contentType) && contentType.GetString() == "text"
                            && item.TryGetProperty("text", out var textValue))
                        {
                            text = textValue.GetString();
                            break;
                        }
                    }
                }
            }
        }
        if (calls.Count > 0) return new(calls, null, null, null, interactionId);
        if (string.IsNullOrWhiteSpace(text)) return new([], null, null, null, interactionId);
        try
        {
            var parsed = JsonSerializer.Deserialize<GeminiFinalDecision>(NormalizeJson(text), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return parsed is not null
                && !string.IsNullOrWhiteSpace(parsed.Summary)
                && parsed.FailedChecks is not null
                && parsed.SuggestedActions is not null
                && Enum.TryParse<SafetyValidationDecision>(parsed.Decision, true, out var decision)
                ? new([], decision, parsed.Summary, parsed.SuggestedActions, interactionId)
                : new([], null, null, null, interactionId);
        }
        catch (JsonException) { return new([], null, null, null, interactionId); }
    }

    private sealed class GeminiFinalDecision
    {
        public string? Decision { get; set; }
        public string? Summary { get; set; }
        public List<string>? FailedChecks { get; set; }
        public List<string>? SuggestedActions { get; set; }
    }

    private static string NormalizeJson(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return trimmed;

        var firstNewline = trimmed.IndexOf('\n');
        if (firstNewline < 0) return trimmed;
        var body = trimmed[(firstNewline + 1)..];
        var closingFence = body.LastIndexOf("```", StringComparison.Ordinal);
        return (closingFence >= 0 ? body[..closingFence] : body).Trim();
    }

    private void LogDevelopmentResponseMetadata(string raw)
    {
        if (!_environment.IsDevelopment()) return;
        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            var hasInteractionId = root.TryGetProperty("id", out _);
            var steps = root.TryGetProperty("steps", out var stepValue) && stepValue.ValueKind == JsonValueKind.Array
                ? stepValue.EnumerateArray().ToList()
                : [];
            var stepTypes = steps
                .Select(step => step.TryGetProperty("type", out var type) ? type.GetString() : null)
                .Where(type => !string.IsNullOrWhiteSpace(type))
                .ToArray();
            var functionCallCount = stepTypes.Count(type => type == "function_call");
            var hasText = steps.Any(step => step.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.Array
                && content.EnumerateArray().Any(item => item.TryGetProperty("type", out var type) && type.GetString() == "text"));
            _logger.LogInformation("Gemini safety response metadata: InteractionIdPresent={InteractionIdPresent}, StepCount={StepCount}, StepTypes={StepTypes}, FunctionCallCount={FunctionCallCount}, HasText={HasText}.",
                hasInteractionId, steps.Count, string.Join(',', stepTypes), functionCallCount, hasText);
        }
        catch (JsonException)
        {
            _logger.LogWarning("Gemini safety response metadata could not be inspected because the response was not valid JSON.");
        }
    }

    private sealed class SafetyValidationToolsSchemas
    {
        public object[] Schemas { get; } = SafetyValidationTools.AllowedToolNames.Select(name => (object)new
        {
            type = "function", name, description = $"Read-only deterministic safety check: {name}.",
            parameters = new { type = "object", properties = new { assignmentId = new { type = "string" }, planVersion = new { type = "integer" } }, required = new[] { "assignmentId", "planVersion" } }
        }).ToArray();
    }
}
