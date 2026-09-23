using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentA.Models;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Services.Email;

namespace RescueSriLanka.Api.Features.ComponentA.Services.Notifications;
public interface INotificationService
{
    /// <summary>Receipt to the citizen who filed a report.</summary>
    Task<int> SendReportReceivedAsync(Guid incidentId, CancellationToken ct = default);

    /// <summary>
    /// Warns everyone who has set this incident's district in the app. Safe to
    /// call more than once for the same incident — only the first call sends.
    /// </summary>
    Task<int> SendDistrictWarningAsync(Guid incidentId, CancellationToken ct = default);

    /// <summary>
    /// Welcome to a newly registered citizen, listing any warnings already in
    /// force in the district they chose.
    /// </summary>
    Task<int> SendWelcomeAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// The warnings already in force in a citizen's district, sent when they
    /// choose or change it. District warnings go out once, at confirmation, so
    /// without this someone who moves into a district mid-emergency would
    /// never hear about it. Sends nothing when the district is quiet.
    /// </summary>
    Task<int> SendDistrictBriefingAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>
/// Decides who hears about what, and why.
///
/// Two rules shape everything here. A warning only goes out once a human has
/// confirmed the risk, because an email to a whole district is not something to
/// hand to an unreviewed report. And a warning goes out once per incident, no
/// matter how many times verification and approval fire.
/// </summary>
public class NotificationService(
    AppDbContext db,
    IEmailSender sender,
    IOptions<EmailOptions> options,
    ILogger<NotificationService> logger) : INotificationService
{
    private readonly EmailOptions _options = options.Value;

    public async Task<int> SendReportReceivedAsync(Guid incidentId, CancellationToken ct = default)
    {
        if (!_options.Enabled) return 0;

        var incident = await db.Incidents.AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.Id == incidentId, ct);

        if (incident?.ReportedByUserId is not Guid reporterId)
        {
            // An anonymous report has nobody to write to. Not a failure.
            return 0;
        }

        var reporter = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(user => user.Id == reporterId, ct);

        if (reporter is null || !reporter.IsActive || !reporter.EmailNotificationsEnabled)
        {
            return 0;
        }

        var sent = await sender.SendAsync(EmailTemplates.ReportReceived(reporter, incident), ct);
        return sent ? 1 : 0;
    }

    public async Task<int> SendDistrictWarningAsync(Guid incidentId, CancellationToken ct = default)
    {
        if (!_options.Enabled) return 0;

        // Tracked: a successful send stamps the incident.
        var incident = await db.Incidents
            .FirstOrDefaultAsync(entity => entity.Id == incidentId, ct);

        if (incident is null || !incident.IsActive) return 0;

        if (incident.DistrictWarningSentAt is not null)
        {
            logger.LogDebug(
                "Incident {Id} has already warned its district; skipping.", incidentId);
            return 0;
        }

        if (incident.Severity < _options.MinimumWarningSeverity)
        {
            logger.LogInformation(
                "Incident {Id} is {Severity}; below the {Minimum} warning threshold.",
                incidentId, incident.Severity, _options.MinimumWarningSeverity);
            return 0;
        }

        // Both sides of the comparison are reduced to one spelling, so a report
        // filed for "colombo" still reaches someone who picked "Colombo".
        var district = SriLankaDistricts.Normalise(incident.District);

        if (district is null)
        {
            logger.LogWarning(
                "Incident {Id} has district {District}, which matches no Sri Lankan district — "
                + "nobody can be warned.",
                incidentId, incident.District ?? "(none)");
            return 0;
        }

        var recipients = await db.Users.AsNoTracking()
            .Where(user =>
                user.IsActive &&
                user.EmailNotificationsEnabled &&
                user.District == district)
            .ToListAsync(ct);

        if (recipients.Count == 0)
        {
            // Leave the incident unstamped: someone who sets this district in a
            // minute's time should still be warned by the next trigger.
            logger.LogInformation(
                "No subscribers in {District} for incident {Id}.", district, incidentId);
            return 0;
        }

        var sent = 0;
        foreach (var recipient in recipients)
        {
            if (await sender.SendAsync(
                    EmailTemplates.DistrictWarning(recipient, incident, district), ct))
            {
                sent++;
            }
        }

        if (sent > 0)
        {
            incident.DistrictWarningSentAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        logger.LogInformation(
            "Warned {Sent}/{Total} subscriber(s) in {District} about incident {Id} ({Severity}).",
            sent, recipients.Count, district, incidentId, incident.Severity);

        return sent;
    }

    public async Task<int> SendWelcomeAsync(Guid userId, CancellationToken ct = default)
    {
        if (!_options.Enabled) return 0;

        var user = await FindReachableUserAsync(userId, ct);
        if (user is null) return 0;

        var warnings = await ActiveWarningsInAsync(user.District, ct);

        var sent = await sender.SendAsync(EmailTemplates.Welcome(user, warnings), ct);
        return sent ? 1 : 0;
    }

    public async Task<int> SendDistrictBriefingAsync(Guid userId, CancellationToken ct = default)
    {
        if (!_options.Enabled) return 0;

        var user = await FindReachableUserAsync(userId, ct);
        if (user?.District is not string district) return 0;

        var warnings = await ActiveWarningsInAsync(district, ct);

        if (warnings.Count == 0)
        {
            // A quiet district is not news. The next confirmed warning will
            // reach them through SendDistrictWarningAsync like everyone else.
            return 0;
        }

        var sent = await sender.SendAsync(
            EmailTemplates.DistrictBriefing(user, district, warnings), ct);
        return sent ? 1 : 0;
    }

    private async Task<User?> FindReachableUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.Id == userId, ct);

        return user is { IsActive: true, EmailNotificationsEnabled: true } ? user : null;
    }

    /// <summary>
    /// What a subscriber to this district would already have been warned about:
    /// open, at or above the warning threshold, and confirmed by a human —
    /// the same bar <see cref="SendDistrictWarningAsync"/> holds itself to.
    /// Most severe first.
    /// </summary>
    private async Task<IReadOnlyList<Incident>> ActiveWarningsInAsync(
        string? district, CancellationToken ct)
    {
        if (SriLankaDistricts.Normalise(district) is not string canonical) return [];

        var candidates = await db.Incidents.AsNoTracking()
            .Where(incident =>
                incident.IsActive &&
                incident.Severity >= _options.MinimumWarningSeverity &&
                (incident.DistrictWarningSentAt != null ||
                 incident.VerifiedAt != null ||
                 incident.Status == IncidentStatus.Verified ||
                 incident.Status == IncidentStatus.InProgress))
            .ToListAsync(ct);

        // Incident districts are free text from the reporter, so they are
        // matched after normalising rather than in SQL.
        return [.. candidates
            .Where(incident => SriLankaDistricts.Normalise(incident.District) == canonical)
            .OrderByDescending(incident => incident.Severity)
            .ThenByDescending(incident => incident.ReportedAt)];
    }
}
