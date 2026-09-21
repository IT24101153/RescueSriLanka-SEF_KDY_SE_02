using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.DTOs;
using RescueSriLanka.Api.Models.Agents;

namespace RescueSriLanka.Api.Agents.SafetyValidation;

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
        var stepNumber = 0;

        for (var iteration = 1; iteration <= MaxAgentSteps && modelDecision is null && providerFailure is null; iteration++)
        {
            GeminiSafetyAgentResponse response;
            try
            {
                response = await _gemini.GetNextResponseAsync(context, priorResults, cancellationToken);
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

            if (response.ToolCalls.Count == 0)
            {
                providerFailure = "Gemini returned neither a valid decision nor a tool call.";
                break;
            }

            foreach (var call in response.ToolCalls)
            {
                if (++stepNumber > MaxAgentSteps)
                {
                    providerFailure = $"Safety validation exceeded the {MaxAgentSteps}-step limit.";
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
                priorResults.Add(new { name = call.Name, result = check });
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

    public GeminiSafetyValidationClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _model = configuration["Gemini:Model"] ?? "gemini-3-flash-preview";
        _apiKey = configuration["Gemini:ApiKey"] ?? throw new InvalidOperationException("Gemini:ApiKey is not configured.");
    }

    public async Task<GeminiSafetyAgentResponse> GetNextResponseAsync(
        AssignmentValidationContextDto context, IReadOnlyList<object> priorToolResults, CancellationToken cancellationToken)
    {
        const string instruction = "You are a safety-validation agent. Use only supplied read-only tools. Operational facts come only from tool results; never assume missing facts. You cannot dispatch or mutate anything. A human EmergencyCoordinator must approve any recommendation. Return APPROVE, REVISE, or REJECT only after checks.";
        var payload = new
        {
            model = _model,
            input = priorToolResults.Count == 0
                ? (object)$"Validate assignment {context.AssignmentId} at plan version {context.PlanVersion}."
                : priorToolResults,
            tools = new SafetyValidationToolsSchemas().Schemas,
            system_instruction = instruction
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "interactions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-goog-api-key", _apiKey);
        request.Headers.Add("Api-Revision", "2026-05-20");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private static GeminiSafetyAgentResponse Parse(string raw)
    {
        using var document = JsonDocument.Parse(raw);
        var calls = new List<GeminiSafetyToolCall>();
        string? text = null;
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
                    text = content.EnumerateArray().FirstOrDefault(c => c.TryGetProperty("type", out var t) && t.GetString() == "text")
                        .GetProperty("text").GetString();
                }
            }
        }
        if (calls.Count > 0) return new(calls, null, null, null);
        if (string.IsNullOrWhiteSpace(text)) return new([], null, null, null);
        try
        {
            var parsed = JsonSerializer.Deserialize<GeminiFinalDecision>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return parsed is not null && Enum.TryParse<SafetyValidationDecision>(parsed.Decision, true, out var decision)
                ? new([], decision, parsed.Summary, parsed.SuggestedActions)
                : new([], null, null, null);
        }
        catch (JsonException) { return new([], null, null, null); }
    }

    private sealed class GeminiFinalDecision
    {
        public string? Decision { get; set; }
        public string? Summary { get; set; }
        public List<string>? SuggestedActions { get; set; }
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
