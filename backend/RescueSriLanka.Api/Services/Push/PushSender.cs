namespace RescueSriLanka.Api.Services.Push;

/// <summary>What a phone shows: a title and one line of plain text.</summary>
public sealed record PushMessage(string Title, string Body);

public enum PushOutcome
{
    /// <summary>Firebase accepted the message for delivery.</summary>
    Sent,

    /// <summary>The device no longer exists or its token has expired. Its row should go.</summary>
    TokenInvalid,

    /// <summary>Something else went wrong. Logged, and the message is dropped.</summary>
    Failed,

    /// <summary>Firebase is not configured, so nothing was sent.</summary>
    Skipped
}

/// <summary>Delivers one message to one device.</summary>
public interface IPushSender
{
    /// <summary>Shown in logs and at start-up so the active transport is never a guess.</summary>
    string Name { get; }

    Task<PushOutcome> SendAsync(string token, PushMessage message, CancellationToken ct = default);
}
