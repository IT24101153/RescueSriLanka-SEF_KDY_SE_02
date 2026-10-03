using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RescueSriLanka.Api.Data;

namespace RescueSriLanka.Api.Services.Push;

/// <summary>
/// Decides which phones get a push. Callers say who the message is about; this
/// checks each person's own opt-in and queues one delivery per registered device.
/// </summary>
public interface IPushNotificationService
{
    /// <summary>False when push is switched off in configuration.</summary>
    bool Enabled { get; }

    /// <summary>
    /// Queues the message for every device of each listed user who has push
    /// turned on. Returns how many deliveries were queued. Never waits on Firebase.
    /// </summary>
    Task<int> SendToUsersAsync(IEnumerable<Guid> userIds, PushMessage message, CancellationToken ct = default);
}

public sealed class PushNotificationService(
    AppDbContext db,
    IPushQueue queue,
    IOptions<PushOptions> options) : IPushNotificationService
{
    public bool Enabled => options.Value.Enabled;

    public async Task<int> SendToUsersAsync(
        IEnumerable<Guid> userIds, PushMessage message, CancellationToken ct = default)
    {
        if (!Enabled) return 0;

        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return 0;

        var tokens = await db.DeviceTokens.AsNoTracking()
            .Where(device =>
                ids.Contains(device.UserId) &&
                device.User != null &&
                device.User.IsActive &&
                device.User.PushNotificationsEnabled)
            .Select(device => device.Token)
            .ToListAsync(ct);

        foreach (var token in tokens)
        {
            queue.TryQueue(new PushDelivery(token, message));
        }

        return tokens.Count;
    }
}
