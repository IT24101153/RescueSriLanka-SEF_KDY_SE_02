using System.Threading.Channels;

namespace RescueSriLanka.Api.Features.ComponentA.Services.Notifications;
public enum NotificationKind
{
    /// <summary>Receipt to the citizen who filed the report.</summary>
    ReportReceived,

    /// <summary>Risk warning to everyone subscribed to the incident's district.</summary>
    DistrictWarning
}

public readonly record struct NotificationJob(NotificationKind Kind, Guid IncidentId);

public interface INotificationQueue
{
    /// <summary>
    /// Asks for an email to go out soon. Returns immediately: a citizen filing a
    /// report, and a coordinator approving one, must never wait on a mail
    /// server — nor fail because one is down.
    /// </summary>
    void Enqueue(NotificationJob job);
}

/// <summary>
/// Buffers notification work between the request that caused it and the worker
/// that sends it. Mirrors <see cref="IncidentAnalysisQueue"/> deliberately —
/// same shape, same reasoning, one pattern to learn.
/// </summary>
public class NotificationQueue(ILogger<NotificationQueue> logger) : INotificationQueue
{
    // Bounded, because a burst of reports must not grow the queue without
    // limit. Dropping the oldest is the right loss here: the newest warning is
    // always the more urgent one.
    private readonly Channel<NotificationJob> _channel = Channel.CreateBounded<NotificationJob>(
        new BoundedChannelOptions(500)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

    public ChannelReader<NotificationJob> Reader => _channel.Reader;

    public void Enqueue(NotificationJob job)
    {
        if (!_channel.Writer.TryWrite(job))
        {
            logger.LogWarning(
                "Notification queue rejected {Kind} for incident {IncidentId}.",
                job.Kind, job.IncidentId);
        }
    }
}

/// <summary>Drains the queue one message at a time.</summary>
public class NotificationWorker(
    NotificationQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<NotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await DrainAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Shutdown. ReadAllAsync throws this from the enumerator itself, so
            // it lands outside the per-job handler below — and a BackgroundService
            // that lets an exception escape takes the whole host down with it.
        }
    }

    private async Task DrainAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                // The notification service owns a DbContext, so it is scoped;
                // this worker is a singleton and needs its own scope per job.
                using var scope = scopeFactory.CreateScope();
                var notifications = scope.ServiceProvider
                    .GetRequiredService<INotificationService>();

                _ = job.Kind switch
                {
                    NotificationKind.ReportReceived =>
                        await notifications.SendReportReceivedAsync(job.IncidentId, stoppingToken),
                    _ =>
                        await notifications.SendDistrictWarningAsync(job.IncidentId, stoppingToken)
                };
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A failed email must never take the worker down, or one bad
                // address would silence every warning that follows it.
                logger.LogError(
                    ex, "Notification {Kind} failed for incident {IncidentId}",
                    job.Kind, job.IncidentId);
            }
        }
    }
}
