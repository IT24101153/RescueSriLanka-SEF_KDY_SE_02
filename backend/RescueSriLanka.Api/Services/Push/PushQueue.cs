using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RescueSriLanka.Api.Data;

namespace RescueSriLanka.Api.Services.Push;

/// <summary>One message waiting to leave: the device it is for, and what it says.</summary>
public sealed record PushDelivery(string Token, PushMessage Message);

public interface IPushQueue
{
    bool TryQueue(PushDelivery delivery);
}

/// <summary>
/// Sends pushes on a background worker, so no request waits on Firebase. When
/// Firebase reports a device as gone, its token is removed here.
/// </summary>
public sealed class PushQueue(
    IServiceScopeFactory scopeFactory,
    ILogger<PushQueue> logger) : BackgroundService, IPushQueue
{
    // Bounded, so a burst of district warnings cannot grow memory without limit. The oldest
    // is dropped first: the newest warning is the one that matters most.
    private readonly Channel<PushDelivery> _deliveries = Channel.CreateBounded<PushDelivery>(
        new BoundedChannelOptions(5000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

    public bool TryQueue(PushDelivery delivery) => _deliveries.Writer.TryWrite(delivery);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var delivery in _deliveries.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await DeliverAsync(delivery, stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError(exception, "A queued push failed unexpectedly.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task DeliverAsync(PushDelivery delivery, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();

        var sender = scope.ServiceProvider.GetRequiredService<IPushSender>();
        var outcome = await sender.SendAsync(delivery.Token, delivery.Message, ct);

        if (outcome != PushOutcome.TokenInvalid) return;

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stale = await db.DeviceTokens.FirstOrDefaultAsync(device => device.Token == delivery.Token, ct);
        if (stale is null) return;

        db.DeviceTokens.Remove(stale);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Removed a device token that Firebase no longer accepts.");
    }
}
