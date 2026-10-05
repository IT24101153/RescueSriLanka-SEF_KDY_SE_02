using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentA.Services.Notifications;
using RescueSriLanka.Api.Features.ComponentD.Data;
using RescueSriLanka.Api.Features.ComponentD.Models;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Services.Push;

namespace RescueSriLanka.Api.Services.Email;

/// <summary>
/// Email for each user-visible action: the person who acted gets a receipt or
/// update, the staff role that needs to act gets a copy. Recipients are looked
/// up here, so controllers only say what happened.
/// </summary>
public interface IActionEmailService
{
    Task IncidentReportedAsync(Guid incidentId, CancellationToken ct = default);
    Task IncidentStatusChangedAsync(Guid incidentId, string status, CancellationToken ct = default);
    Task IncidentSeverityOverriddenAsync(Guid incidentId, string severity, CancellationToken ct = default);
    Task HelpRequestSubmittedAsync(Guid helpRequestId, CancellationToken ct = default);
    Task HelpRequestVerifiedAsync(Guid helpRequestId, bool isReal, CancellationToken ct = default);
    Task HelpRequestStatusChangedAsync(Guid helpRequestId, string status, CancellationToken ct = default);
    Task HelpRequestGuidanceAsync(Guid helpRequestId, bool critical, CancellationToken ct = default);
    Task HelpRequestCancelledAsync(Guid helpRequestId, CancellationToken ct = default);
    Task ResourceRequestSubmittedAsync(Guid resourceRequestId, CancellationToken ct = default);
    Task ResourceRequestStatusChangedAsync(Guid resourceRequestId, string status, CancellationToken ct = default);
    Task DonationSubmittedAsync(Guid donationId, CancellationToken ct = default);
    Task DonationStatusChangedAsync(Guid donationId, string status, CancellationToken ct = default);
    Task AssignmentCreatedAsync(Guid assignmentId, CancellationToken ct = default);
    Task AssignmentDecidedAsync(Guid assignmentId, string decision, CancellationToken ct = default);
    Task DispatchStatusChangedAsync(Guid dispatchId, string status, CancellationToken ct = default);
}

public sealed class ActionEmailService(
    AppDbContext db,
    ComponentDDbContext componentD,
    IEmailQueue queue,
    IPushNotificationService push,
    IOptions<EmailOptions> options,
    ILogger<ActionEmailService> logger) : IActionEmailService
{
    private const string Accent = "#0b5ea8";

    public async Task IncidentReportedAsync(Guid incidentId, CancellationToken ct = default)
    {
        var incident = await db.Incidents.AsNoTracking().FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return;

        await ToStaffAsync(UserRole.EmergencyCoordinator,
            $"New incident reported: {incident.Title}",
            "New incident reported",
            "A citizen has reported a new incident. Review the report and the AI assessment on the disaster dashboard.",
            new (string Label, string Value)[]
            {
                ("Incident", incident.Title),
                ("Type", incident.Type.ToString()),
                ("District", incident.District ?? "Not given"),
                ("Severity", incident.Severity.ToString()),
                ("Reported", Time(incident.ReportedAt)),
            },
            "Verify the report and decide on its severity from the dashboard.",
            ct);
    }

    public async Task IncidentStatusChangedAsync(Guid incidentId, string status, CancellationToken ct = default)
    {
        var incident = await db.Incidents.AsNoTracking().FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return;

        await ToCitizenAsync(incident.ReportedByUserId,
            $"Update on your incident report: {incident.Title}",
            "Your incident report was updated",
            $"The status of the incident you reported is now {status}.",
            new (string Label, string Value)[] { ("Incident", incident.Title), ("Status", status) },
            "Thank you for reporting this. You can follow the incident on the map.",
            ct);
    }

    public async Task IncidentSeverityOverriddenAsync(Guid incidentId, string severity, CancellationToken ct = default)
    {
        var incident = await db.Incidents.AsNoTracking().FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return;

        await ToCitizenAsync(incident.ReportedByUserId,
            $"Severity updated for your incident report: {incident.Title}",
            "Severity updated",
            $"A coordinator has set the severity of the incident you reported to {severity}.",
            new (string Label, string Value)[] { ("Incident", incident.Title), ("Severity", severity) },
            "The safety map reflects this change.",
            ct);
    }

    public async Task HelpRequestSubmittedAsync(Guid helpRequestId, CancellationToken ct = default)
    {
        var request = await db.HelpRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == helpRequestId, ct);
        if (request is null) return;

        var facts = new (string Label, string Value)[]
        {
            ("Request type", request.Type.ToString()),
            ("Description", request.Description),
            ("People affected", request.EstimatedPeopleCount?.ToString() ?? "Not given"),
            ("Submitted", Time(request.CreatedAt)),
        };

        await ToCitizenAsync(request.CitizenId,
            "We received your help request",
            "Help request received",
            "Thank you. Your help request has been received and is waiting for verification.",
            facts,
            "You will get another email each time its status changes.",
            ct);

        await ToStaffAsync(UserRole.HelpRequestManager,
            $"New help request: {request.Type}",
            "New help request",
            "A citizen has submitted a help request that needs verification.",
            facts,
            "Open the help request dashboard to review it.",
            ct);
    }

    public async Task HelpRequestVerifiedAsync(Guid helpRequestId, bool isReal, CancellationToken ct = default)
    {
        var request = await db.HelpRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == helpRequestId, ct);
        if (request is null) return;

        var facts = new (string Label, string Value)[]
        {
            ("Request type", request.Type.ToString()),
            ("Description", request.Description),
        };

        if (isReal)
        {
            await ToCitizenAsync(request.CitizenId,
                "Your help request was verified",
                "Help request verified",
                "Your help request has been verified and is now being coordinated.",
                facts,
                "You will be emailed as the response moves forward.",
                ct);
        }
        else
        {
            await ToCitizenAsync(request.CitizenId,
                "Your help request could not be verified",
                "Help request not verified",
                "Your help request could not be verified and has been closed. If this is a mistake, please submit it again with more detail.",
                facts,
                "Thank you for your understanding.",
                ct);
        }
    }

    public async Task HelpRequestStatusChangedAsync(Guid helpRequestId, string status, CancellationToken ct = default)
    {
        var request = await db.HelpRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == helpRequestId, ct);
        if (request is null) return;

        await ToCitizenAsync(request.CitizenId,
            "Update on your help request",
            "Help request update",
            $"The status of your help request is now {status}.",
            new (string Label, string Value)[] { ("Request type", request.Type.ToString()), ("Status", status) },
            "You can see the full history in the app.",
            ct);
    }

    // The advice itself stays in the app; a push or email only says there is something to read.
    public async Task HelpRequestGuidanceAsync(Guid helpRequestId, bool critical, CancellationToken ct = default)
    {
        var request = await db.HelpRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == helpRequestId, ct);
        if (request is null) return;

        await ToCitizenAsync(request.CitizenId,
            critical ? "Important safety guidance for your help request" : "New message about your help request",
            critical ? "Important safety guidance" : "New message from the response team",
            critical
                ? "The response team has sent urgent safety guidance for your help request. Please read it now."
                : "The response team has sent you a message about your help request.",
            new (string Label, string Value)[] { ("Request type", request.Type.ToString()) },
            "Open the app and go to My requests to read it.",
            ct);
    }

    public async Task HelpRequestCancelledAsync(Guid helpRequestId, CancellationToken ct = default)
    {
        var request = await db.HelpRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == helpRequestId, ct);
        if (request is null) return;

        await ToStaffAsync(UserRole.HelpRequestManager,
            $"Help request cancelled: {request.Type}",
            "Help request cancelled by the requester",
            "The citizen who submitted this help request cancelled it.",
            new (string Label, string Value)[] { ("Request type", request.Type.ToString()), ("Description", request.Description) },
            "No action is needed unless you were already responding to it.",
            ct);
    }

    public async Task ResourceRequestSubmittedAsync(Guid resourceRequestId, CancellationToken ct = default)
    {
        var request = await db.ResourceHelpRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == resourceRequestId, ct);
        if (request is null) return;

        var facts = new (string Label, string Value)[]
        {
            ("Need", request.NeedType),
            ("Requested by", request.RequesterName),
            ("Description", request.Description),
            ("Submitted", Time(request.CreatedAtUtc)),
        };

        await ToCitizenAsync(request.UserId,
            "We received your resource request",
            "Resource request received",
            "Thank you. Your request for supplies has been received and will be reviewed by the resource team.",
            facts,
            "You will get another email when its status changes.",
            ct);

        await ToStaffAsync(UserRole.ResourceManager,
            $"New resource request: {request.NeedType}",
            "New resource request",
            "A new request for supplies has been submitted and needs review.",
            facts,
            "Open the resource dashboard to accept or reject it.",
            ct);
    }

    public async Task ResourceRequestStatusChangedAsync(Guid resourceRequestId, string status, CancellationToken ct = default)
    {
        var request = await db.ResourceHelpRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == resourceRequestId, ct);
        if (request is null) return;

        await ToCitizenAsync(request.UserId,
            "Update on your resource request",
            "Resource request update",
            $"The status of your request for {request.NeedType} is now {status}.",
            new (string Label, string Value)[] { ("Need", request.NeedType), ("Status", status) },
            "Thank you for your patience.",
            ct);
    }

    public async Task DonationSubmittedAsync(Guid donationId, CancellationToken ct = default)
    {
        var donation = await db.Donations.AsNoTracking().FirstOrDefaultAsync(d => d.Id == donationId, ct);
        if (donation is null) return;

        var facts = new (string Label, string Value)[]
        {
            ("Donor", donation.DonorName),
            ("Donation", donation.DonationType),
            ("Quantity", $"{donation.Quantity} {donation.Unit}"),
            ("Submitted", Time(donation.CreatedAtUtc)),
        };

        await ToCitizenAsync(donation.UserId,
            "Thank you for your donation offer",
            "Donation offer received",
            "Thank you for offering to help. Your donation offer has been received and will be reviewed.",
            facts,
            "You will get another email when it is accepted or its status changes.",
            ct);

        await ToStaffAsync(UserRole.ResourceManager,
            $"New donation offer: {donation.DonationType}",
            "New donation offer",
            "A new donation offer has been submitted and needs review.",
            facts,
            "Open the resource dashboard to accept or reject it.",
            ct);
    }

    public async Task DonationStatusChangedAsync(Guid donationId, string status, CancellationToken ct = default)
    {
        var donation = await db.Donations.AsNoTracking().FirstOrDefaultAsync(d => d.Id == donationId, ct);
        if (donation is null) return;

        await ToCitizenAsync(donation.UserId,
            "Update on your donation offer",
            "Donation offer update",
            $"The status of your {donation.DonationType} donation offer is now {status}.",
            new (string Label, string Value)[] { ("Donation", donation.DonationType), ("Status", status) },
            "Thank you for helping.",
            ct);
    }

    public async Task AssignmentCreatedAsync(Guid assignmentId, CancellationToken ct = default)
    {
        var assignment = await componentD.Assignments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == assignmentId, ct);
        if (assignment is null) return;

        var team = await componentD.RescueTeams.AsNoTracking()
            .Where(t => t.Id == assignment.RescueTeamId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(ct) ?? "Assigned rescue team";

        var facts = new (string Label, string Value)[]
        {
            ("Rescue team", team),
            ("Required skill", assignment.RequiredSkill.ToString()),
            ("Status", "Awaiting coordinator approval"),
        };

        await ToCitizenAsync(await CitizenForAssignmentAsync(assignment, ct),
            "A rescue response is being arranged",
            "Rescue response planned",
            "A rescue team has been proposed for your request. The Rescue Coordinator is reviewing it before anything is dispatched.",
            facts,
            "You will be emailed when the plan is decided and on each dispatch update.",
            ct);

        await ToStaffAsync(UserRole.RescueTeam,
            "New rescue assignment to review",
            "New rescue assignment",
            "A rescue response plan has been created and is waiting for a safety review and decision.",
            facts,
            "Open the rescue coordination dashboard to review it.",
            ct);
    }

    public async Task AssignmentDecidedAsync(Guid assignmentId, string decision, CancellationToken ct = default)
    {
        var assignment = await componentD.Assignments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == assignmentId, ct);
        if (assignment is null) return;

        var approved = string.Equals(decision, "Approved", StringComparison.OrdinalIgnoreCase);
        await ToCitizenAsync(await CitizenForAssignmentAsync(assignment, ct),
            approved ? "A rescue response was approved" : $"Rescue response update: {decision}",
            approved ? "Rescue response approved" : "Rescue response update",
            approved
                ? "The Rescue Coordinator approved the response plan for your request."
                : $"The response plan for your request was {decision.ToLowerInvariant()}.",
            new (string Label, string Value)[] { ("Decision", decision) },
            "You will be emailed on each dispatch update.",
            ct);
    }

    public async Task DispatchStatusChangedAsync(Guid dispatchId, string status, CancellationToken ct = default)
    {
        var dispatch = await componentD.Dispatches.AsNoTracking().FirstOrDefaultAsync(d => d.Id == dispatchId, ct);
        if (dispatch is null) return;

        var assignment = await componentD.Assignments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == dispatch.AssignmentId, ct);
        if (assignment is null) return;

        await ToCitizenAsync(await CitizenForAssignmentAsync(assignment, ct),
            "Rescue team update",
            "Rescue team update",
            $"Your rescue response is now {status}.",
            new (string Label, string Value)[] { ("Status", status) },
            "Stay safe and keep your phone available.",
            ct);
    }

    private async Task<Guid?> CitizenForAssignmentAsync(Assignment assignment, CancellationToken ct)
    {
        if (assignment.HelpRequestId is { } helpRequestId)
        {
            return await db.HelpRequests.AsNoTracking()
                .Where(r => r.Id == helpRequestId)
                .Select(r => (Guid?)r.CitizenId)
                .FirstOrDefaultAsync(ct);
        }

        if (assignment.IncidentId is { } incidentId)
        {
            return await db.Incidents.AsNoTracking()
                .Where(i => i.Id == incidentId)
                .Select(i => i.ReportedByUserId)
                .FirstOrDefaultAsync(ct);
        }

        return null;
    }

    private async Task ToCitizenAsync(
        Guid? userId,
        string subject,
        string heading,
        string intro,
        (string Label, string Value)[] facts,
        string closing,
        CancellationToken ct)
    {
        if (userId is null) return;

        // Push and email are separate opt-ins, so each is checked on its own: a
        // citizen who has switched off one still hears through the other.
        await push.SendToUsersAsync([userId.Value], new PushMessage(heading, intro), ct);

        var citizen = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive && u.EmailNotificationsEnabled, ct);
        if (citizen is not null)
            Send(Compose(citizen, subject, heading, intro, facts, closing));
    }

    private async Task ToStaffAsync(
        UserRole role,
        string subject,
        string heading,
        string intro,
        (string Label, string Value)[] facts,
        string closing,
        CancellationToken ct)
    {
        var staff = await db.Users.AsNoTracking()
            .Where(u => u.Role == role && u.IsActive && u.EmailNotificationsEnabled)
            .ToListAsync(ct);

        foreach (var member in staff)
            Send(Compose(member, subject, heading, intro, facts, closing));
    }

    private void Send(EmailMessage message)
    {
        if (!options.Value.Enabled) return;

        if (!queue.TryQueue(message))
            logger.LogWarning("Could not queue email to {Email}: {Subject}", message.ToAddress, message.Subject);
    }

    private static EmailMessage Compose(
        User recipient,
        string subject,
        string heading,
        string intro,
        (string Label, string Value)[] facts,
        string closing)
    {
        var html = EmailTemplates.Wrap(
            heading: EmailTemplates.Escape(heading),
            accent: Accent,
            bodyHtml: $"""
                <p style="margin:0 0 16px">Hello {EmailTemplates.Escape(recipient.FullName)},</p>
                <p style="margin:0 0 16px">{EmailTemplates.Escape(intro)}</p>
                {EmailTemplates.FactTable(facts)}
                <p style="margin:20px 0 0">{EmailTemplates.Escape(closing)}</p>
                """);

        var text = new StringBuilder()
            .AppendLine($"Hello {recipient.FullName},")
            .AppendLine()
            .AppendLine(intro)
            .AppendLine();
        foreach (var (label, value) in facts)
            text.AppendLine($"{label,-16}: {value}");
        text.AppendLine()
            .AppendLine(closing)
            .AppendLine()
            .AppendLine("— RescueSriLanka");

        return new EmailMessage
        {
            ToAddress = recipient.Email,
            ToName = recipient.FullName,
            Subject = OneLine(subject),
            HtmlBody = html,
            TextBody = text.ToString()
        };
    }

    private static string Time(DateTime value) => $"{value:dd MMM yyyy, HH:mm} UTC";

    // A subject is a single header line: a title containing a line break must not split it.
    private static string OneLine(string value) =>
        string.Join(' ', value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
}
