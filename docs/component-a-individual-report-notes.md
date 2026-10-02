# Component A — Individual Report working notes

**Student A: Lelum Jayasooriya, IT24101153** · Component A — Incident & Disaster Map · Agent: Incident Analysis Agent

Compiled 1 October 2026 from the code on `main` at commit `008dbf4`. This is source material laid out under
the guide's seven headings, not the report itself. Three kinds of marker are used:

- **[verified]** — read from the code or produced by a command run on 1 Oct 2026. Safe to cite.
- **[you]** — only you can supply it (screenshots, real run output, your own words, signature).
- **[check]** — something in the repo that does not match what the report would naturally claim. Fix it or
  word around it before you write that part.

Older material: [component-a-report.md](component-a-report.md), [component-a-agent.md](component-a-agent.md),
[component-a-performance.md](component-a-performance.md). Where these notes differ from those files, these
notes are newer.

---

## 0. Read this first — things that will cost marks if left alone

| # | Issue | Why it matters | What to do |
|---|---|---|---|
| 1 | Git history covers 9 Sep – 1 Oct (about 3 weeks), with 25 of your 73 commits on 23 Sep and 13 on 29 Sep. The last two commits are large (`7c593ed`: 23 files, +2 686 lines; `008dbf4`: 7 files, +731). | The guide asks for steady work across 9 weeks, "not a final-day bulk upload". | Do not rewrite history. Present the real timeline in 2.3 and be ready to explain the 30 Sep commit piece by piece. |
| 2 | Early commit messages are `update1` … `update6`, `update 10`. | A viva question can ask what one of them changed. | Section 2.3 below lists what each actually contained. Learn them. |
| 3 | The "Reviewed by" column. Every PR merge commit in the log is authored by you (#1, #2, #3, #4, #6–#12), and #6–#12 are `IT24101153/main` → `main`. I could not see GitHub reviews (`gh` is not installed here). | The guide's table has a "Reviewed by" column and asks for "PRs with reviews". | Open each PR on GitHub and record the real reviewer. If a PR had no review, write "not reviewed" rather than a name. |
| 4 | No real agent run is captured anywhere in the repo. The JSON in `component-a-agent.md` §3.2 is labelled illustrative, and §11.2 (live Gemini evaluation) is still an empty table. | Checklist: "Agent section has a real JSON input/output and a run trace." | Run one analysis and copy the row out of `agent_runs` (query in 2.2.5). |
| 5 | No requirement IDs (`FR-A1` …) exist anywhere in the repo. | The test case table must link each case to a requirement ID. | Take the IDs from the group report's requirements section; the table in section 3 uses placeholders. |
| 6 | Stale comments in your own code (list in section 8). | "Anything you cannot explain … may get reduced or zero marks." A comment that contradicts the code is an easy viva trap. | Fix the comments (five small edits) or know the true behaviour. |
| 7 | Test gaps against the guide's table: no React tests for form validation / protected routes / `ReportDecision`; no Flutter widget, form-validation or navigation tests for Component A screens; the 5 PostgreSQL tests are skipped unless `TEST_POSTGRES` is set. | Section 3 of the guide lists each of these. | Either add a few before submission or state the gap plainly in 2.3. |
| 8 | The group outline says Component A has 19 endpoints; the code has 20. | Inconsistent numbers between group and individual sections. | Use 20 and correct [group-report-outline.md](group-report-outline.md) §2.3. |

---

## 2.1 Contribution statement (about half a page)

Facts to build it from — all **[verified]**:

- **Component:** A — Incident & Disaster Map. **Agent:** Incident Analysis Agent (coordinator + Planner,
  Evidence, Severity, Validator roles).
- **Backend:** 4 controllers, 20 endpoints; 5 services (`IncidentService`, `AgentRunService`,
  `SafetyZoneService`, `ImageStorageService`, `NotificationService`) plus two background queues/workers
  (`IncidentAnalysisQueue`/`Worker`, `NotificationQueue`/`Worker`). About 3 900 lines under
  `Features/ComponentA`.
- **Database:** entities `Incident`, `IncidentImage`, `SafetyZone`; the shared `AgentRun` table; migrations
  `ComponentA_IncidentsAndZones` (9 Sep), `AgentRuns` (9 Sep), `EmailNotifications` (21 Sep),
  `AddAgentRunPlanAndDecision` (30 Sep).
- **React:** `DisasterDashboard` with five tabs (Overview, Disaster map, Safety zones, Incident queue, Agent
  activity) and a detail drawer with `ReportDecision` (approve / reject / revise / re-run / delete).
- **Flutter:** `ReportScreen` (camera or gallery photo, GPS, map pin picker, form validation, "My reports"
  with a review timeline), `DisasterMapScreen` (live map, zones, severity filter, place search, "am I in a
  zone" check), `IncidentSheet` (detail with the AI rationale).
- **Tests:** 12 backend test files for Component A (120 test cases, listed in section 3), 4 React tests,
  3 Flutter tests on the Component A API client, a load-test script and its results.
- **Shared work you also did** (from the Git log — mention briefly, it is not Component A): login /
  forgot-password flow, Dockerfile and Azure deployment workflow, the mobile shared UI kit, and merging
  Components B, C and D into the `Features/Component*` folder structure (23 Sep).
- **Group report sections you wrote:** **[you]** — list them. The repo shows you authored ADRs 0001–0004 and
  [group-report-outline.md](group-report-outline.md).

---

## 2.2 Owned component and technical work (3–5 pages)

### 2.2.1 ASP.NET Core API (10 marks)

**Endpoints [verified] — 20.** Source: [Controllers/](../backend/RescueSriLanka.Api/Features/ComponentA/Controllers/).

| # | Method | Route | Auth | Success | Failure codes | Purpose |
|--:|---|---|---|---|---|---|
| 1 | GET | `/api/incidents` | Anonymous | 200 + `X-Total-Count` | 400 (page < 1, pageSize not 1–100) | Filter (`status`, `severity`, `type`, `district`, `activeOnly`), sort (`sortBy`, `sortDir`), page |
| 2 | GET | `/api/incidents/mine` | Signed in | 200 | 401 | The caller's own reports, any status, newest first |
| 3 | GET | `/api/incidents/{id}` | Anonymous | 200 | 404 (also for a report the viewer may not see) | One incident |
| 4 | GET | `/api/incidents/nearby` | Anonymous | 200 | 400 (lat/lng range, radius not 0–500 km) | Geo query, with `distanceKm` |
| 5 | GET | `/api/incidents/statistics` | Anonymous | 200 | — | Dashboard counts and breakdowns |
| 6 | POST | `/api/incidents` | Signed in | 201 + Location | 400 (model validation), 401 | File a report |
| 7 | POST | `/api/incidents/with-photo` | Signed in | 201 | 400 (bad file), 401 | Report + photo in one multipart request, 10 MB limit |
| 8 | PATCH | `/api/incidents/{id}/status` | EmergencyCoordinator | 200 | 401, 403, 404 | Verify / reject / resolve |
| 9 | PATCH | `/api/incidents/{id}/severity` | EmergencyCoordinator | 200 | 401, 403, 404 | Manual severity override, recorded |
| 10 | DELETE | `/api/incidents/{id}` | EmergencyCoordinator | 204 | 403, 404 | Remove a duplicate or spam report |
| 11 | POST | `/api/incidents/{id}/images` | Signed in | 201 | 400, 404, 502 (storage backend failed) | Attach another photo |
| 12 | POST | `/api/incidents/{id}/analyse` | EmergencyCoordinator | 200 | 404, 429 (rate limit), 502 (run failed, recorded) | Run the agent by hand |
| 13 | GET | `/api/agentruns` | Signed in | 200 | 401 | Run history with plan, tool calls, output |
| 14 | POST | `/api/agentruns/{id}/approve` | EmergencyCoordinator | 200 | 404, 409 | Approve, or revise by sending a severity |
| 15 | POST | `/api/agentruns/{id}/reject` | EmergencyCoordinator | 200 | 400 (reason required), 404, 409 | Reject; incident untouched |
| 16 | GET | `/api/safetyzones` | Anonymous | 200 | — | Active zones |
| 17 | GET | `/api/safetyzones/check` | Anonymous | 200 | 400 | Point-in-zone check; worst zone wins |
| 18 | POST | `/api/safetyzones/recompute` | EmergencyCoordinator | 200 | 403 | Rebuild derived zones |
| 19 | POST | `/api/notifications/test` | EmergencyCoordinator | 200 | 400 (bad address), 401 | Send a sample district warning |
| 20 | GET | `/api/notifications/inbox` | EmergencyCoordinator | 200 | 400, 503 (testmail not configured) | Read the test inbox |

**DTOs [verified]** — [IncidentDtos.cs](../backend/RescueSriLanka.Api/Features/ComponentA/DTOs/IncidentDtos.cs),
[AgentRunDtos.cs](../backend/RescueSriLanka.Api/Features/ComponentA/DTOs/AgentRunDtos.cs),
[SafetyZoneDtos.cs](../backend/RescueSriLanka.Api/Features/ComponentA/DTOs/SafetyZoneDtos.cs).
Entities never leave the API: each response DTO has a static `FromIncident` / `FromRun` / `FromZone` mapper,
and enums travel as strings (`JsonStringEnumConverter` in `Program.cs`).

**Validation [verified]** — data annotations on `CreateIncidentRequest`:

| Field | Rule |
|---|---|
| `Title` | Required, max 200 |
| `Description` | Required, max 4 000 |
| `Type` | Required enum |
| `Latitude` / `Longitude` | Range −90…90 / −180…180 |
| `AffectedRadiusMeters` | Range 50…50 000, default 1 000 |
| `District` / `AddressText` | Max 100 / 300 |
| `EstimatedAffectedPeople` | Range 0…1 000 000 |
| Photo (`ImageStorageService.Validate`) | Not empty, ≤ 8 MB, `image/jpeg`, `png`, `webp` or `heic` |
| `RejectAgentRunRequest.Reason` | Required, max 500 |

**Service layer and async [verified]** — controllers hold no data access; every service method is `async`
and takes a `CancellationToken`. Read queries use `AsNoTracking()`. Constructor injection throughout,
registered as scoped in `Program.cs`.

**Business operations beyond CRUD [verified]** — pick two or three to show in depth:

1. **Human approval gate** — [AgentRunService.cs:46-116](../backend/RescueSriLanka.Api/Features/ComponentA/Services/AgentRunService.cs#L46-L116).
   One decision per proposal, newest run only, one transaction for decision + incident + zone recompute,
   warning email queued only after commit.
2. **Derived safety zones** — [SafetyZoneService.cs:78-136](../backend/RescueSriLanka.Api/Features/ComponentA/Services/SafetyZoneService.cs#L78-L136).
   Rebuilt from active, approved incidents; manual zones are never touched; an unreviewed report draws no zone.
3. **Geo query** — [IncidentService.cs:185-211](../backend/RescueSriLanka.Api/Features/ComponentA/Services/IncidentService.cs#L185-L211).
   Bounding box in SQL, exact haversine distance in memory ([GeoService.cs](../backend/RescueSriLanka.Api/Services/GeoService.cs)).
4. **Visibility rule** — `GetForViewerAsync`: staff and the reporter see a report in any state; everyone
   else only once approved. A hidden report answers 404, the same as a missing one.
5. **Report with photo ordering** — the photo is stored *before* the analysis is queued, so the agent sees it
   (`CreateWithPhotoAsync`). The report survives a storage failure and returns `photoError`.
6. **Opt-in paging** — sorting and paging apply only when asked for, so the map and Component D's callers,
   which send no paging parameters, were not broken.

**Snippet candidate (15 lines)** — the one-decision rule:

```csharp
private async Task EnsureAwaitingDecisionAsync(AgentRun run, CancellationToken ct)
{
    if (run.Status is not (AgentRunStatus.Succeeded or AgentRunStatus.SucceededWithFallback))
        throw new InvalidOperationException($"This run has no proposal to decide on (status: {run.Status}).");

    if (run.Decision != AgentRunDecision.Pending || run.ApprovedAt is not null)
        throw new InvalidOperationException(run.Decision == AgentRunDecision.Rejected
            ? "This proposal has already been rejected."
            : "This proposal has already been approved.");

    if (run.IncidentId is Guid incidentId &&
        await db.AgentRuns.AnyAsync(other =>
            other.IncidentId == incidentId && other.Id != run.Id &&
            other.StartedAt > run.StartedAt && other.Status != AgentRunStatus.Failed, ct))
        throw new InvalidOperationException("A newer analysis exists for this incident; review that proposal instead.");
}
```

(Condensed from [AgentRunService.cs:146-172](../backend/RescueSriLanka.Api/Features/ComponentA/Services/AgentRunService.cs#L146-L172); the controller turns the exception into 409.)

**[check]**
- `PATCH /status` accepts any status from any status — there is no transition table. `Rejected → Verified`
  is deliberate (tested in `IncidentStatusRollbackTests`), but nothing stops `Resolved → Reported`.
- `POST /{id}/images` lets any signed-in user attach a photo to any incident, including one they did not
  file and cannot see.
- `GET /statistics` is anonymous and its counts include reports still under review.
- `GetStatisticsAsync` loads every incident into memory to count them. Fine at this size; say so if asked.

### 2.2.2 PostgreSQL and data modelling (10 marks)

**Entities [verified]** — [Models/](../backend/RescueSriLanka.Api/Features/ComponentA/Models/), mapping in
[AppDbContext.cs:85-142](../backend/RescueSriLanka.Api/Data/AppDbContext.cs#L85-L142).

| Table | Key columns | Notes |
|---|---|---|
| `incidents` | `Id` uuid PK; `Title`(200), `Description`(4000); `Type`, `Severity`, `Status` stored as text(40); `Latitude`, `Longitude`, `AffectedRadiusMeters`; `District`(100); `Ai*` proposal fields kept apart from `Severity`; `SeverityOverriddenBy/At`; `ReportedByUserId`, `VerifiedByUserId/At`; `DistrictWarningSentAt`; `IsActive`; `CreatedAt`, `UpdatedAt` | Audit fields present |
| `incident_images` | `Id` PK; `IncidentId` FK; `StoragePath`(500); `ContentType`, `SizeBytes`, `Caption`; `UploadedByUserId`, `UploadedAt` | |
| `safety_zones` | `Id` PK; `Name`; `Status`, `Source` as text; centre + `RadiusMeters`; `SourceIncidentId` FK nullable; `IsActive`; `ComputedAt`, `ExpiresAt` | |
| `agent_runs` (shared) | `Id` PK; `AgentName`, `Objective`, `IncidentId`; `Status`; `Model`, `ModelAttempts`; `InputJson`, `PlanJson`, `ToolCallsJson`, `OutputJson`; `ErrorMessage`; `UsedFallback`; `Decision` (default `Pending`), `DecisionNote`; `Approved`, `ApprovedByUserId`, `ApprovedAt`; `DurationMs`, `StartedAt`, `CompletedAt` | `ApprovedAt` is a concurrency token |

**Relationships [verified]**
- `Incident` 1 — * `IncidentImage`, cascade delete.
- `Incident` 1 — * `SafetyZone` through `SourceIncidentId`, cascade delete (a derived zone disappears with
  its incident).
- Component B's `HelpRequest.RelatedIncidentId` → `incidents`, `SET NULL` on delete.
- `agent_runs.IncidentId` is a plain uuid with **no foreign key**, so `IncidentService.DeleteAsync` removes
  an incident's runs by hand.

**Indexes [verified]** — `incidents`: `Status`, `Severity`, `IsActive`, `District`, `(Latitude, Longitude)`.
`safety_zones`: `Status`, `IsActive`, `SourceIncidentId`. `incident_images`: `IncidentId`.
`agent_runs`: `AgentName`, `IncidentId`, `StartedAt`. `users`: `District` (district warnings ask "who lives
here?").

**Migrations [verified]**

| Migration | Date | What it did |
|---|---|---|
| `20260909130321_ComponentA_IncidentsAndZones` | 9 Sep | The three tables, both FKs, nine indexes |
| `20260909135553_AgentRuns` | 9 Sep | `agent_runs` and its three indexes |
| `20260921083709_EmailNotifications` | 21 Sep | `users.District`, `users.EmailNotificationsEnabled`, `incidents.DistrictWarningSentAt`. **Hand-edited**: the generated default `false` was changed to `true` so existing accounts were not silently opted out — a good migration to explain in the viva |
| `20260930144845_AddAgentRunPlanAndDecision` | 30 Sep | `Decision`, `DecisionNote`, `ModelAttempts`, `PlanJson`, plus two `UPDATE` statements that backfill `Decision` for runs decided before the column existed |

**Transaction and concurrency [verified]** — in `ApproveAsync`: `BeginTransactionAsync` (only when the
provider is relational), then the decision, the incident change and `RecomputeAsync` commit together.
`ApprovedAt` is configured with `IsConcurrencyToken()`, so EF Core writes
`UPDATE … WHERE "Id" = @id AND "ApprovedAt" IS NULL`; a second coordinator's update matches no row, EF throws
`DbUpdateConcurrencyException`, and the service turns it into "decided by someone else a moment ago" (409).

**[check]**
- `incidents.ReportedByUserId`, `VerifiedByUserId` and `SeverityOverriddenBy` are plain uuids with **no
  foreign key to `users`** (see the migration). The ER diagram in the group outline draws
  `USER ||--o{ INCIDENT` and `USER ||--o{ AGENT_RUN` as relationships — either show them as soft references
  or be ready to say why there is no FK.
- **No CHECK constraints on Component A tables.** Coordinate and radius ranges are enforced only by DTO
  validation. Component B has `CK_HelpRequests_Latitude` and similar; yours does not. The guide's database
  evidence asks for a "constraint violation test" — the only database-enforced rules you can demonstrate are
  NOT NULL, the two FKs and the concurrency token.
- `SafetyZone` and `IncidentImage` have `ComputedAt` / `UploadedAt` but not `CreatedAt` / `UpdatedAt`. Only
  `Incident` has the pair the rubric names.
- `SafetyZone.ExpiresAt` and `ZoneSource.ManualOverride` exist in the model, but no endpoint creates a manual
  zone or expires one.

### 2.2.3 React web app (10 marks)

Source: [frontend/src/components/componentA/](../frontend/src/components/componentA/).

| Rubric item | What exists [verified] |
|---|---|
| Screens | `DisasterDashboard.tsx` with tabs: `OverviewSection` (stat row, distribution, activity feed, coverage), `MapSection` + `IncidentMap` (react-leaflet, markers sized and coloured by severity, zone circles, zone toggle), `ZonesSection`, `IncidentTable` (queue), `AgentActivity` (run log with expandable Plan column). Detail drawer: facts, `IncidentPhotos`, `ReportDecision` |
| Routes | `react-router-dom` v7. `ConsoleShell.tsx` maps a role to its pages; `EmergencyCoordinator` gets `/` → `DisasterDashboard`; any unknown path redirects to the role's first page |
| Role guards | `pagesFor(role)` returns `null` for a role with no console page ("never fall back to an admin dashboard"); `LoginPage` refuses a Citizen account; `App.tsx` clears a stored Citizen session; the delete button shows only for `EmergencyCoordinator`. The server enforces the same roles — the UI guard is a convenience, not the control |
| State management | Plain `useState` / `useEffect`, no store. Two fetch effects in the dashboard (whole set for map/overview; paged set for the queue), each with an `AbortController` so a slow response for old filters cannot overwrite newer results. A `reloadToken` counter triggers refetch |
| Validation | Login form: required email and password with `aria-invalid` and per-field messages. Reject and delete need a confirmation step. The server's 409 message is shown as-is when a decision is refused |
| Loading / empty / error | "Refreshing…", "Loading…"; "No incidents match the current filters.", "No agent runs recorded yet."; `role="alert"` banners; `ReportDecision` polls every 4 s (max 30 polls) while the agent is still running |
| Server-side sort and paging | Sortable column headers with `aria-sort`; `apiFetchPage` reads `X-Total-Count`; Previous / Next with "Page N of M"; 20 rows per page |
| Accessibility | Severity is never colour-only: a text chip, and marker size on the map (`severity.ts`) |

`ReportDecision` splits the coordinator's job into two questions kept separate on purpose: *is the report
true?* (`PATCH /status`) and *is the AI's level right?* (`POST /agentruns/{id}/approve`, optionally with a
different severity).

**[check]** There is no create/edit form for incidents in React (reports are filed from Flutter), so "form
validation" evidence on the React side is the login form and the confirm steps only.

**Screenshots [you]** — Overview tab; Disaster map with zones on; Incident queue sorted and on page 2;
drawer showing the AI proposal before approval; the same drawer after approval; a 409 message after a second
approval; Agent activity with a Plan expanded; an empty state; an error banner with the API stopped.

### 2.2.4 Flutter mobile app (10 marks)

Source: [mobile/lib/features/component_a/](../mobile/lib/features/component_a/).

| Rubric item | What exists [verified] |
|---|---|
| Screens | `ReportScreen` (form + "My reports" switcher), `LocationPickerScreen` (fixed centre pin, map moves under it, place search), `MyReportsView` (cards with a four-step progress track, detail with a timeline), `DisasterMapScreen` (flutter_map, zones, severity filter chips with counts, legend, draggable list sheet, place search with debounce), `IncidentSheet` (detail, AI score and rationale read-only) |
| State management | `StatefulWidget` + `setState`; `AuthService extends ChangeNotifier` is the shared session, and `HomeShell` rebuilds its tab bar from it (2 tabs signed out; Report, Help, Resources, Rescue added when signed in) |
| Form validation | `Form` + `GlobalKey<FormState>`. Title: required, at least 8 characters, max 200. Description: required, minimum length, max 4 000. People affected: optional, must be a number. Address: max 300. A location must be set before submit |
| Device features | **GPS** via `geolocator` (service-enabled check, permission request, denied and denied-forever handled) in both the report form and the map's "locate me". **Camera and gallery** via `image_picker` (`ImageSource.camera` / `.gallery`) |
| Status tracking and history | "My reports" loads `GET /api/incidents/mine`; a copy is cached per user in `SharedPreferences` (`MyReportsStore`, 50 items) so the list shows instantly and offline; pull-to-refresh |
| Location sources | Three: phone GPS, the district on the profile, or another district / a pin on the map |
| Design decision worth stating | The form deliberately does not ask for severity — `createIncident` does not send it. Grading is the agent's job and the coordinator's decision |
| Errors | `ApiException` carries the API's message; 401 clears the session; a `photoError` from the server is shown as "report filed, photo not stored" |

**[check]** No Flutter test targets these screens (see section 3).

**Screenshots [you]** — report form empty with validation messages; GPS permission prompt; camera/gallery
choice; submitted sheet; My reports with a report "under review"; the same report after approval; the map
with zones and the zone banner after "locate me"; the incident sheet with the AI rationale.

### 2.2.5 Individual Agentic AI (12 marks)

Full write-up: [component-a-agent.md](component-a-agent.md). Source:
[Agents/IncidentAnalysisAgent/](../backend/RescueSriLanka.Api/Features/ComponentA/Agents/IncidentAnalysisAgent/).

**Responsibility [verified]** — propose a severity (Low/Moderate/High/Critical, score 0–100, confidence
0–1), a zone status (Safe/Caution/Danger) and radius, with a short rationale. It only proposes: it writes the
incident's `Ai*` fields, never `Severity` or `AffectedRadiusMeters`.

**Roles [verified]** — `IncidentAnalysisAgent` coordinates four classes in
[IncidentAnalysisRoles.cs](../backend/RescueSriLanka.Api/Features/ComponentA/Agents/IncidentAnalysisAgent/IncidentAnalysisRoles.cs):

| Role | Does | May touch |
|---|---|---|
| `AnalysisPlannerAgent` | Builds the ordered plan; rainfall only for Flood/Landslide/Storm/Tsunami, photos only if the report has any; records each omission in `notes`; refuses invalid coordinates | Nothing — no model, so report text cannot steer it |
| `EvidenceGatheringAgent` | Runs only the tools the plan names; records the rest as `{ skipped: true }` | The three tools |
| `SeverityAnalysisAgent` | Gemini with a JSON response schema, or `SeverityRules` when the model is unconfigured or unusable | The model or rule engine; no tools, no database |
| `ProposalValidationAgent` | Clamps score 0–100, confidence 0–1, radius 100–20 000 and records each clamp; rejects an empty rationale | Nothing — pure function |

**Honest framing [check]** — three of the four are deterministic code and only the Severity step calls a
model. `component-a-agent.md` §12 already says this. Describe it as one agent with a plan-and-delegate
pipeline rather than as four independent LLM agents.

**Allow-listed tools [verified]**

| Tool | Returns | Guard | On failure |
|---|---|---|---|
| `count_nearby_active_incidents` | Other active incidents within 5 km | Coordinates validated; radius clamped 0.5–50 km; excludes the incident itself | Throws on invalid coordinates (the Planner refuses these first) |
| `get_rainfall_last_48h` | mm of rain from Open-Meteo (no key) | Coordinates validated before any request; 5 s timeout; null hours skipped | Returns `null`; run continues |
| `load_incident_images` | Up to 3 photos, 6 MB total | Count and size caps | Fewer or no photos |

**Input/output contract** — shapes are **[verified]** from
[IncidentAnalysisContracts.cs](../backend/RescueSriLanka.Api/Features/ComponentA/Agents/IncidentAnalysisAgent/IncidentAnalysisContracts.cs);
the values below are placeholders. **[you]** Replace with a real `InputJson` and `OutputJson`.

```jsonc
// InputJson — IncidentAnalysisInput (PascalCase, enums as names)
{ "IncidentId": "…", "Title": "…", "Description": "…", "Type": "Flood",
  "Latitude": 0.0, "Longitude": 0.0, "District": "…",
  "EstimatedAffectedPeople": 0, "ImageCount": 0 }

// OutputJson — IncidentAnalysisResult (camelCase)
{ "severity": "High", "severityScore": 0, "confidence": 0.0,
  "recommendedZoneStatus": "Danger", "recommendedRadiusMeters": 0, "rationale": "…" }
```

The reporter's identity is not part of the input.

**How to capture a real run and trace [you]**

1. Start the API with your Gemini key, file a report from the Flutter app (or `POST /api/incidents`).
2. Read the run back:

```sql
SELECT r."Status", r."Model", r."ModelAttempts", r."UsedFallback", r."DurationMs",
       r."InputJson", r."PlanJson", r."ToolCallsJson", r."OutputJson",
       r."Decision", r."DecisionNote", r."StartedAt", r."CompletedAt"
FROM agent_runs r
WHERE r."AgentName" = 'IncidentAnalysisAgent'
ORDER BY r."StartedAt" DESC LIMIT 1;
```

3. Paste `InputJson` and `OutputJson` as the contract example and `PlanJson` + `ToolCallsJson` as the run
   trace. Approve it in React and re-run the query to show `Decision` change.
4. Do the same once with the key removed, to show a `SucceededWithFallback` run (`Model = rule-engine`).
5. Fill the three-row table in `component-a-agent.md` §11.2 from these runs.

**Run states [verified]** — `Running` → `Succeeded` | `SucceededWithFallback` | `Failed`. A model failure is
recovered by the rule engine and the reason stored in `ErrorMessage`. Any other exception marks the run
`Failed`, resets the incident entity to unchanged, saves the failed step in `PlanJson`, and rethrows; the
manual endpoint answers 502.

**Rule engine [verified]** — `SeverityRules.Score`: hazard type 12–40, people 5–30 (8 if not reported),
nearby incidents 0–20, rainfall 0–10 (Flood and Landslide only). ≥ 75 Critical, ≥ 55 High, ≥ 32 Moderate,
else Low. Confidence is always 0.55 and `UsedFallback = true`.

**Security [verified]**

| Control | Where |
|---|---|
| Fixed system instruction; citizen text enters only as labelled data in the prompt | `SeverityAnalysisAgent.BuildPrompt` |
| Deterministic plan — text cannot change which tools run | `AnalysisPlannerAgent` |
| Schema-constrained output, strict enum parsing, clamping, empty-rationale rejection | `ResponseSchema`, `ProposalValidationAgent` |
| A fooled model can still only produce a proposal | `AgentRunService` approval gate |
| API key from configuration, sent in the `x-goog-api-key` header, not the URL | `GoogleAiClient` |
| Timeouts: Gemini 30 s, Open-Meteo 5 s. Retries: up to 3 (configurable 1–5) on 429/500/502/503/504, backoff 600 ms then 1 200 ms | `Program.cs`, `GoogleAiClient` |
| `analyse` rate limited: 10 per minute per user (`ai` policy) | `Program.cs` |
| Roles: analyse, approve, reject need `EmergencyCoordinator` | controllers |

**Place in the workflow [verified]** — report saved → photo stored → `IncidentAnalysisQueue` (bounded 200,
drop-oldest) → `IncidentAnalysisWorker` runs the agent in its own DI scope → proposal in `agent_runs` and the
incident's `Ai*` fields → coordinator decides in React → severity, radius and zones change → Flutter map and
"My reports" show it → district warning email if the severity clears the threshold.

**Model [verified]** — default `gemini-3-flash-preview`, overridable with `GoogleAi:Model`; temperature 0.2;
max output tokens 2 048 (clamped 512–8 192); thinking budget 128. Choice justified in
[ADR 0001](adr/0001-llm-provider.md) as a deviation from the proposal's Ollama plan.

### 2.2.6 API integration, security, cross-platform (10 marks)

**Who calls what [verified]**

| Endpoint | React | Flutter |
|---|:-:|:-:|
| `GET /api/incidents` | ✓ (map set, and paged queue) | ✓ `fetchIncidents` |
| `GET /api/incidents/{id}` | ✓ after a decision | ✓ `fetchIncident` |
| `GET /api/incidents/mine` | | ✓ `fetchMyReports` |
| `GET /api/incidents/nearby` | | ✓ `fetchNearby` |
| `GET /api/incidents/statistics` | ✓ | |
| `POST /api/incidents`, `/with-photo` | | ✓ |
| `PATCH /status`, `PATCH /severity`, `DELETE`, `POST /analyse` | ✓ | |
| `GET /api/agentruns`, `approve`, `reject` | ✓ | |
| `GET /api/safetyzones` | ✓ | ✓ `fetchZones` |
| `GET /api/safetyzones/check` | | ✓ `checkZone` |

**JWT and roles [verified]** — `AddJwtBearer` validates issuer, audience, lifetime and signing key (HMAC,
1-minute clock skew). Controllers are `[Authorize]` by default with `[AllowAnonymous]` on the public reads
and `[Authorize(Roles = "EmergencyCoordinator")]` on coordinator actions. `IsStaff()` (authenticated and not
a Citizen) decides whether unapproved reports are included.

**Token storage [verified]** — React: `localStorage` when "Keep me signed in" is ticked, otherwise
`sessionStorage`; an expired token is dropped on read; a 401 clears the session. Flutter:
`flutter_secure_storage`, with a one-time migration of an older `SharedPreferences` copy; a 401 calls
`handleUnauthorized()` and signs out.

**[check]** Web storage is readable by any script on the page. If asked about "secure token storage", the
honest answer is that Flutter uses the platform keystore and React does not use an httpOnly cookie.

**Other controls [verified]** — CORS: any localhost origin in Development, an explicit `Cors:AllowedOrigins`
list otherwise, with `X-Total-Count` exposed. `GlobalExceptionHandler` returns `ProblemDetails` and hides the
exception message outside Development. Rate limits: `auth` 10/min per IP, `ai` 10/min per user. Secrets come
from configuration; `appsettings.Development.json` is git-ignored and the committed `appsettings.json` holds
no keys.

**Your step in the Flutter → API → agents → React → Flutter flow [verified in code; [you] screenshot it]**

1. Flutter `ReportScreen` → `POST /api/incidents/with-photo` (bearer token).
2. API validates, saves the incident (`Status = Reported`), stores the photo, queues analysis and a receipt email.
3. Worker runs the agent; proposal saved to `agent_runs` and `incidents.Ai*`.
4. React drawer shows the proposal; coordinator marks the report true and approves or revises the level.
5. One transaction: decision + severity + radius + zone recompute. District warning queued after commit.
6. Flutter "My reports" timeline moves on; the map shows the incident and its zone.

**[check]** The comment on `GET /api/safetyzones/check` says it is consumed by Student B's travel advisory.
It is not — only your own Flutter client calls it. Do not claim a cross-component API integration.

---

## 2.3 Key commit, pull request and test evidence

Repository: `https://github.com/IT24101153/RescueSriLanka-SEF_KDY_SE_02`. Commit link form:
`…/commit/<hash>`; PR link form: `…/pull/<n>`.

**Commits [verified from `git log`]** — "Reviewed by" is **[you]** throughout.

| Date | Commit / PR | What it did | Reviewed by |
|---|---|---|---|
| 2026-09-09 | `7a7be50` … `d4d7950` (`update1`–`update3`) | Project start; `InitialAuth`, `ComponentA_IncidentsAndZones` and `AgentRuns` migrations are dated this day | |
| 2026-09-16 | PR #1 (`feature/component-a-incident-map` → main, merge `2cb652d`) | First Component A merge | |
| 2026-09-18 | `078b560` `update5` | 5 files, +762 lines under Component A paths | |
| 2026-09-18 | `b93d73a` `update6` | 1 file, +22 / −9 | |
| 2026-09-21 | `5132bd6` `update 10` | 2 files, +400; `EmailNotifications` migration is dated this day | |
| 2026-09-22 | PR #4 (merge `f7808ad`) | Second merge of the Component A branch | |
| 2026-09-22 | `039d78e` | Restructured code into `Features/ComponentA` / `ComponentB` folders (54 files) | |
| 2026-09-23 | `ba44c97`, `685ae0c` | Mobile: tabs gated on sign-in, OpenStreetMap tiles, place search on the map | |
| 2026-09-23 | PR #6 (merge `8e227af`), `d7e5b89` | Merge of Components C and D into the shared structure | |
| 2026-09-24 | `0605d72` | Backend fixes and Dockerfile | |
| 2026-09-27 | `d5b3e94` | Disaster dashboard improvements | |
| 2026-09-28 | `de3ef48` | Pagination and sorting on `GET /api/incidents`; `GlobalExceptionHandler` | |
| 2026-09-29 | `a555295`, `aa96936`, `ca678f2` | Component A polish (20 files, +4 460 / −831), map update | |
| 2026-09-30 | `7c593ed` | Plan-and-delegate agent (`IncidentAnalysisRoles.cs`), one-decision approval guard, concurrency token, `AddAgentRunPlanAndDecision` migration, React Plan column and its tests | |
| 2026-10-01 | `008dbf4` | `PostgresIntegrationTests`, PostgreSQL service in CI, load-test script and performance results | |

For each `updateN` commit, run `git show --stat <hash>` and write one true sentence about what changed.

**Branch [verified]** — `feature/component-a-incident-map` exists on the remote with 10 commits (9–21 Sep).
After 22 Sep the work went straight to `main`.

**[you]** — issues you opened or closed; any merge conflict you resolved (candidates: `0c03141`, `6584ae5`,
`e0f54bb`, the three "Merge origin/main into …" commits on 22–23 Sep; and PR #3, a Copilot-authored revert
of Component A files that had been merged into the Component B branch by mistake); screenshot of a passing
GitHub Actions run.

**CI [verified]** — [.github/workflows/ci.yml](../.github/workflows/ci.yml): three jobs on every push and on
PRs to `main`. Backend: restore, build (Release), `dotnet test` against a `postgres:16` service with
`TEST_POSTGRES` set, upload `.trx`. Web: `npm ci`, lint (oxlint), `npm test`, `tsc -b && vite build`.
Mobile: `flutter pub get`, `flutter analyze`, `flutter test`.

**Test runs on 1 Oct 2026 [verified — I ran these]**

| Suite | Command | Result |
|---|---|---|
| Backend (whole solution) | `dotnet test RescueSriLanka.slnx` | 375 passed, 0 failed, 5 skipped (the PostgreSQL tests; `TEST_POSTGRES` was not set) |
| React (whole app) | `npm test` | 27 passed, 3 files |
| Flutter (whole app) | `flutter test` | 65 passed |

These are whole-repository totals, not Component A's alone. **[you]** Re-run them yourself and screenshot
your own output; also run the PostgreSQL tests locally so the screenshot shows 380 passing:

```bash
TEST_POSTGRES="Host=localhost;Username=$USER;Database=postgres" \
  dotnet test RescueSriLanka.slnx --filter PostgresIntegration
```

---

## 3. Testing your component (guide section 3)

### Coverage against the guide's table

| Area | What exists [verified] | Gap [check] |
|---|---|---|
| Backend | `IncidentAnalysisAgentTests` 27, `AgentRunServiceTests` 19, `NotificationServiceTests` 21, `SafetyZoneServiceTests` 14, `SeverityRulesTests` 11, `GeoServiceTests` 6, `IncidentReadTests` 5, `IncidentVisibilityTests` 4, `ReportWithPhotoTests` 3, `GlobalExceptionHandlerTests` 3, `IncidentStatusRollbackTests` 2 — 115 cases, EF Core in-memory provider | `AuthorizationIntegrationTests` exercises Component D's endpoints, not yours. No test shows a Citizen getting 403 on `PATCH /status` or `POST /approve` |
| Database | `PostgresIntegrationTests` 5, on real PostgreSQL 16, one throwaway database each: every migration applies and the model has no pending changes; decision backfill; two coordinators approving at once (10 rounds) → one winner, one warning; stale write rejected by the concurrency token; failed zone recompute rolls the approval back | Skipped locally unless `TEST_POSTGRES` is set. No CHECK-constraint violation test, because there are no CHECK constraints |
| React | `AgentActivity.test.tsx` 4 (Vitest + React Testing Library, API mocked with `vi.mock`) | Nothing for `DisasterDashboard`, `IncidentTable`, `ReportDecision`, protected routes or the login form. MSW is not used |
| Flutter | `auth_test.dart`, group "ApiClient writes" 3: refuses to send without a session; attaches the bearer token and omits blank optional fields; a rejected token clears the session | No widget, form-validation or navigation test for `ReportScreen`, `DisasterMapScreen` or `MyReportsView` |
| End to end | Not recorded | **[you]** manual run with numbered screenshots (steps in 2.2.6) |
| Performance | [component-a-performance.md](component-a-performance.md), produced by `tools/perf/load_test.py` on 1 Oct | Local machine, Gemini switched off — the model path is not measured. A custom Python script, not k6/JMeter/NBomber (those are "suggested" tools) |
| Agent | Golden cases and deterministic checks with a scripted `FakeLlm` — no LLM-as-judge | Live Gemini table empty |

### Performance headline figures [verified, from the 1 Oct run]

- 100 % success on every endpoint at 1, 10 and 50 concurrent (about 5 700 requests).
- Reads at 50 concurrent: p95 between 7.7 ms and 14.0 ms (`/health` 37.4 ms).
- `POST /api/incidents` at 50 concurrent: p95 72.1 ms, p99 79.1 ms.
- Agent latency, rule-engine path with live Open-Meteo, 6 sequential runs: median 242 ms, max 1 023 ms.

Use the full table in [perf-results.md](perf-results.md) and keep the caveats from the performance doc §4.

### Test case table — draft rows

Requirement IDs are placeholders **[you]**. "Actual" is from the 1 Oct run, where every listed test passed
(the PostgreSQL rows passed on 30 Sep per the earlier doc and were skipped in my run — re-run them).

| Test ID | Req. | Description | Input / steps | Expected | Actual | Status |
|---|---|---|---|---|---|---|
| TC-A01 | FR-A? | Golden case: valid model answer is stored | `GoldenCase_ValidModelAnswer_IsPersistedAsSucceededRun` — scripted model returns a valid proposal | Run `Succeeded`, output stored, `Ai*` fields set | As expected | Pass |
| TC-A02 | FR-A? | Agent only proposes | `GoldenCase_AgentProposesOnly_SeverityInForceIsUnchanged` | `Severity` in force unchanged | As expected | Pass |
| TC-A03 | FR-A? | Plan is persisted and delegated | `Run_PersistsAStructuredPlan_DelegatedToThreeDistinctAgents` | `PlanJson` has 3 steps, 3 agents | As expected | Pass |
| TC-A04 | FR-A? | Planner adapts to the incident | `Planner_AdaptsToTheIncident_AndSaysWhatItLeftOut` — Fire report, no photos | Rainfall and photo tools omitted, notes recorded | As expected | Pass |
| TC-A05 | FR-A? | Out-of-range values clamped | `OutOfRangeModelValues_AreClamped` | Score ≤ 100, confidence ≤ 1, radius ≤ 20 000 | As expected | Pass |
| TC-A06 | FR-A? | Empty rationale rejected | `EmptyRationale_IsRejected_AndRuleEngineTakesOver` | `SucceededWithFallback`, reason recorded | As expected | Pass |
| TC-A07 | FR-A? | Prompt injection cannot change severity in force | `PromptInjection_ThatFoolsTheModel_StillCannotChangeSeverityInForce` | Proposal stored; `Severity` unchanged | As expected | Pass |
| TC-A08 | FR-A? | Model unavailable | `ModelUnavailable_FallsBackToRuleEngine_AndRecordsWhy` | Rule engine result, `ErrorMessage` set | As expected | Pass |
| TC-A09 | FR-A? | Unexpected failure is safe | `UnexpectedFailure_MarksTheRunFailed_RecordsWhy_AndTouchesNothing` | Run `Failed`, incident untouched | As expected | Pass |
| TC-A10 | FR-A? | Rainfall service down | `RainfallTool_ReturnsNull_WhenTheServiceFailsOrAnswersBadly` (4 cases) | `null`, no exception | As expected | Pass |
| TC-A11 | FR-A? | Approve applies the proposal | `Approve_AppliesTheProposal_WithoutMarkingAnOverride` | Severity and radius applied, warning queued | As expected | Pass |
| TC-A12 | FR-A? | Second approval refused | `Approve_Twice_IsRefused_AndWarnsTheDistrictOnlyOnce` | `InvalidOperationException` (409), one email | As expected | Pass |
| TC-A13 | FR-A? | Reject leaves the incident alone | `Reject_LeavesTheIncidentUntouched_AndRecordsTheReason` | No change, reason in `DecisionNote` | As expected | Pass |
| TC-A14 | FR-A? | Older run cannot be decided | `AnOlderRun_CannotBeDecided_OnceANewerAnalysisExists` | Refused | As expected | Pass |
| TC-A15 | FR-A? | Unreviewed report draws no zone | `Recompute_IgnoresAReportNotYetApproved` | No zone | As expected | Pass |
| TC-A16 | FR-A? | Worst zone wins | `CheckPoint_WhereZonesOverlap_ReportsTheWorstOne` | `Danger` | As expected | Pass |
| TC-A17 | FR-A? | Unapproved report hidden from the public | `UnapprovedReport_IsHiddenFromThePublic_ButNotItsReporterOrStaff` | 404 for others; visible to reporter and staff | As expected | Pass |
| TC-A18 | FR-A? | Bad photo refused before a report exists | `AnUnacceptableFileIsRefusedBeforeAnyReportExists` | `ArgumentException` (400), no incident row | As expected | Pass |
| TC-A19 | FR-A? | District warning sent once | `Warning_IsSentOnlyOncePerIncident` | Second call sends 0 | As expected | Pass |
| TC-A20 | FR-A? | Two coordinators approve at once (PostgreSQL) | `TwoCoordinatorsApprovingAtOnce_ExactlyOneWins_AndOnlyOneWarningIsQueued` | 1 success, 1 refusal, 1 email | **[you]** re-run | |
| TC-A21 | FR-A? | Approval rolls back if zones fail (PostgreSQL) | `IfTheZoneRecomputeFails_TheWholeApprovalRollsBack` | Decision, severity, radius reverted | **[you]** re-run | |
| TC-A22 | FR-A? | React shows the plan | `AgentActivity.test.tsx` — "shows the plan, the agent each step was delegated to, and skipped tools" | Three agent names, note, attempts | As expected | Pass |
| TC-A23 | FR-A? | Flutter write needs a session | `auth_test.dart` — "refuse to send without a session" | `ApiException` 401, no request sent | As expected | Pass |

**A test that failed first [you]** — the guide requires at least one, with the fix. This must be something
that really happened to you. Things in the code that look like they came from a real failure, to jog your
memory (confirm each against your own recollection and Git history before using it):

- `ReportWithPhotoTests.ThePhotoIsStoredBeforeTheAgentIsAskedToLook` — the controller comment says filing
  the report and photo separately "let the analysis agent run before the photo arrived, so it graded the
  report blind".
- `Approve_Twice_IsRefused_AndWarnsTheDistrictOnlyOnce` — the approval guard was added on 30 Sep because a
  run could be approved twice and re-send the warning.
- `IncidentStatusRollbackTests.ApproveAfterReject_BringsTheReportAndItsZoneBack` — the service comment
  describes a rejected report being "stranded inactive with a stale ResolvedAt".
- The mutation check in `component-a-agent.md` §11.1: with the concurrency token and transaction removed,
  3 of the 5 PostgreSQL tests fail. That shows the tests can fail; it is not a "failed first" story.

### Agent evaluation checklist (guide's six bullets)

| Guide item | Test evidence [verified] |
|---|---|
| Correct tool selection | `AllowListedToolCalls_AreRecordedOnTheRun`, `ToolsThePlanLeavesOut_AreNeverCalled_AndAreRecordedAsSkipped`, `Planner_AdaptsToTheIncident_AndSaysWhatItLeftOut` |
| Structured output passes schema validation | `GoldenCase_ValidModelAnswer_IsPersistedAsSucceededRun`, `OutOfRangeModelValues_AreClamped`, `MalformedJson_FallsBackToRuleEngine_AndRecordsWhy` |
| Business rules enforced | `Approve_Twice_…`, `Approve_AfterReject_…`, `AnOlderRun_CannotBeDecided_…`, `Approve_OnAReportNotYetApproved_DrawsNoZone` |
| Nothing high-impact before human approval | `GoldenCase_AgentProposesOnly_SeverityInForceIsUnchanged`, `Reject_LeavesTheIncidentUntouched_…` |
| Prompt-injection resistance | `PromptInjection_StaysInsideTheReportData_NotTheInstructions`, `…_ThatBreaksTheSchema_IsRejected`, `…_ThatFoolsTheModel_StillCannotChangeSeverityInForce` |
| Safe failure (model down, timeout, retry limit) | `ModelUnavailable_…`, `NoModelConfigured_…`, `ModelRetries_AreRecordedOnTheRunAndItsStep`, `UnexpectedFailure_…`, `RainfallTool_ReturnsNull_…` |

The guide names Ollama and OSRM; your equivalents are Gemini and Open-Meteo.

---

## 2.4 Challenges and learning

**[you]** Choose two or three that you actually lived through and write cause → fix → lesson. Candidates the
repository supports:

| Candidate | Evidence in the repo |
|---|---|
| The agent graded reports before the photo arrived | `POST /api/incidents/with-photo`, `CreateWithPhotoAsync`, `ReportWithPhotoTests` |
| Approval could be repeated, reversed, or applied to a stale run, and re-sent the warning | `EnsureAwaitingDecisionAsync`, concurrency token, transaction, commit `7c593ed` |
| Adding paging to an endpoint the Flutter map and Component D already called | Opt-in `page`/`pageSize`, `X-Total-Count`, `apiFetchPage`, commit `de3ef48` |
| Local-disk photos do not survive a container restart | [ADR 0002](adr/0002-incident-photo-storage.md), `IImageStore` |
| Gemini instead of the proposal's Ollama | [ADR 0001](adr/0001-llm-provider.md) |
| Mapbox tiles rendered grey on the Android emulator | [ADR 0003](adr/0003-map-tile-provider.md) |
| testmail.app turned out to be receive-only | [ADR 0004](adr/0004-email-notifications.md) |
| A generated migration default would have opted every existing user out of warnings | Hand-edited `EmailNotifications` migration |
| Gemini thinking tokens truncating the JSON; a thinking budget of 0 being rejected | Comments in `GoogleAiClient.cs` |
| Merging three other components into one folder structure and migration chain | 25 commits on 23 Sep |

---

## 2.5 Individual AI usage log

Exact columns required: **Date · Tool and model · Task / section · What the tool produced · What I changed
or rejected · How I verified it.**

Only you know what was really used and when. I can vouch for one entry — this session:

| Date | Tool and model | Task / section | What the tool produced | What I changed or rejected | How I verified it |
|---|---|---|---|---|---|
| 2026-10-01 | Claude Code (Claude Opus 5.5) | Individual report preparation | Read the Component A code and produced these working notes, including a list of mismatches between code, comments and docs; ran the three test suites | **[you]** | **[you]** |

**[check]** [component-a-report.md](component-a-report.md) §10 has eight earlier entries with the dates left
blank and the model given as `claude-sonnet-5`. They were drafted by an AI tool, not by you. Before copying
any of them: confirm the tool and model you really used, put the real date (it should line up with a commit
— e.g. pagination ↔ `de3ef48` on 28 Sep, agent redesign ↔ `7c593ed` on 30 Sep), and fill "What I changed or
rejected" from memory. That table also lacks the "What I changed or rejected" column. Drop any entry you
cannot stand behind. Also log any other tools you used (Copilot authored PR #3 in this repo).

---

## 2.6 AI reflection (about one page)

**[you] — write this yourself.** The guide gives no credit for an AI-written reflection, so there is no
draft here. The four questions:

1. Which AI tools did you use, and at which stages?
2. What did they do well, and what did they get wrong?
3. What did you change, add or reject from the AI output, and why?
4. What did you learn about your own skills and understanding?

It has to agree with 2.3 and 2.5. One fact you may want to address directly: the 30 Sep and 1 Oct commits
are large and were AI-assisted.

---

## 2.7 Signed declaration

Guide's wording: *"I confirm that the work in this section is my own, that all AI use has been disclosed in
my AI usage log, and that I can explain, test and modify the work submitted under my name."*

Name: Lelum Jayasooriya · Student ID: IT24101153 · Signature: **[you]** · Date: **[you]**

---

## 8. Code and doc mismatches to fix (each is a small edit)

| Where | Says | Actually |
|---|---|---|
| [SafetyZonesController.cs:19-22](../backend/RescueSriLanka.Api/Features/ComponentA/Controllers/SafetyZonesController.cs#L19-L22) and `ZoneCheckResultDto` | Consumed by Student B's travel advisory | Only your Flutter client calls it |
| [IncidentsController.cs:224-228](../backend/RescueSriLanka.Api/Features/ComponentA/Controllers/IncidentsController.cs#L224-L228) | Severity in force changes "solely through the severity endpoint" | It also changes through `POST /api/agentruns/{id}/approve`, which is the main path |
| `IncidentAnalysisAgent.cs` class comment | "Contract with the Coordinator/Planner Agent (Student B)" | Nothing in Component B calls `AnalyseAsync` |
| `GoogleAiClient.cs` | Header comment gives `gemini-2.0-flash`; another comment mentions `gemini-3.6-flash` | Default is `gemini-3-flash-preview` |
| `AppDbContext.cs` class comment | "The single EF Core context for the platform" | Component D has its own `ComponentDDbContext` |
| [group-report-outline.md](group-report-outline.md) §2.3, §4, §5.1 | 19 endpoints; 236 backend tests; "Component A has zero React tests"; agent tests 20 and 6; agent is "single-step" | 20 endpoints; 380 tests; 4 React tests; 27 and 19; plan-and-delegate since 30 Sep |
| [component-a-report.md](component-a-report.md) §7 | Commit table stops at 28 Sep | Add `7c593ed` and `008dbf4` |

---

## 9. Viva preparation — where to look

| Guide prompt | Practise on |
|---|---|
| Explain one controller | `AgentRunsController` — 70 lines, shows roles, 404 vs 409, claim reading |
| Explain one service | `AgentRunService.ApproveAsync` |
| Explain a DTO | `CreateIncidentRequest` and why `IncidentDto.FromIncident` exists |
| Relationship / constraint | `Incident`–`SafetyZone` cascade; why `agent_runs.IncidentId` has no FK |
| Migration | `EmailNotifications` (hand-edited default) or `AddAgentRunPlanAndDecision` (backfill SQL) |
| Index | `(Latitude, Longitude)` and the bounding-box query that uses it |
| JWT and role checks | `Program.cs` token validation; `[Authorize(Roles = …)]`; `IsStaff()` |
| State management | Dashboard's two effects + `AbortController`; Flutter `setState` + `ChangeNotifier` |
| Token storage | `session.ts` vs `flutter_secure_storage` |
| Agent role, tools, state, validation, approval | `IncidentAnalysisRoles.cs` top to bottom; `agent_runs` columns |
| Explain one test | `Approve_Twice_IsRefused_AndWarnsTheDistrictOnlyOnce` or a PostgreSQL concurrency test |
| CI steps | `ci.yml` — three jobs, the PostgreSQL service container |
| Deployment choice / ADR | ADR 0001 (Gemini vs Ollama), ADR 0002 (Cloudinary vs disk) |
| Change a rule live | Likely asks: a severity threshold in `SeverityRules`; the title length; a clamp range in `ProposalValidationAgent`; adding a status-transition check to `UpdateStatusAsync`; a new sort key in `QueryAsync` |
| Debug a failed workflow | Read a `Failed` run's `ErrorMessage` and `PlanJson` to find which step failed |

---

## 10. Guide checklist — status today

| Item | Status |
|---|---|
| All 7 items present, with the brief's headings | To write |
| Rubric areas covered with real screenshots or snippets | Snippet sources listed above; screenshots **[you]** |
| At least 4 endpoints and the business operation | Ready (20 endpoints; approval gate) |
| Agent section has real JSON input/output and a run trace | **Missing** — capture a run |
| Commit, PR and test evidence with links | Table drafted; reviewers and screenshots **[you]** |
| AI usage log, exact six columns, real entries only | One entry confirmed; the rest **[you]** |
| Reflection, about one page, written by you | **[you]** |
| Declaration signed and dated | **[you]** |
| No passwords, keys or tokens in any screenshot | Check Swagger "Authorize", `appsettings.Development.json`, the Mapbox token in tile URLs in browser dev tools, and terminal screenshots showing connection strings |
