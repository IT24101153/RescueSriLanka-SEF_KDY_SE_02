using Microsoft.Extensions.Options;

namespace RescueSriLanka.Api.Services.Email;

/// <summary>
/// Wraps the real sender and rewrites every recipient to a testmail.app address
/// inside the configured namespace.
///
/// The point is that the rest of the system stays honest: the notification
/// service still looks up the real citizens in a district and still addresses
/// them by name, and only the last step before the wire swaps the envelope. A
/// demo therefore exercises the true recipient query, with no chance of a test
/// run emailing a member of the public.
///
/// The original address survives in the tag, so the inbox shows who each
/// message was *for*, and a test can query that exact tag.
/// </summary>
public class TestmailRedirectingEmailSender(
    IEmailSender inner,
    IOptions<EmailOptions> options,
    ILogger<TestmailRedirectingEmailSender> logger) : IEmailSender
{
    private readonly TestmailOptions _testmail = options.Value.Testmail;

    public string Name => $"{inner.Name} → testmail.app/{_testmail.Namespace}";

    public Task<bool> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (message.BypassTestRedirect)
        {
            logger.LogWarning(
                "Sending to the real address {Recipient} — test redirect bypassed.",
                message.ToAddress);

            return inner.SendAsync(message, ct);
        }

        var tag = TestmailOptions.TagFor(message.ToAddress);
        var redirected = _testmail.AddressFor(tag);

        logger.LogInformation(
            "Redirecting mail for {Original} to {Redirected}", message.ToAddress, redirected);

        return inner.SendAsync(
            message with { ToAddress = redirected, ToName = message.ToName },
            ct);
    }
}
