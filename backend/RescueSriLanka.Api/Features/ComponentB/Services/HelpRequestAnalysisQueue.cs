using System.Threading.Channels;
using RescueSriLanka.Api.Features.ComponentB.Agents.PlannerAgent;
using RescueSriLanka.Api.Features.ComponentB.DTOs;
using RescueSriLanka.Api.Features.ComponentB.Models;

namespace RescueSriLanka.Api.Features.ComponentB.Services;

public interface IHelpRequestAnalysisQueue
{
    /// <summary>
    /// Asks for a help request to be triaged by the Planner Agent soon.
    /// Returns immediately — the citizen who filed the request must never
    /// wait on a language model.
    /// </summary>
    void Enqueue(Guid helpRequestId);
}

/// <summary>
/// Hands new help requests to the Planner Agent in the background, the same
/// way Component A auto-queues every incident. Without this, a request sits
/// unassessed until a manager opens it and presses "Run AI review" — which
/// makes the agent a button rather than something that's already run by the
/// time the manager looks. The manager's decision (verify/reject) is still
/// the only thing that acts on the request; this only produces the proposal.
/// </summary>
public class HelpRequestAnalysisQueue(ILogger<HelpRequestAnalysisQueue> logger)
    : IHelpRequestAnalysisQueue
{
    // Bounded so a flood of submissions cannot grow the queue without limit;
    // the oldest waiting item is dropped rather than blocking the request thread.
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(
        new BoundedChannelOptions(200)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

    public ChannelReader<Guid> Reader => _channel.Reader;

    public void Enqueue(Guid helpRequestId)
    {
        if (!_channel.Writer.TryWrite(helpRequestId))
        {
            logger.LogWarning(
                "Help request analysis queue rejected {HelpRequestId}; it can still be analysed by hand.",
                helpRequestId);
        }
    }
}

/// <summary>Drains the queue one help request at a time.</summary>
public class HelpRequestAnalysisWorker(
    HelpRequestAnalysisQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<HelpRequestAnalysisWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var helpRequestId in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                // The planner is scoped (it owns a DbContext), so each run
                // needs its own scope — the background service is a singleton.
                using var scope = scopeFactory.CreateScope();
                var planner = scope.ServiceProvider.GetRequiredService<IPlannerAgentService>();

                await planner.TriggerAsync(new TriggerWorkflowDto
                {
                    ObjectiveType = PlannerWorkflowObjectiveType.HelpRequest,
                    ObjectiveId = helpRequestId
                });

                logger.LogInformation("Auto-triaged help request {HelpRequestId}.", helpRequestId);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A failed triage must never take the worker down — the
                // request simply stays unassessed until someone runs it by hand.
                logger.LogError(ex, "Background triage failed for help request {HelpRequestId}", helpRequestId);
            }
        }
    }
}
