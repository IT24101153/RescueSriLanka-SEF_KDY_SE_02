using System.Diagnostics;
using System.Text.Json;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentA.Agents.IncidentAnalysisAgent;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Features.ComponentA.Agents.Shared;

/// <summary>
/// Records a multi-agent workflow on its <see cref="AgentRun"/>: the plan, how
/// each step went, and a safe failure. The Enrichment and Zone Planning agents
/// share it, so every Component A run reads the same way in the monitoring view
/// as the Incident Analysis Agent's.
/// </summary>
public sealed class AgentRunTracer(AppDbContext db, AgentRun run)
{
    private static readonly JsonSerializerOptions TraceJson = new(JsonSerializerDefaults.Web);

    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private List<StepTrace> _steps = [];
    private List<string> _notes = [];

    public AgentRun Run => run;

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

    /// <summary>Adds the run and persists it, so a crash still leaves a record.</summary>
    public async Task StartAsync(CancellationToken ct)
    {
        db.AgentRuns.Add(run);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetPlanAsync(
        IEnumerable<PlannedStep> steps, IEnumerable<string> notes, CancellationToken ct)
    {
        _steps = [.. steps.Select(step => new StepTrace(step))];
        _notes = [.. notes];
        await SavePlanAsync(ct);
    }

    /// <summary>Runs one plan step; each finished step is durable before the next starts.</summary>
    public async Task<T> StepAsync<T>(
        int index, Func<Task<(T Value, string Detail)>> work, CancellationToken ct)
    {
        var trace = _steps[index];
        trace.Status = "Running";
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var (value, detail) = await work();
            trace.Status = "Completed";
            trace.Detail = Truncate(detail, 1000);
            trace.DurationMs = (int)stopwatch.ElapsedMilliseconds;
            await SavePlanAsync(ct);
            return value;
        }
        catch (Exception ex)
        {
            trace.Status = "Failed";
            trace.Detail = Truncate(ex.Message, 300);
            trace.DurationMs = (int)stopwatch.ElapsedMilliseconds;
            throw;
        }
    }

    /// <summary>Completes the run with its validated output.</summary>
    public Task CompleteAsync(
        AgentRunStatus status, string model, int attempts, string? error,
        string outputJson, bool usedFallback, CancellationToken ct)
    {
        _stopwatch.Stop();
        run.Status = status;
        run.Model = model;
        run.ModelAttempts = attempts;
        run.ErrorMessage = error is null ? null : Truncate(error, 1000);
        run.OutputJson = outputJson;
        run.UsedFallback = usedFallback;
        run.DurationMs = (int)_stopwatch.ElapsedMilliseconds;
        run.CompletedAt = DateTime.UtcNow;
        run.PlanJson = SerializePlan();
        return db.SaveChangesAsync(ct);
    }

    /// <summary>A safe, clearly recorded failure. Never throws itself.</summary>
    public async Task FailAsync(Exception ex, ILogger logger)
    {
        _stopwatch.Stop();
        run.Status = AgentRunStatus.Failed;
        run.ErrorMessage = Truncate(
            ex is OperationCanceledException ? "The run was cancelled." : ex.Message, 1000);
        run.DurationMs = (int)_stopwatch.ElapsedMilliseconds;
        run.CompletedAt = DateTime.UtcNow;
        run.PlanJson = SerializePlan();

        try
        {
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception saveError)
        {
            logger.LogError(saveError, "Could not record the failure of run {RunId}.", run.Id);
        }
    }

    private async Task SavePlanAsync(CancellationToken ct)
    {
        run.PlanJson = SerializePlan();
        await db.SaveChangesAsync(ct);
    }

    private string SerializePlan() =>
        JsonSerializer.Serialize(new { steps = _steps, notes = _notes }, TraceJson);

    public static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
