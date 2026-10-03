using RescueSriLanka.Api.Services.Push;

namespace RescueSriLanka.Api.Tests;

/// <summary>Records which users were asked to be pushed, instead of queueing real deliveries.</summary>
public sealed class RecordingPushNotificationService : IPushNotificationService
{
    public bool Enabled { get; set; } = true;

    public List<(List<Guid> UserIds, PushMessage Message)> Calls { get; } = [];

    public Task<int> SendToUsersAsync(IEnumerable<Guid> userIds, PushMessage message, CancellationToken ct = default)
    {
        var ids = userIds.ToList();
        Calls.Add((ids, message));
        return Task.FromResult(ids.Count);
    }
}
