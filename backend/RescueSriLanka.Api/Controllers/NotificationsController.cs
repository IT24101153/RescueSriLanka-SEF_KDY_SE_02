using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MimeKit;
using Microsoft.Extensions.Options;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Services;
using RescueSriLanka.Api.Services.Email;

namespace RescueSriLanka.Api.Controllers;

/// <summary>
/// Proving the notification path works, without waiting for a real disaster.
///
/// Both endpoints are coordinator-only. Sending mail on demand and reading a
/// shared test inbox are fine tools for the person running the system and a
/// poor thing to hand to the public.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = nameof(UserRole.EmergencyCoordinator))]
public class NotificationsController(
    IAuthService authService,
    IEmailSender sender,
    ITestmailClient testmail,
    IOptions<EmailOptions> options) : ControllerBase
{
    /// <summary>
    /// Sends a sample district warning, so the transport, the template and the
    /// redirect can all be checked in one call.
    ///
    /// With no <paramref name="to"/> it goes to the signed-in coordinator and
    /// follows the redirect like any other message. Naming an address instead
    /// sends there **directly**, bypassing the redirect — the only way to prove
    /// a real mailbox receives real mail. Coordinator-only, one fixed template,
    /// no caller-supplied content, so it cannot be turned into a way of mailing
    /// arbitrary text to strangers.
    /// </summary>
    [HttpPost("test")]
    public async Task<IActionResult> SendTest([FromQuery] string? to, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Unauthorized();
        }

        var user = await authService.FindByIdAsync(userId, ct);
        if (user is null) return Unauthorized();

        if (to is not null && !MailboxAddress.TryParse(to, out _))
        {
            return BadRequest(new { message = $"'{to}' is not a valid email address." });
        }

        // A real template rather than a "hello world", so what arrives is what a
        // citizen would actually receive.
        var sample = new Incident
        {
            Title = "Sample warning — test message",
            Description =
                "This is a test of the RescueSriLanka warning system. No action is needed.",
            Type = IncidentType.Flood,
            Severity = IncidentSeverity.High,
            District = user.District ?? "Colombo",
            AffectedRadiusMeters = 2500
        };

        var message = EmailTemplates.DistrictWarning(user, sample, sample.District);

        if (to is not null)
        {
            message = message with
            {
                ToAddress = to,
                ToName = null,
                BypassTestRedirect = true
            };
        }

        var sent = await sender.SendAsync(message, ct);

        return Ok(new
        {
            sent,
            transport = sender.Name,
            recipient = message.ToAddress,
            redirected = to is null && options.Value.Testmail.IsConfigured
                         && options.Value.Testmail.RedirectAllMail,
            testmailTag = to is null && options.Value.Testmail.IsConfigured
                ? TestmailOptions.TagFor(user.Email)
                : null
        });
    }

    /// <summary>
    /// Reads back what testmail.app received for one recipient — the other half
    /// of the loop, since SMTP can only tell us a message left the building.
    /// </summary>
    [HttpGet("inbox")]
    [ProducesResponseType(typeof(IReadOnlyList<TestmailMessage>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IReadOnlyList<TestmailMessage>>> Inbox(
        [FromQuery] string? email,
        [FromQuery] string? tag,
        [FromQuery] int limit = 10,
        CancellationToken ct = default)
    {
        if (!testmail.IsConfigured)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new { message = "testmail.app is not configured. Set Email:Testmail:Namespace and ApiKey." });
        }

        var resolved = tag
            ?? (email is null ? null : TestmailOptions.TagFor(email));

        if (string.IsNullOrWhiteSpace(resolved))
        {
            return BadRequest(new { message = "Provide either email or tag." });
        }

        return Ok(await testmail.FetchAsync(resolved, limit, ct));
    }
}
