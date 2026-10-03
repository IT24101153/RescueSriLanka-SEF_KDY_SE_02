namespace RescueSriLanka.Api.Services.Push;

/// <summary>
/// Stands in for Firebase until a service account is configured. It writes the
/// message to the log, which keeps the feature runnable without a Firebase project.
/// </summary>
public sealed class LoggingPushSender(ILogger<LoggingPushSender> logger) : IPushSender
{
    public string Name => "log only";

    public Task<PushOutcome> SendAsync(string token, PushMessage message, CancellationToken ct = default)
    {
        logger.LogInformation(
            "Push not sent (Firebase is not configured): {Title} — {Body}", message.Title, message.Body);
        return Task.FromResult(PushOutcome.Skipped);
    }
}
