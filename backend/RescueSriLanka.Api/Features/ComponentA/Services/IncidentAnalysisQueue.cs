using System.Threading.Channels;
using RescueSriLanka.Api.Features.ComponentA.Agents.IncidentAnalysisAgent;

namespace RescueSriLanka.Api.Features.ComponentA.Services;
public interface IIncidentAnalysisQueue
{
    /// <summary>
    /// Asks for an incident to be analysed soon. Returns immediately — the
    /// citizen who filed the report must never wait on a language model.
    /// </summary>
    void Enqueue(Guid incidentId);
}

/// <summary>
/// Hands new incidents to the Incident Analysis Agent in the background.
///
/// Without this, a report sits unscored until a coordinator opens it and
/// presses "Run analysis" — which makes the agent a button rather than an
/// agent. With it, the coordinator's queue is a list of proposals already
/// waiting for a decision, and the human approval gate is still the only way
/// anything reaches the incident.
/// </summary>
public class IncidentAnalysisQueue(ILogger<IncidentAnalysisQueue> logger)
    : IIncidentAnalysisQueue
{
    // Bounded so a flood of reports cannot grow the queue without limit; the
    // oldest waiting item is dropped rather than blocking the request thread.
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(
        new BoundedChannelOptions(200)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

    public ChannelReader<Guid> Reader => _channel.Reader;

    public void Enqueue(Guid incidentId)
    {
        if (!_channel.Writer.TryWrite(incidentId))
        {
            logger.LogWarning(
                "Analysis queue rejected incident {IncidentId}; it can still be analysed by hand.",
                incidentId);
        }
    }
}

/// <summary>Drains the queue one incident at a time.</summary>
public class IncidentAnalysisWorker(
    IncidentAnalysisQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<IncidentAnalysisWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var incidentId in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                // The agent is scoped (it owns a DbContext), so each run needs
                // its own scope — the background service itself is a singleton.
                using var scope = scopeFactory.CreateScope();
                var agent = scope.ServiceProvider.GetRequiredService<IIncidentAnalysisAgent>();

                var result = await agent.AnalyseAsync(incidentId, stoppingToken);

                logger.LogInformation(
                    "Auto-analysed incident {IncidentId}: proposed {Severity} ({Score}/100){Fallback}",
                    incidentId, result.Severity, result.SeverityScore,
                    result.UsedFallback ? " via rule engine" : string.Empty);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A failed analysis must never take the worker down — the
                // incident simply stays unanalysed until someone runs it.
                logger.LogError(ex, "Background analysis failed for incident {IncidentId}", incidentId);
            }
        }
    }
}
