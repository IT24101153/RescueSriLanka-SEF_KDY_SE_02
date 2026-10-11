using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Services.Llm;
using RescueSriLanka.Api.Features.ComponentA.Services;

namespace RescueSriLanka.Api.Features.ComponentA.Agents.IncidentAnalysisAgent;
public interface IIncidentAnalysisAgent
{
    /// <summary>
    /// Analyses an incident and persists the run. Throws
    /// <see cref="LlmUnavailableException"/> when the model cannot answer.
    /// </summary>
    Task<IncidentAnalysisResult> AnalyseAsync(Guid incidentId, CancellationToken ct = default);
}

/// <summary>
/// Component A's coordinator. Receives the objective (analyse this incident),
/// has the Planner build a structured plan, delegates each step to the agent
/// named in it, and persists the plan, every step's outcome and the final
/// result on the run.
///
/// Contract with the Coordinator/Planner Agent (Student B): call
/// <see cref="AnalyseAsync"/> with an incident id; receive a validated
/// <see cref="IncidentAnalysisResult"/>. The agent proposes only — it never
/// writes the incident's severity in force. A coordinator approves that.
///
/// The severity comes from the language model or not at all — no rule engine
/// stands in for it. A model that is unconfigured, out of quota or unusable
/// fails the run like any other error: the run is marked
/// <see cref="AgentRunStatus.Failed"/> with the reason and the exception is
/// rethrown, so a run is never left "Running" and nothing is applied to the
/// incident. The coordinator sees that reason and grades the report by hand.
/// </summary>
public class IncidentAnalysisAgent(
    AppDbContext db,
    ILlmClient llm,
    IncidentAnalysisTools tools,
    IImageStorageService imageStorage,
    ILogger<IncidentAnalysisAgent> logger) : IIncidentAnalysisAgent
{
    public const string AgentName = "IncidentAnalysisAgent";

    private readonly AnalysisPlannerAgent planner = new();
    private readonly EvidenceGatheringAgent evidenceAgent = new(tools, imageStorage);
    private readonly SeverityAnalysisAgent severityAgent = new(llm);
    private readonly ProposalValidationAgent validator = new();

    /// <summary>One plan step plus how it went; serialised to the run's PlanJson.</summary>
    private sealed class StepTrace(PlannedStep step)
    {
        public int Step { get; } = step.Number;
        public string Agent { get; } = step.Agent;
        public string Action { get; } = step.Action;
        public IReadOnlyList<string> Tools { get; } = step.Tools;
        public string Status { get; set; } = "Pending";
        public int DurationMs { get; set; }
        public string? Detail { get; set; }
    }

    public async Task<IncidentAnalysisResult> AnalyseAsync(
        Guid incidentId, CancellationToken ct = default)
    {
        var incident = await db.Incidents.FirstOrDefaultAsync(e => e.Id == incidentId, ct)
            ?? throw new InvalidOperationException($"Incident {incidentId} not found.");

        var input = new IncidentAnalysisInput
        {
            IncidentId = incident.Id,
            Title = incident.Title,
            Description = incident.Description,
            Type = incident.Type,
            Latitude = incident.Latitude,
            Longitude = incident.Longitude,
            District = incident.District,
            EstimatedAffectedPeople = incident.EstimatedAffectedPeople,
            ImageCount = await db.IncidentImages.CountAsync(i => i.IncidentId == incident.Id, ct)
        };

        var run = new AgentRun
        {
            AgentName = AgentName,
            Objective = $"Classify severity and zone status for incident {incident.Id}",
            IncidentId = incident.Id,
            InputJson = JsonSerializer.Serialize(input, AnalysisJson.Options)
        };
        db.AgentRuns.Add(run);
        await db.SaveChangesAsync(ct);

        var stopwatch = Stopwatch.StartNew();
        List<StepTrace> traces = [];
        List<string> notes = [];

        try
        {
            // ---- Plan: the Planner turns the objective into ordered steps ----
            var plan = planner.Plan(input);
            traces = [.. plan.Steps.Select(step => new StepTrace(step))];
            notes = [.. plan.Notes];
            await SavePlanAsync(run, traces, notes, ct);

            // ---- Step 1: Evidence agent — the only role that calls tools ----
            var evidence = await RunStepAsync(run, traces, notes, 0, ct, async () =>
            {
                var bundle = await evidenceAgent.GatherAsync(input, plan, ct);
                return (bundle,
                    $"{bundle.NearbyIncidents} nearby, rainfall "
                    + (bundle.RainfallMm is null ? "unavailable" : $"{bundle.RainfallMm:F0} mm")
                    + $", {bundle.Images.Count} photo(s)");
            });
            run.ToolCallsJson = JsonSerializer.Serialize(evidence.ToolCalls);

            // ---- Step 2: Severity agent — the model, with nothing standing in for it ----
            var proposal = await RunStepAsync(run, traces, notes, 1, ct, async () =>
            {
                var result = await severityAgent.ProposeAsync(input, evidence, ct);
                var attempts = result.Attempts > 1 ? $", {result.Attempts} attempts" : string.Empty;
                return (result, $"{result.Model}: {result.Result.Severity}{attempts}");
            });

            // ---- Step 3: Validation agent — deterministic, has the last word ----
            var accepted = await RunStepAsync(run, traces, notes, 2, ct, () =>
            {
                var outcome = validator.Validate(proposal.Result);
                if (!outcome.Accepted)
                {
                    // An answer nobody can explain is no answer: the run fails with the reason.
                    throw new LlmUnavailableException(
                        $"The AI model's answer was rejected: {outcome.Rejection} Try again.");
                }

                return Task.FromResult((proposal with { Result = outcome.Result! },
                    outcome.Adjustments.Count == 0 ? "accepted" : "accepted: " + string.Join("; ", outcome.Adjustments)));
            });

            var result = accepted.Result with
            {
                ToolResults = evidence.ToolCalls.ToDictionary(kv => kv.Key, kv => kv.Value!)
            };

            // ---- Persist the proposal (never the severity in force) ----
            stopwatch.Stop();
            run.Status = AgentRunStatus.Succeeded;
            run.Model = accepted.Model;
            run.ModelAttempts = accepted.Attempts;
            run.OutputJson = JsonSerializer.Serialize(result, AnalysisJson.Options);
            run.DurationMs = (int)stopwatch.ElapsedMilliseconds;
            run.CompletedAt = DateTime.UtcNow;

            incident.AiSeverity = result.Severity;
            incident.AiSeverityScore = result.SeverityScore;
            incident.AiConfidence = result.Confidence;
            incident.AiRationale = result.Rationale;
            incident.AiAnalysedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "Incident {Id} analysed: {Severity} ({Score}/100)",
                incident.Id, result.Severity, result.SeverityScore);

            return result;
        }
        catch (Exception ex)
        {
            // A safe, clearly recorded failure: the run says why, and the
            // incident keeps whatever it had.
            stopwatch.Stop();
            logger.LogError(ex, "Incident analysis failed for incident {Id}.", incident.Id);

            db.Entry(incident).State = EntityState.Unchanged;
            run.Status = AgentRunStatus.Failed;
            run.ErrorMessage = Truncate(ex is OperationCanceledException ? "Analysis was cancelled." : ex.Message, 1000);
            run.DurationMs = (int)stopwatch.ElapsedMilliseconds;
            run.CompletedAt = DateTime.UtcNow;
            run.PlanJson = SerializePlan(traces, notes);

            try
            {
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception saveError)
            {
                logger.LogError(saveError, "Could not record the failure of run {RunId}.", run.Id);
            }

            throw;
        }
    }

    private async Task<T> RunStepAsync<T>(
        AgentRun run, List<StepTrace> traces, List<string> notes, int index, CancellationToken ct,
        Func<Task<(T Value, string Detail)>> work)
    {
        var trace = traces[index];
        trace.Status = "Running";
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var (value, detail) = await work();
            trace.Status = "Completed";
            trace.Detail = detail;
            trace.DurationMs = (int)stopwatch.ElapsedMilliseconds;

            // Each finished step is durable before the next one starts.
            await SavePlanAsync(run, traces, notes, ct);
            return value;
        }
        catch (Exception ex)
        {
            // The coordinator's handler records the failed step on the run.
            trace.Status = "Failed";
            trace.Detail = Truncate(ex.Message, 300);
            trace.DurationMs = (int)stopwatch.ElapsedMilliseconds;
            throw;
        }
    }

    private async Task SavePlanAsync(
        AgentRun run, List<StepTrace> traces, List<string> notes, CancellationToken ct)
    {
        run.PlanJson = SerializePlan(traces, notes);
        await db.SaveChangesAsync(ct);
    }

    private static readonly JsonSerializerOptions TraceJson = new(JsonSerializerDefaults.Web);

    private static string SerializePlan(List<StepTrace> traces, List<string> notes) =>
        JsonSerializer.Serialize(new { steps = traces, notes }, TraceJson);

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
