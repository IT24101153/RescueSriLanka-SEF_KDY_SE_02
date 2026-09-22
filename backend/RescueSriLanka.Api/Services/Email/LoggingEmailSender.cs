namespace RescueSriLanka.Api.Services.Email;

/// <summary>
/// The sender used when no SMTP host is configured: writes the whole message to
/// the log instead of delivering it.
///
/// This is what makes the notification feature runnable by anyone who clones the
/// repository. Every trigger, recipient query and template runs for real; only
/// the last hop is a log line. Drop SMTP credentials into the configuration and
/// the same messages start arriving in an inbox, with nothing else changed.
/// </summary>
public class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public string Name => "console log (no SMTP host configured)";

    public Task<bool> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        logger.LogInformation(
            """
            ---------------- EMAIL (not sent — no SMTP configured) ----------------
            To      : {Recipient}
            Subject : {Subject}

            {Body}
            -----------------------------------------------------------------------
            """,
            message.ToAddress,
            message.Subject,
            message.TextBody);

        return Task.FromResult(true);
    }
}
