using System.Net;
using System.Text;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Features.ComponentA.Models;
using RescueSriLanka.Api.Services.Email;

namespace RescueSriLanka.Api.Features.ComponentA.Services.Notifications;
/// <summary>
/// Every email this platform sends. All are built here so the wording of a
/// public safety message lives in one reviewable place rather than being
/// assembled inside whichever service happened to trigger it.
///
/// Every template ships HTML and plain text. Colours match the console's
/// severity scale, and — as on the map — colour never carries the meaning on
/// its own: the severity word is always written out.
/// </summary>
public static class EmailTemplates
{
    private const string Brand = "#faa71b";
    private const string Ink = "#0b0e13";
    private const string Body = "#5b6675";
    private const string Border = "#e4e8ee";

    private static string SeverityColour(IncidentSeverity severity) => severity switch
    {
        IncidentSeverity.Low => "#12946a",
        IncidentSeverity.Moderate => "#e5a800",
        IncidentSeverity.High => "#e35d0b",
        _ => "#9c1c3d"
    };

    /// <summary>Guidance is per disaster type — generic advice helps nobody.</summary>
    private static string[] SafetyAdvice(IncidentType type) => type switch
    {
        IncidentType.Flood =>
        [
            "Move to higher ground now, before roads become impassable.",
            "Never walk or drive through moving water.",
            "Switch off electricity at the mains if water may enter the building."
        ],
        IncidentType.Landslide =>
        [
            "Leave slopes and the ground directly below them immediately.",
            "Watch for cracked ground, tilting trees and sudden changes in stream water.",
            "Do not return for belongings once you are clear."
        ],
        IncidentType.Fire =>
        [
            "Leave the building and stay out. Do not go back inside.",
            "Keep low to avoid smoke as you go.",
            "Call 110 for the fire service once you are safe."
        ],
        IncidentType.Tsunami =>
        [
            "Go inland and uphill straight away — do not wait for an official siren.",
            "Stay at least 2 km from the coast until the all-clear is given.",
            "A receding sea is a warning sign. Move immediately."
        ],
        IncidentType.Storm =>
        [
            "Stay indoors and away from windows.",
            "Secure or bring in loose outdoor items.",
            "Keep clear of fallen power lines and damaged trees."
        ],
        IncidentType.Accident =>
        [
            "Avoid the area so emergency vehicles can get through.",
            "Follow diversions set out by the police.",
            "Call 1990 (Suwa Seriya) if you witness casualties."
        ],
        _ =>
        [
            "Follow instructions from local authorities and emergency services.",
            "Avoid the affected area until it is declared safe.",
            "Keep your phone charged and stay reachable."
        ]
    };

    /// <summary>Receipt for a citizen who has just filed a report.</summary>
    public static EmailMessage ReportReceived(User reporter, Incident incident)
    {
        var subject = $"Report received: {incident.Title}";

        var facts = new (string Label, string Value)[]
        {
            ("Reference", incident.Id.ToString()[..8].ToUpperInvariant()),
            ("Type", incident.Type.ToString()),
            ("District", incident.District ?? "Not given"),
            ("Reported", $"{incident.ReportedAt:dd MMM yyyy, HH:mm} UTC"),
            ("Status", "Awaiting verification")
        };

        var html = Wrap(
            heading: "Thank you — your report has reached us",
            accent: Brand,
            bodyHtml: $"""
                <p style="margin:0 0 16px">Hello {Escape(reporter.ShortName())},</p>
                <p style="margin:0 0 16px">
                  We have received your report of
                  <strong style="color:{Ink}">{Escape(incident.Title)}</strong>
                  and it is now in the emergency coordination queue.
                </p>
                {FactTable(facts)}
                <p style="margin:20px 0 8px;color:{Ink};font-weight:600">What happens next</p>
                <ol style="margin:0;padding-left:20px;color:{Body};font-size:14px;line-height:1.7">
                  <li>Our analysis agent grades the severity from what you described.</li>
                  <li>An emergency coordinator checks the report and confirms that grading.</li>
                  <li>Once confirmed, it appears on the public disaster map and, if the risk is
                      high, everyone in the district is warned by email.</li>
                </ol>
                <p style="margin:20px 0 0">
                  If anyone is in immediate danger, call <strong style="color:{Ink}">119</strong>
                  (Police) or <strong style="color:{Ink}">110</strong> (Fire &amp; Rescue) now.
                  This email is a receipt, not an emergency response.
                </p>
                """);

        var text = new StringBuilder()
            .AppendLine($"Hello {reporter.ShortName()},")
            .AppendLine()
            .AppendLine($"We have received your report of \"{incident.Title}\" and it is now in the")
            .AppendLine("emergency coordination queue.")
            .AppendLine()
            .AppendJoin(Environment.NewLine, facts.Select(f => $"{f.Label,-10}: {f.Value}"))
            .AppendLine()
            .AppendLine()
            .AppendLine("WHAT HAPPENS NEXT")
            .AppendLine("1. Our analysis agent grades the severity from what you described.")
            .AppendLine("2. An emergency coordinator checks the report and confirms that grading.")
            .AppendLine("3. Once confirmed it appears on the public map, and if the risk is high,")
            .AppendLine("   everyone in the district is warned by email.")
            .AppendLine()
            .AppendLine("If anyone is in immediate danger, call 119 (Police) or 110 (Fire & Rescue)")
            .AppendLine("now. This email is a receipt, not an emergency response.")
            .AppendLine()
            .AppendLine("— RescueSriLanka")
            .ToString();

        return new EmailMessage
        {
            ToAddress = reporter.Email,
            ToName = reporter.FullName,
            Subject = subject,
            HtmlBody = html,
            TextBody = text
        };
    }

    /// <summary>Area warning for everyone who has set this district in the app.</summary>
    public static EmailMessage DistrictWarning(User recipient, Incident incident, string district)
    {
        var severity = incident.Severity;
        var colour = SeverityColour(severity);
        var subject = $"{severity.ToString().ToUpperInvariant()} warning — {incident.Type} in {district}";

        var facts = new (string Label, string Value)[]
        {
            ("Risk level", severity.ToString()),
            ("Type", incident.Type.ToString()),
            ("District", district),
            ("Affected area", $"about {incident.AffectedRadiusMeters / 1000.0:0.0} km around the site"),
            ("Confirmed", $"{DateTime.UtcNow:dd MMM yyyy, HH:mm} UTC")
        };

        var advice = SafetyAdvice(incident.Type);

        var html = Wrap(
            heading: $"{severity} risk warning for {Escape(district)}",
            accent: colour,
            bodyHtml: $"""
                <p style="margin:0 0 16px">Hello {Escape(recipient.ShortName())},</p>
                <p style="margin:0 0 16px">
                  A coordinator has confirmed
                  <strong style="color:{Ink}">{Escape(incident.Title)}</strong>
                  in your district at
                  <strong style="color:{colour}">{severity} risk</strong>.
                </p>
                <p style="margin:0 0 16px;color:{Body}">{Escape(incident.Description)}</p>
                {FactTable(facts)}
                <p style="margin:20px 0 8px;color:{Ink};font-weight:600">What to do now</p>
                <ul style="margin:0;padding-left:20px;color:{Body};font-size:14px;line-height:1.7">
                  {string.Join("\n                  ", advice.Select(line => $"<li>{Escape(line)}</li>"))}
                </ul>
                <p style="margin:20px 0 0">
                  Emergency numbers: <strong style="color:{Ink}">119</strong> Police ·
                  <strong style="color:{Ink}">110</strong> Fire &amp; Rescue ·
                  <strong style="color:{Ink}">1990</strong> Ambulance.
                </p>
                <p style="margin:16px 0 0;font-size:12.5px;color:{Body}">
                  You are receiving this because you set your district to {Escape(district)} in the
                  RescueSriLanka app. You can change it, or turn these warnings off, under
                  Profile → Notification settings.
                </p>
                """);

        var text = new StringBuilder()
            .AppendLine($"{severity.ToString().ToUpperInvariant()} RISK WARNING — {district.ToUpperInvariant()}")
            .AppendLine()
            .AppendLine($"Hello {recipient.ShortName()},")
            .AppendLine()
            .AppendLine($"A coordinator has confirmed \"{incident.Title}\" in your district at")
            .AppendLine($"{severity} risk.")
            .AppendLine()
            .AppendLine(incident.Description)
            .AppendLine()
            .AppendJoin(Environment.NewLine, facts.Select(f => $"{f.Label,-14}: {f.Value}"))
            .AppendLine()
            .AppendLine()
            .AppendLine("WHAT TO DO NOW")
            .AppendJoin(Environment.NewLine, advice.Select(line => $"- {line}"))
            .AppendLine()
            .AppendLine()
            .AppendLine("Emergency numbers: 119 Police, 110 Fire & Rescue, 1990 Ambulance.")
            .AppendLine()
            .AppendLine($"You are receiving this because you set your district to {district} in the")
            .AppendLine("RescueSriLanka app. Change it, or turn warnings off, under")
            .AppendLine("Profile > Notification settings.")
            .AppendLine()
            .AppendLine("— RescueSriLanka")
            .ToString();

        return new EmailMessage
        {
            ToAddress = recipient.Email,
            ToName = recipient.FullName,
            Subject = subject,
            HtmlBody = html,
            TextBody = text
        };
    }

    /// <summary>
    /// First email a new citizen gets. Tells them what they signed up for and,
    /// if their district is already under warning, says so up front — someone
    /// registering during a flood is probably registering because of it.
    /// </summary>
    public static EmailMessage Welcome(User user, IReadOnlyList<Incident> activeWarnings)
    {
        var district = user.District;
        var underWarning = district is not null && activeWarnings.Count > 0;

        var subject = underWarning
            ? $"Welcome to RescueSriLanka — {activeWarnings.Count} active warning(s) in {district}"
            : "Welcome to RescueSriLanka";

        var districtHtml = district is null
            ? $"""
              <p style="margin:0 0 16px">
                You have not set a district yet, so we cannot warn you about disasters
                near you. Open <strong style="color:{Ink}">Profile → Notification settings</strong>
                in the app and choose where you live.
              </p>
              """
            : $"""
              <p style="margin:0 0 16px">
                You will be emailed whenever a coordinator confirms a high-risk disaster in
                <strong style="color:{Ink}">{Escape(district)}</strong>.
              </p>
              """;

        var warningsHtml = underWarning
            ? $"""
              <p style="margin:20px 0 8px;color:{Ink};font-weight:600">
                Warnings in force in {Escape(district!)} right now
              </p>
              {WarningList(activeWarnings)}
              """
            : string.Empty;

        var html = Wrap(
            heading: $"Welcome, {Escape(user.ShortName())}",
            accent: underWarning ? SeverityColour(activeWarnings[0].Severity) : Brand,
            bodyHtml: $"""
                <p style="margin:0 0 16px">
                  Your RescueSriLanka account is ready. With it you can report a disaster,
                  follow it as coordinators respond, and see safe and danger zones on the map.
                </p>
                {districtHtml}
                {warningsHtml}
                <p style="margin:20px 0 0">
                  Emergency numbers: <strong style="color:{Ink}">119</strong> Police ·
                  <strong style="color:{Ink}">110</strong> Fire &amp; Rescue ·
                  <strong style="color:{Ink}">1990</strong> Ambulance.
                </p>
                <p style="margin:16px 0 0;font-size:12.5px;color:{Body}">
                  You can turn these emails off at any time under Profile → Notification settings.
                </p>
                """);

        var text = new StringBuilder()
            .AppendLine($"Welcome, {user.ShortName()}")
            .AppendLine()
            .AppendLine("Your RescueSriLanka account is ready. With it you can report a disaster,")
            .AppendLine("follow it as coordinators respond, and see safe and danger zones on the map.")
            .AppendLine()
            .AppendLine(district is null
                ? "You have not set a district yet, so we cannot warn you about disasters near\n"
                  + "you. Choose where you live under Profile > Notification settings in the app."
                : $"You will be emailed whenever a coordinator confirms a high-risk disaster in\n{district}.")
            .AppendLine();

        if (underWarning)
        {
            text.AppendLine($"WARNINGS IN FORCE IN {district!.ToUpperInvariant()} RIGHT NOW")
                .AppendLine(WarningListText(activeWarnings))
                .AppendLine();
        }

        text.AppendLine("Emergency numbers: 119 Police, 110 Fire & Rescue, 1990 Ambulance.")
            .AppendLine()
            .AppendLine("You can turn these emails off under Profile > Notification settings.")
            .AppendLine()
            .AppendLine("— RescueSriLanka");

        return new EmailMessage
        {
            ToAddress = user.Email,
            ToName = user.FullName,
            Subject = subject,
            HtmlBody = html,
            TextBody = text.ToString()
        };
    }

    /// <summary>
    /// The warnings already in force in a district, for a citizen who has just
    /// chosen it and so missed the original alerts.
    /// </summary>
    public static EmailMessage DistrictBriefing(
        User recipient, string district, IReadOnlyList<Incident> activeWarnings)
    {
        var worst = activeWarnings[0].Severity;
        var subject = $"{worst.ToString().ToUpperInvariant()} warning in force — "
                      + $"{activeWarnings.Count} active in {district}";

        var html = Wrap(
            heading: $"Warnings in force in {Escape(district)}",
            accent: SeverityColour(worst),
            bodyHtml: $"""
                <p style="margin:0 0 16px">Hello {Escape(recipient.ShortName())},</p>
                <p style="margin:0 0 16px">
                  You have just set your district to
                  <strong style="color:{Ink}">{Escape(district)}</strong>. These warnings were
                  confirmed there before you joined, and are still in force:
                </p>
                {WarningList(activeWarnings)}
                <p style="margin:20px 0 0">
                  Emergency numbers: <strong style="color:{Ink}">119</strong> Police ·
                  <strong style="color:{Ink}">110</strong> Fire &amp; Rescue ·
                  <strong style="color:{Ink}">1990</strong> Ambulance.
                </p>
                <p style="margin:16px 0 0;font-size:12.5px;color:{Body}">
                  From now on you will be emailed as each new warning for {Escape(district)} is
                  confirmed. Change your district, or turn warnings off, under
                  Profile → Notification settings.
                </p>
                """);

        var text = new StringBuilder()
            .AppendLine($"WARNINGS IN FORCE — {district.ToUpperInvariant()}")
            .AppendLine()
            .AppendLine($"Hello {recipient.ShortName()},")
            .AppendLine()
            .AppendLine($"You have just set your district to {district}. These warnings were")
            .AppendLine("confirmed there before you joined, and are still in force:")
            .AppendLine()
            .AppendLine(WarningListText(activeWarnings))
            .AppendLine()
            .AppendLine("Emergency numbers: 119 Police, 110 Fire & Rescue, 1990 Ambulance.")
            .AppendLine()
            .AppendLine($"You will be emailed as each new warning for {district} is confirmed.")
            .AppendLine("Change your district, or turn warnings off, under")
            .AppendLine("Profile > Notification settings.")
            .AppendLine()
            .AppendLine("— RescueSriLanka")
            .ToString();

        return new EmailMessage
        {
            ToAddress = recipient.Email,
            ToName = recipient.FullName,
            Subject = subject,
            HtmlBody = html,
            TextBody = text
        };
    }

    /// <summary>
    /// One block per incident: severity, title, and the first line of advice for
    /// its type — enough to act on without opening the app.
    /// </summary>
    private static string WarningList(IReadOnlyList<Incident> warnings) =>
        string.Join("\n", warnings.Select(incident =>
        {
            var colour = SeverityColour(incident.Severity);
            return $"""
                <div style="margin:0 0 10px;padding:10px 12px;border:1px solid {Border};
                            border-left:4px solid {colour};border-radius:6px">
                  <p style="margin:0;font-size:12px;font-weight:700;letter-spacing:0.06em;
                            text-transform:uppercase;color:{colour}">
                    {incident.Severity} · {incident.Type}
                  </p>
                  <p style="margin:2px 0 4px;color:{Ink};font-weight:600">{Escape(incident.Title)}</p>
                  <p style="margin:0;font-size:13.5px;color:{Body}">{Escape(SafetyAdvice(incident.Type)[0])}</p>
                </div>
                """;
        }));

    private static string WarningListText(IReadOnlyList<Incident> warnings) =>
        string.Join(Environment.NewLine, warnings.Select(incident =>
            $"- [{incident.Severity.ToString().ToUpperInvariant()}] {incident.Type}: {incident.Title}"
            + $"{Environment.NewLine}  {SafetyAdvice(incident.Type)[0]}"));

    // ---------------------------------------------------------------- layout

    /// <summary>
    /// Shared shell. Inline styles and a fixed-width centred block, because
    /// mail clients strip stylesheets and many still lay out with tables.
    /// </summary>
    private static string Wrap(string heading, string accent, string bodyHtml) =>
        $"""
        <!doctype html>
        <html lang="en">
          <body style="margin:0;padding:24px 12px;background:#f5f7fa;
                       font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;
                       color:{Body};font-size:15px;line-height:1.6">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr>
                <td align="center">
                  <table role="presentation" width="560" cellpadding="0" cellspacing="0" border="0"
                         style="width:560px;max-width:100%;background:#ffffff;border:1px solid {Border};
                                border-radius:12px;overflow:hidden">
                    <tr><td style="height:4px;background:{accent}"></td></tr>
                    <tr>
                      <td style="padding:28px 32px">
                        <p style="margin:0 0 4px;font-size:13px;letter-spacing:0.08em;
                                  text-transform:uppercase;color:{Brand};font-weight:700">
                          RescueSriLanka
                        </p>
                        <h1 style="margin:0 0 20px;font-size:21px;line-height:1.3;color:{Ink};
                                   font-weight:600;letter-spacing:-0.02em">
                          {heading}
                        </h1>
                        {bodyHtml}
                      </td>
                    </tr>
                    <tr>
                      <td style="padding:16px 32px;border-top:1px solid {Border};background:#f5f7fa;
                                 font-size:12px;color:{Body}">
                        RescueSriLanka — disaster response &amp; resource coordination.
                        This mailbox is not monitored.
                      </td>
                    </tr>
                  </table>
                </td>
              </tr>
            </table>
          </body>
        </html>
        """;

    private static string FactTable((string Label, string Value)[] facts) =>
        $"""
        <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%"
               style="margin:4px 0;border-collapse:collapse">
          {string.Join("\n  ", facts.Select(fact => $"""
          <tr>
            <td style="padding:7px 0;border-bottom:1px solid {Border};font-size:13px;color:{Body};width:38%">{Escape(fact.Label)}</td>
            <td style="padding:7px 0;border-bottom:1px solid {Border};font-size:13.5px;color:{Ink};font-weight:600">{Escape(fact.Value)}</td>
          </tr>
          """))}
        </table>
        """;

    private static string Escape(string value) => WebUtility.HtmlEncode(value);

    /// <summary>First name only — a warning should read like a person wrote it.</summary>
    private static string ShortName(this User user) =>
        user.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
        ?? user.FullName;
}
