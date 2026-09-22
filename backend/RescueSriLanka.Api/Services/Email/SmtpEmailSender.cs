using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace RescueSriLanka.Api.Services.Email;

/// <summary>
/// Real delivery over SMTP, via MailKit. Works with anything that speaks SMTP —
/// Gmail with an app password, Brevo, SendGrid, Mailtrap.
///
/// A connection per message is not the fastest thing possible, but notification
/// volume here is a handful of emails per incident and a pooled connection that
/// silently goes stale is a far more annoying bug than a extra TCP handshake.
/// </summary>
public class SmtpEmailSender(
    IOptions<EmailOptions> options,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public string Name => $"SMTP {_options.Smtp.Host}:{_options.Smtp.Port}";

    public async Task<bool> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var smtp = _options.Smtp;

        if (string.IsNullOrWhiteSpace(smtp.Host))
        {
            // Registration only picks this sender when a host is set, so this is
            // a configuration change mid-flight rather than the normal path.
            logger.LogError("SMTP host is not configured; cannot send to {Recipient}.",
                message.ToAddress);
            return false;
        }

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        mime.To.Add(new MailboxAddress(message.ToName ?? message.ToAddress, message.ToAddress));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder
        {
            HtmlBody = message.HtmlBody,
            TextBody = message.TextBody
        }.ToMessageBody();

        using var client = new SmtpClient
        {
            CheckCertificateRevocation = smtp.ShouldCheckRevocation
        };

        try
        {
            await client.ConnectAsync(
                smtp.Host,
                smtp.Port,
                smtp.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto,
                ct);

            // An open relay (a local catcher, say) needs no credentials.
            if (!string.IsNullOrWhiteSpace(smtp.Username))
            {
                await client.AuthenticateAsync(smtp.Username, smtp.Password ?? string.Empty, ct);
            }

            await client.SendAsync(mime, ct);
            await client.DisconnectAsync(true, ct);

            logger.LogInformation(
                "Email sent to {Recipient}: {Subject}", message.ToAddress, message.Subject);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Report it and move on. The incident, the approval and the citizen's
            // report have all already succeeded; only the email failed.
            logger.LogError(
                ex, "Email to {Recipient} failed: {Subject}", message.ToAddress, message.Subject);
            return false;
        }
    }
}
