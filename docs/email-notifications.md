# Email notifications — setup and behaviour

How the two notification emails work, how to configure them, and how to prove
they arrived. For *why* it is built this way, see
[ADR 0004](adr/0004-email-notifications.md).

---

## What gets sent

| Email | Goes to | Triggered by |
|---|---|---|
| **Report received** | The citizen who filed the report | `POST /api/incidents` |
| **District risk warning** | Everyone who set that district in app settings | A coordinator verifying the incident, approving the AI's severity, or setting the severity by hand |

The warning only goes out when:

- a **human** has confirmed the risk — the AI proposing `Critical` on its own
  warns nobody;
- the severity is **High or Critical** (`Email:MinimumWarningSeverity`);
- the incident is **active** and its district is one of Sri Lanka's 25;
- the incident **has not already warned its district** — verification and
  approval both fire, but the district hears once.

Recipients are active accounts with `EmailNotificationsEnabled` and a matching
`District`. District text is normalised on both sides, so a report filed for
`"colombo"`, `"COLOMBO"` or `"Colombo District"` still reaches someone who picked
`"Colombo"` in the app.

---

## Configuration

Everything lives under `Email` in `appsettings.Development.json`, which is
**gitignored** — no credential belongs in a committed file.

```jsonc
"Email": {
  "Enabled": true,
  "FromAddress": "alerts@rescuesrilanka.lk",
  "FromName": "RescueSriLanka Alerts",
  "MinimumWarningSeverity": "High",     // Low | Moderate | High | Critical

  "Smtp": {
    "Host": "",                          // empty => log instead of send
    "Port": 587,
    "UseStartTls": true,
    "Username": "",
    "Password": ""
  },

  "Testmail": {
    "Namespace": "",                     // from the testmail.app dashboard
    "ApiKey": "…",                       // only needed to READ the inbox
    "RedirectAllMail": true
  }
}
```

### It already runs with none of this filled in

With `Smtp.Host` empty, the API uses `LoggingEmailSender`: every trigger,
recipient lookup and template runs for real, and the finished message is written
to the console instead of being delivered. Start the API and watch for:

```
Email notifications ON via console log (no SMTP host configured);
district warnings at High and above.
```

That line prints at start-up and always tells you which transport is live.

### Sending for real

testmail.app **cannot send** — it only receives. Point `Smtp` at any SMTP
server. Gmail with an [app password](https://myaccount.google.com/apppasswords)
is the quickest:

```jsonc
"Smtp": {
  "Host": "smtp.gmail.com",
  "Port": 587,
  "UseStartTls": true,
  "Username": "you@gmail.com",
  "Password": "your-16-char-app-password"
}
```

A normal Google password will not work; 2-Step Verification must be on before
app passwords can be created.

### Pointing it at testmail.app

Fill in `Testmail.Namespace` (8 characters, from the testmail.app dashboard —
the API key alone does not identify it). With `RedirectAllMail: true`, every
recipient is rewritten to a namespace address:

```
priya@gmail.com  →  <namespace>.priyagmailcom@inbox.testmail.app
```

The tag is the real address with everything but letters and digits stripped, so
each person's mail stays separable, and a test can ask for the exact tag it sent
to. The notification service above still looks up the real citizens in the real
district — only the envelope changes, so a demo can never email the public by
accident.

**Leave `RedirectAllMail` off in production**, or nobody receives anything.

---

## Proving it works

```http
### Sample warning to yourself — checks transport, template and redirect
POST /api/notifications/test
Authorization: Bearer <coordinator token>
```

Response names the transport and the tag your mail landed under:

```json
{ "sent": true, "transport": "SMTP smtp.gmail.com:587 → testmail.app/abc12",
  "recipient": "coordinator@rescue.lk", "testmailTag": "coordinatorrescuelk" }
```

Then read the inbox back through testmail.app's API:

```http
GET /api/notifications/inbox?email=coordinator@rescue.lk&limit=5
Authorization: Bearer <coordinator token>
```

Both endpoints are **Emergency Coordinator only**. `inbox` returns `503` when
`Testmail.Namespace` and `ApiKey` are not both set.

`backend/RescueSriLanka.Api/RescueSriLanka.Api.http` walks the whole flow —
sign in, subscribe to a district, file a report, verify it, read the inbox.

---

## API

| Method | Endpoint | Auth | Purpose |
|---|---|---|---|
| GET | `/api/districts` | anonymous | The 25 districts, for the app's picker |
| GET | `/api/auth/me` | signed in | Now includes `district`, `emailNotificationsEnabled` |
| PATCH | `/api/auth/me/preferences` | signed in | Save district / email opt-out |
| POST | `/api/notifications/test` | Coordinator | Send a sample warning to yourself |
| GET | `/api/notifications/inbox` | Coordinator | Read the testmail.app inbox |

`PATCH /api/auth/me/preferences` distinguishes an **absent** field from an
explicit **null**: absent leaves the setting alone, null clears the district and
unsubscribes.

```jsonc
{ "district": "Kandy", "emailNotificationsEnabled": true }  // subscribe
{ "district": null }                                         // unsubscribe
{ "emailNotificationsEnabled": false }                       // silence all email
```

An unknown district returns `400` rather than saving text that would never match
an incident.

---

## In the app

**Profile → Notification settings** (`mobile/lib/screens/profile/notification_settings.dart`):
a district picker, an email switch, and a Save button that only lights up when
something changed. The district list comes from `GET /api/districts`, so the app
cannot drift from the list warnings are matched on.

---

## Troubleshooting

| Symptom | Cause |
|---|---|
| Messages appear in the log, never in an inbox | No `Smtp.Host` — that is the fallback sender working as designed |
| `sent: true` but nothing in testmail | `Testmail.Namespace` empty, so no redirect happened and it went to the real address |
| Nobody in the district is warned | Severity below `MinimumWarningSeverity`, nobody subscribed, or the incident already warned once (`DistrictWarningSentAt`) |
| A second verification sends nothing | Working as intended — one warning per incident |
| `inbox` returns 503 | `Testmail.Namespace` or `ApiKey` missing |
| Gmail rejects the login | Needs an app password with 2-Step Verification on, not the account password |
| `An incomplete certificate revocation check occurred` | macOS cannot finish an OCSP lookup. Handled automatically — `Smtp.ShouldCheckRevocation` defaults to off on macOS. Set `Smtp.CheckCertificateRevocation` explicitly to override |

### That certificate error is worth understanding

The first real send on macOS fails with:

```
MailKit.Security.SslHandshakeException: An error occurred while attempting to
establish an SSL or TLS connection.
  • An incomplete certificate revocation check occurred.
```

It reads like a broken certificate or a bad password, and it is neither. MailKit
asks for a certificate revocation check by default; macOS cannot complete the
OCSP lookup, so the handshake is abandoned even though the certificate is
perfectly valid. `SmtpOptions.ShouldCheckRevocation` therefore defaults to off
on macOS and on everywhere else. Signature, expiry, hostname and chain-to-root
are still verified in both cases — the only thing given up is noticing that a
valid certificate was revoked after issue.

Every decision and skip is logged with its reason, so the API log says which of
these happened rather than leaving it to be guessed.
