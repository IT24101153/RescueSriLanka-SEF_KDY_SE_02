# ADR 0002 — Where incident photographs are stored

- **Status:** Accepted
- **Date:** 2026-09-18
- **Component:** A — Incident & Disaster Map (`IncidentImage`)
- **Decision owner:** Student A

## Context

A citizen reporting a disaster can attach a photograph from the Flutter app's
camera. That photograph has two consumers: the Emergency Coordinator reviewing
the report in the React console, and the Incident Analysis Agent, which reads
the image alongside the description when grading severity.

The original implementation wrote the bytes to `wwwroot/uploads` and served them
back with `UseStaticFiles`. That works on a laptop and fails on a deployment: a
container's filesystem does not survive a restart, so every photograph uploaded
before a redeploy becomes a database row pointing at nothing — and the agent's
vision step silently loses its evidence.

This has to be weighed against the proposal's risk table, which commits to
avoiding *"reliance on paid or unstable external services"*. Adding a hosted
service here needs to be justified, not assumed — the more so because
[ADR 0001](0001-llm-provider.md) already spends one deviation from that
commitment on the language model.

## Decision

Storage is an interface, `IImageStore`, with two implementations selected by
configuration:

| Backend | When it is used | Where the bytes go |
| --- | --- | --- |
| `LocalDiskImageStore` | Default; whenever Cloudinary credentials are absent | `wwwroot/uploads/incidents/{id}/` |
| `CloudinaryImageStore` | When `CloudName`, `ApiKey` **and** `ApiSecret` are all set | Cloudinary, folder per incident |

```jsonc
"Cloudinary": { "CloudName": "…", "ApiKey": "…", "ApiSecret": "…" }
```

Missing or partial credentials fall back to local disk rather than failing
start-up. **Running this project requires no Cloudinary account.** The API logs
which backend is live:

```
Incident photos are stored via local-disk.
```

The stored value on `IncidentImage.StoragePath` is whatever the active backend
returns — an absolute `https://` URL from Cloudinary, or a site-relative path
from local disk. Both clients resolve the two forms, so neither has to know
which backend is in use. This avoided a schema migration.

## How this sits alongside ADR 0001

Both decisions deviate from the proposal's "no external services" commitment, so
the project now depends on two hosted services rather than none. That is worth
stating plainly rather than presenting each choice in isolation.

The difference is in how much each one can hurt:

- **The language model is on the critical path of the AI demonstration.** If
  Gemini rate-limits, every agent run degrades to the rule engine and the
  agentic contribution cannot be shown live. ADR 0001 accepts that risk and
  names the mitigation.
- **Image storage is on no critical path.** If Cloudinary is unreachable the
  upload returns `502`, the report is still filed, and `LoadForAnalysisAsync`
  treats an unfetchable photo as degraded input rather than a failure — the
  agent still grades from text, location, clustering and rainfall.

This dependency is also **optional at every level**: optional to configure
(local disk is the default and needs no account), optional per report (photos
are not required), and non-fatal when it breaks. The language model dependency
is none of those things, which is why it carries the heavier justification.

## Consequences

**Positive**

- Uploads survive a redeploy, which is what the feature needs to be real rather
  than a local-only demo.
- Serving a photograph costs the API nothing — the client fetches it from the
  CDN, and the row already holds the absolute URL.
- Validation, size limits and the database write live in `ImageStorageService`
  and are identical across both backends, so a change of destination cannot
  quietly change the rules.
- A group member with no Cloudinary account can still run and demo everything.

**Negative**

- A third-party service now appears in the stack that the proposal did not
  name. It is free-tier and optional, but it is a deviation and is recorded here
  as one.
- Photographs of identifiable people in distress leave the machine when
  Cloudinary is active. For a live deployment this would need a retention policy
  and a note in the citizen-facing terms; for an assessed prototype it is
  acceptable, and local disk remains the default.
- Credentials are one more thing to keep out of version control.
  `appsettings.Development.json` is gitignored; deployment supplies
  `Cloudinary__ApiSecret` as an environment variable.

## Alternatives considered

| Option | Rejected because |
| --- | --- |
| Local disk only | Uploads do not survive a redeploy; the agent loses its visual evidence and the coordinator sees broken images. |
| Bytes in PostgreSQL (`bytea`) | Needs no new service, but bloats the database the whole group shares, and every image view becomes an API round trip through EF Core. |
| A mounted volume on the host | Solves persistence but not serving, and ties the deployment to one machine. |
| Requiring Cloudinary outright | Would stop the project running for anyone without an account, for no benefit during development. |

## Related

- `Services/Storage/IImageStore.cs` — the contract
- `Services/Storage/CloudinaryImageStore.cs`, `LocalDiskImageStore.cs`
- `Services/ImageStorageService.cs` — validation and persistence, backend-agnostic
- [ADR 0001](0001-llm-provider.md) — the LLM provider, the project's other
  deviation from the no-external-services commitment
