# ADR 0004 — Email delivery and the test inbox

- **Status:** Accepted
- **Date:** 2026-09-21
- **Component:** Cross-cutting — citizen notifications
- **Decision owner:** Student A

## Context

Two notifications were wanted:

1. A citizen who files a disaster report gets an email confirming it arrived.
2. A citizen who has chosen a district in app settings gets an email when a new
   disaster warning with a risk level is raised for that district.

The service named for this was **testmail.app**, with an API key supplied.

The first thing that had to be settled is what testmail.app actually is.
[Its documentation](https://testmail.app/docs/) is unambiguous: it provides
programmable inboxes at `{namespace}.{tag}@inbox.testmail.app` and a JSON and
GraphQL API for *querying what those inboxes received*. There is no outbound
send, no SMTP relay, and the GraphQL schema exposes no mutations. It is a tool
for asserting that the email your application sent is correct.

So testmail.app cannot be the thing that delivers a flood warning to a citizen.
Something still has to send.

## Decision

**Split the two halves. SMTP sends; testmail.app verifies.**

| Concern | Choice |
| --- | --- |
| Transport | SMTP through MailKit, behind `IEmailSender` |
| Transport when unconfigured | `LoggingEmailSender` — writes the message to the log |
| Development recipient | Rewritten to the testmail.app namespace |
| Verification | testmail.app JSON API via `ITestmailClient` |

Three consequences follow, and each was deliberate.

**No SMTP credentials are needed to run the project.** With `Email:Smtp:Host`
empty, the sender logs each message instead of delivering it. Every other part
of the feature still runs for real: the triggers fire, the recipient query runs,
the templates render. Only the last hop is a log line. This matters more than it
sounds — the alternative is a feature that cannot be demonstrated, reviewed or
tested by anyone who does not hold a mail account, which in a team project means
most people most of the time.

**Redirection happens at the last hop, not at the lookup.** In development,
`TestmailRedirectingEmailSender` wraps the real transport and rewrites the
recipient to `{namespace}.{tag}@inbox.testmail.app`, where the tag is derived
from the real address (`priya@gmail.com` → `priyagmailcom`). The notification
service above it still looks up the actual citizens in the actual district and
still addresses them by name. A demo therefore exercises the true recipient
query, and cannot email a member of the public by accident.

**Verification is a first-class part of the feature.** SMTP can only report that
a message left the building. `ITestmailClient` reads the namespace back, so
`GET /api/notifications/inbox?email=…` answers "did it actually arrive, and what
did it say?" — the question that matters during a demo.

## Alternatives considered

**A transactional email API (SendGrid, Brevo, Resend).** The right answer for
production, and the interface is ready for one — `IEmailSender` has a single
method. Rejected for now because all three want sender-domain verification
before they will deliver, which is a DNS exercise this project does not need in
order to show the feature working.

**Mailtrap instead of testmail.app.** Mailtrap is both a catching SMTP server
and an inbox, so it would collapse the two halves into one dependency. Rejected
because testmail.app was the service chosen for the project, and because
Mailtrap's catch-all model hides the redirect step rather than making it
explicit.

**Sending directly from the request.** Rejected outright. A citizen filing a
report during a flood must not wait on a mail server, and must not have their
report fail because one is down. Notifications go on a bounded background queue
(`NotificationQueue`), mirroring `IncidentAnalysisQueue`.

## Warning policy

A district-wide email is not something to hand to an unreviewed report, so:

- **A warning only goes out once a human has confirmed the risk** — a coordinator
  verifying the incident, approving the AI's severity, or setting the severity
  by hand. The Incident Analysis Agent proposing `Critical` on its own warns
  nobody, which keeps the existing human approval gate meaningful.
- **Only High and Critical** (`Email:MinimumWarningSeverity`). An alert people
  learn to ignore is worse than no alert.
- **Once per incident.** Verification and approval both fire; the first send
  stamps `Incident.DistrictWarningSentAt` and later triggers are no-ops. A
  warning that reached nobody is not stamped, so a citizen who sets their
  district a minute later is still warned by the next trigger.

## Consequences

- Adds one dependency, MailKit, to the API.
- `User` gains `District` and `EmailNotificationsEnabled`; `Incident` gains
  `DistrictWarningSentAt` (migration `EmailNotifications`).
- District matching is string-based, so both sides are normalised through
  `SriLankaDistricts` — a report filed for `"colombo"` still reaches someone who
  picked `"Colombo"` from the app's list.
- Moving to a production email provider means writing one `IEmailSender` and
  changing one registration in `Program.cs`. Nothing above that line changes.

## Links

- Setup and troubleshooting: [`docs/email-notifications.md`](../email-notifications.md)
- testmail.app API: <https://testmail.app/docs/>
