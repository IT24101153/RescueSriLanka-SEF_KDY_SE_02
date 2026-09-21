using System.Runtime.InteropServices;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Services.Email;

/// <summary>
/// Everything about outbound mail, bound from the "Email" configuration
/// section. Nothing here has a secret for a default: with no SMTP host the API
/// still runs and still "sends", it just writes the message to the log.
/// </summary>
public class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>Master switch. False silences every notification.</summary>
    public bool Enabled { get; set; } = true;

    public string FromAddress { get; set; } = "alerts@rescuesrilanka.lk";

    public string FromName { get; set; } = "RescueSriLanka Alerts";

    /// <summary>
    /// A district warning goes out only at this severity or above. High by
    /// default: a Low or Moderate incident is worth a map pin, not an email to
    /// every citizen in the district, and an alert people learn to ignore is
    /// worse than no alert.
    /// </summary>
    public IncidentSeverity MinimumWarningSeverity { get; set; } = IncidentSeverity.High;

    public SmtpOptions Smtp { get; set; } = new();

    public TestmailOptions Testmail { get; set; } = new();
}

public class SmtpOptions
{
    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    /// <summary>STARTTLS on 587 is what Gmail, Brevo and SendGrid all expect.</summary>
    public bool UseStartTls { get; set; } = true;

    public string? Username { get; set; }

    /// <summary>Never a real password in a committed file — see the dev settings.</summary>
    public string? Password { get; set; }

    /// <summary>
    /// Whether the TLS handshake must complete a certificate revocation check.
    /// Leave unset to decide by platform.
    /// </summary>
    public bool? CheckCertificateRevocation { get; set; }

    /// <summary>
    /// On by default, except on macOS, which cannot complete an OCSP revocation
    /// check — MailKit asks for one, the lookup never finishes, and every
    /// handshake dies with "an incomplete certificate revocation check
    /// occurred". The certificate is valid; only the *revocation lookup* fails.
    ///
    /// Everything else about the certificate is still verified either way:
    /// signature, expiry, hostname and chain to a trusted root. What is given up
    /// is noticing that a valid certificate has been revoked since it was
    /// issued — which is why this is narrowed to the platform that forces it,
    /// and left overridable.
    /// </summary>
    public bool ShouldCheckRevocation =>
        CheckCertificateRevocation
        ?? !RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);
}

/// <summary>
/// testmail.app receives mail; it cannot send it. So it is not the transport —
/// it is the inbox we point the transport at while developing, plus an API for
/// asserting in tests that a message really arrived.
///
/// Every address in the namespace works with no set-up, so each recipient gets
/// their own tag: priya@gmail.com is delivered to
/// {namespace}.priyagmailcom@inbox.testmail.app, which keeps one citizen's
/// warnings separable from another's when you read the inbox.
/// </summary>
public class TestmailOptions
{
    /// <summary>The 8-character namespace from the testmail.app dashboard.</summary>
    public string? Namespace { get; set; }

    /// <summary>Only needed to query the inbox from tests, never to send.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Divert every recipient into the namespace. On in development so a demo
    /// run can never email a real person by accident.
    /// </summary>
    public bool RedirectAllMail { get; set; } = true;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Namespace);

    public string AddressFor(string tag) => $"{Namespace}.{tag}@inbox.testmail.app";

    /// <summary>
    /// The tag a given recipient's mail is filed under. Letters and digits only,
    /// because anything else risks an address testmail will not route, and
    /// stable, because a test has to be able to ask for the same tag it sent to.
    /// </summary>
    public static string TagFor(string emailAddress)
    {
        var tag = new string(emailAddress
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .Take(32)
            .ToArray());

        return tag.Length == 0 ? "unknown" : tag;
    }
}
