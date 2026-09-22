namespace RescueSriLanka.Api.Services.Email;

/// <summary>One message to one person, in both the formats a mail client may want.</summary>
public record EmailMessage
{
    public required string ToAddress { get; init; }

    public string? ToName { get; init; }

    public required string Subject { get; init; }

    public required string HtmlBody { get; init; }

    /// <summary>Plain-text alternative. Not optional — a warning has to survive
    /// a text-only client, and a message without one scores as spam.</summary>
    public required string TextBody { get; init; }

    /// <summary>
    /// Deliver to <see cref="ToAddress"/> as written, ignoring the test-inbox
    /// redirect.
    ///
    /// Only ever set for an explicitly addressed test: the one case where the
    /// whole point is to prove a real mailbox receives real mail, which the
    /// redirect would otherwise make impossible to check. Never set on a
    /// notification produced by the system itself.
    /// </summary>
    public bool BypassTestRedirect { get; init; }
}

/// <summary>
/// Delivers a message, or says it could not. Implementations never throw for an
/// ordinary delivery failure: a citizen's report must not fail because a mail
/// server is down.
/// </summary>
public interface IEmailSender
{
    /// <summary>Human-readable transport description, logged at start-up.</summary>
    string Name { get; }

    Task<bool> SendAsync(EmailMessage message, CancellationToken ct = default);
}
