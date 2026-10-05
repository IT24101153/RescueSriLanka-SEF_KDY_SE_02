# Individual Report — Student A

| | |
|---|---|
| **Name** | Lelum Jayasooriya |
| **Student ID** | IT24101153 |
| **Group** | SEF_KDY_SE_02 — RescueSriLanka |
| **Component** | A — Incident & Disaster Map |
| **Primary web role** | Emergency Coordinator |
| **Agentic AI** | Incident Analysis Agent, Incident Enrichment Agent, Zone Planning Agent |
| **Repository** | https://github.com/IT24101153/RescueSriLanka-SEF_KDY_SE_02 |

> **Before submitting:** every line marked **`[TO DO]`** needs something only I can supply (a screenshot, a reviewer's name, my own wording, a signature). Delete these notes once each is done.

---

## 1. Contribution statement

I designed and built **Component A — Incident & Disaster Map**, the part of RescueSriLanka that takes a citizen's disaster report, assesses it, and turns it into the public safety map. I own it end to end across all three tiers and its AI subsystem.

**What I built**

- **ASP.NET Core API** — 4 controllers and 26 endpoints (incidents, safety zones, agent runs, notification checks), 5 services, and two background workers, about 6,700 lines under `backend/RescueSriLanka.Api/Features/ComponentA`.
- **PostgreSQL data model** — the `Incident`, `IncidentImage` and `SafetyZone` entities, the `AgentRun` audit table that the other components also write to, and the migrations behind them.
- **React web console (Emergency Coordinator)** — the `DisasterDashboard` with five tabs (Overview, Disaster map, Safety zones, Incident queue, Agent activity) and the review drawer where a coordinator approves, rejects or revises a report and every AI proposal; about 3,900 lines of TypeScript.
- **Flutter mobile app (citizen / tourist)** — the live disaster map, the report form with camera, GPS and map-pin location, and "My reports" with a review timeline; about 5,700 lines of Dart.
- **Agentic AI** — three agents that each follow the same plan → evidence → reasoning → validation workflow, with a deterministic rule engine as a fallback and a human approval gate in front of every change.
- **Notifications** — email and push warnings to everyone in an affected district, sent once per incident and only after a human has confirmed the report.
- **Tests** — 13 backend test files (139 test cases) covering Component A, 8 React tests on the agent panels, a PostgreSQL integration suite, and a load-test script.

**Shared work I also carried for the group**

- Authentication and account flows used by every component (login, registration, forgot-password with OTP, profile, separate citizen and staff sign-in).
- Integrating Components B, C and D into one codebase: the `Features/Component*` folder structure, one migration chain, and the conflict resolution on 22–23 September.
- The mobile shared UI kit and navigation shell, the web console shell, the Dockerfile, the Azure App Service deployment workflow, and the three-job GitHub Actions CI pipeline.

Of the 151 commits across all branches of the repository, 81 are mine (72 of the 142 on `main`), spread from 9 September to 4 October 2026.

**`[TO DO]`** List the group-report sections I wrote.

---

## 2. Owned component and technical work

### 2.1 What the component does

A citizen files a report from the phone. It is saved as **Reported** and is not public. In the background the agents grade its severity and check it for duplicates and missing details. An Emergency Coordinator opens it in the web console, decides whether the report is true, and decides whether the AI's assessment is right. Only then does the report appear on the public map, draw a safety zone, and warn its district.

```
Citizen (Flutter)            API (ASP.NET Core)                      Coordinator (React)
─────────────────            ──────────────────                      ───────────────────
Report + photo + GPS ──────► POST /api/incidents/with-photo
                             save incident (Reported, hidden)
                             store photo (Cloudinary)
                             enqueue ─► IncidentAnalysisWorker
                                         1. Incident Analysis Agent
                                         2. Incident Enrichment Agent
                                         └► proposals saved on agent_runs ──► review drawer
                                                                              approve / revise / reject
                             POST /api/agentruns/{id}/approve ◄──────────────┘
                             apply change + recompute zones (one transaction)
                             enqueue district warning (email + push)
Live map + zone banner ◄──── GET /api/incidents, /api/safetyzones
```

Two rules run through the whole design:

1. **An agent only proposes.** The severity in force, a corrected field, a merge, or a new zone changes only when a coordinator approves it.
2. **Nothing unreviewed is public.** A report that no coordinator has approved is hidden from the public list, draws no zone, and warns nobody.

### 2.2 ASP.NET Core API

| Controller | Endpoints | Purpose |
|---|---|---|
| `IncidentsController` | 14 | List (filter, sort, page), mine, get, nearby, statistics, create, create with photo, edit, status, severity override, delete, image upload, run analysis, run enrichment |
| `SafetyZonesController` | 7 | List, point check, recompute, declare / edit / retire a manual zone, run the zone planner |
| `AgentRunsController` | 3 | List runs, approve (optionally revised), reject |
| `NotificationsController` | 2 | Send a test warning, read the test inbox |

Technical points I can explain and defend:

- **Layering.** Controllers hold no business logic; they translate HTTP to a service call and map exceptions to status codes (`ArgumentException` → 400, `InvalidOperationException` → 409, missing → 404, a failed agent → 502 with the failure already recorded on its run).
- **Role-based access.** JWT bearer authentication. Reads of the map are anonymous on purpose, so a tourist sees hazards without an account. Filing a report needs the Citizen or Emergency Coordinator role; every coordinator action is `[Authorize(Roles = EmergencyCoordinator)]`.
- **Visibility rule.** `GetForViewerAsync` returns an unapproved report only to staff and the person who filed it. Everyone else gets the same 404 as a missing report, so its existence is not leaked.
- **Status state machine.** `AllowedStatusTransitions` in `IncidentService` defines where a report may go next (Reported → Verified / Rejected; Rejected → Verified; Merged is final). An illegal move returns 409.
- **Paging without breaking callers.** `GET /api/incidents` sorts and pages only when `page` and `pageSize` are both sent, returning the total in `X-Total-Count`. Callers that send neither (the Flutter map, Component D) get exactly what they got before.
- **Geo queries.** "What's near me" filters with a bounding box in SQL (using the latitude/longitude index), then applies the exact haversine distance in memory (`GeoService`).
- **Background work.** New reports go onto a bounded `Channel` (200 items, drop-oldest) drained by `IncidentAnalysisWorker`, so a citizen never waits on a language model. Notifications use the same pattern (`NotificationQueue`, 500 items).
- **Photo upload.** One multipart request files the report and its photo together. The file is checked for size (8 MB), declared type, and its real file signature (JPEG, PNG, WebP, HEIC magic bytes) before any report is created.
- **Rate limiting.** The three agent endpoints share an `ai` policy of 10 requests per minute.

### 2.3 PostgreSQL and data modelling

| Table | Key columns | Notes |
|---|---|---|
| `incidents` | type, severity, status, latitude, longitude, radius, district, people affected, `Ai*` fields, `DuplicateOfIncidentId`, `DistrictWarningSentAt` | AI proposal is stored in separate `Ai*` columns from the severity in force. Indexed on status, severity, active flag, district, and (latitude, longitude). |
| `incident_images` | incident id, storage path, content type, size, caption | Cascade-deleted with the incident. |
| `safety_zones` | status, source (derived / manual), centre, radius, expiry, `SourceIncidentId`, `SourceAgentRunId` | Derived zones are rebuilt from approved incidents; manual zones are never touched by the rebuild. |
| `agent_runs` (shared) | agent name, objective, input, plan, tool calls, output, status, decision, decided by / at, model, attempts, duration | One row per agent execution. `ApprovedAt` is a concurrency token. |

Migrations I wrote for Component A: `ComponentA_IncidentsAndZones`, `AgentRuns`, `EmailNotifications`, `AddAgentRunPlanAndDecision`, `PushNotifications`, `ComponentAEnrichmentAndZonePlanning`.

Design decisions:

- **Enums are stored as text**, so the database is readable and adding a value does not renumber existing rows.
- **Approval is transactional.** The decision, the change to the incident, and the zone recompute commit together or not at all.
- **Optimistic concurrency.** If two coordinators decide the same proposal at once, the second update matches no row and is refused cleanly.

### 2.4 React web console (Emergency Coordinator)

| Tab | What the coordinator does there |
|---|---|
| Overview | Headline figures, breakdowns by severity / type / district, AI coverage meters, latest reports |
| Disaster map | Leaflet map locked to Sri Lanka, incident markers sized and coloured by severity, zone circles, side list |
| Safety zones | Declare, edit and retire manual zones; run the Zone Planning Agent and preview its proposals on the map before approving |
| Incident queue | Server-side sorted and paged table with severity, status and type filters |
| Agent activity | Every agent run: its plan, which agent each step was delegated to, tools called or skipped, status, and the coordinator's decision |

The review drawer is where the role's real work happens:

- `ReportDecision` asks two separate questions — *is the report true?* and *is the AI's severity right?* — because a report can be true while the AI has graded it wrong.
- `EnrichmentPanel` lists each field correction and any likely duplicate, all ticked by default; the coordinator unticks what they disagree with and approves the rest.
- `ZonePlanner` lets the coordinator edit a proposed zone's name, radius or expiry before approving; an edited plan is recorded as *Revised*.
- `IncidentForm` and `LocationPicker` let a coordinator file a report taken by phone, choosing the location by place search or by clicking the map.

Other details: requests are aborted when filters change so a slow response cannot overwrite a newer one; the drawer polls for an agent result that is still being produced; and severity is never shown by colour alone — every use pairs it with a label, and the map with marker size.

### 2.5 Flutter mobile app (citizen / tourist)

| Screen | Features |
|---|---|
| `DisasterMapScreen` | Live map (OpenStreetMap tiles) held to the island, incident markers and zone circles, severity filter chips with counts, place search with debounce, opt-in GPS, and a zone banner ("You are in a CAUTION zone") from `GET /api/safetyzones/check` |
| `IncidentSheet` | Incident detail with photos and the AI rationale, read-only |
| `ReportScreen` | Step-by-step form: disaster type, description, location from GPS / home district / a chosen spot, photo from camera or gallery, a checklist of what still blocks sending |
| `LocationPickerScreen` | Full-screen map with a fixed centre pin that the map moves under |
| `MyReportsView` | The user's own reports from `GET /api/incidents/mine`, each with a progress track and review timeline; cached on the phone for instant and offline display |

Design choices: the form deliberately does not ask the citizen how severe it is — grading is the agent's job and the coordinator's decision. A citizen sees a severity only after a coordinator has approved the report. Push notifications use Firebase Cloud Messaging, with the token registered against the signed-in account and removed on sign-out.

### 2.6 Agentic AI

Component A has three agents. Each is a small multi-agent workflow with one coordinator and four roles, and each role has one job, typed inputs and outputs, and a fixed list of what it may touch.

| Role | Input → output | May touch |
|---|---|---|
| Planner | incident → ordered plan | Nothing. Deterministic, so report text cannot steer which tools run. |
| Evidence | incident → evidence bundle | Allow-listed, read-only tools only |
| Reasoning | incident + evidence → proposal | The language model (Gemini) or the rule engine. No tools, no database. |
| Validator | proposal → accepted result | Nothing. A pure function with the last word. |

| Agent | Question it answers | Tools | Proposes |
|---|---|---|---|
| **Incident Analysis** | How bad is it? | `count_nearby_active_incidents`, `get_rainfall_last_48h` (Open-Meteo), `load_incident_images` | Severity, score 0–100, confidence, zone status, radius, rationale |
| **Incident Enrichment** | Is the record right, and is it new? | `find_duplicate_candidates`, `locate_district_from_coordinates`, `extract_people_estimate` | Field corrections (title, type, district, people, address) and a duplicate to merge into |
| **Zone Planning** | What does the whole map need? | `list_approved_active_incidents`, `cluster_incidents`, `get_rainfall_last_48h`, `list_manual_zones` | New area zones over clusters of incidents, and stale manual zones to retire |

**Adaptive planning.** The plan depends on the incident: rainfall is fetched only for weather-driven hazards, photos are loaded only when the report has some, and a closed report skips the duplicate search. Each omission is written into the plan's notes.

**Safety controls**

| Risk | Control |
|---|---|
| Prompt injection in citizen text | Fixed system instruction; report text enters only as labelled data. Even a fooled model can only produce a proposal, never a change. |
| Model output out of range | Provider is constrained to a JSON schema, then every field is re-validated: score clamped to 0–100, confidence to 0–1, radius to 100–20,000 m. Each clamp is recorded. |
| Invented references | A duplicate id must be one the Evidence agent actually found; a zone's incident ids must be in the evidence; a zone centre must be inside Sri Lanka; a Danger zone must rest on an approved incident. |
| Model unavailable | Up to 3 attempts with backoff on 429 / 5xx, then the deterministic rule engine. The run is marked `SucceededWithFallback` with the reason. |
| Tool failure | The rainfall tool returns null on any failure (5-second timeout); the agent carries on without it. |
| Unexpected failure | The run is marked `Failed` with the reason, the incident is left untouched, and the next agent in the queue still runs. |
| Repeated or stale approval | A proposal takes exactly one decision, on the newest run only. |

**Rule engine (the deterministic floor).** Severity score = hazard weight (0–40) + people at risk (0–30) + clustered reports (0–20) + rainfall (0–10). Worked example: a flood (28) affecting 600 people (24), with 2 other incidents within 5 km (8) and 120 mm of rain in 48 hours (7), scores **67 / 100 → High**, zone **Danger**, radius **2,000 m**, confidence fixed at 0.55 because rules never claim model-level confidence.

**Auditability.** Every run stores its objective, validated input, plan with per-step status and duration, tool calls, validated output, model name, attempt count, any error, and the coordinator's decision and note.

**Model choice.** Google Gemini (`gemini-3-flash-preview`, temperature 0.2), a recorded deviation from the proposal's Ollama: the agent must read photographs, and a local vision model did not fit the development machine or the schedule. Agents depend on an `ILlmClient` interface, so the provider is one class to swap.

**`[TO DO]`** Paste one real run from the `agent_runs` table (input, plan, tool calls, output) and a screenshot of the Agent activity tab.

### 2.7 Integration, security and deployment

- **Integration with other components.** Component D reads Component A's incidents for team assignment; the shared `AgentRun` table and Gemini key are used by every component's agents.
- **Notifications.** A district warning is sent only for a confirmed incident at or above the configured severity, once per incident (`DistrictWarningSentAt`), with separate email and push opt-ins.
- **Security.** JWT with role claims, rate limiting, file-signature checks on uploads, the API key sent as a header so it never lands in logs, HTML-escaped citizen text in emails, and secrets supplied by environment variable.
- **Deployment.** Dockerfile for the API, GitHub Actions deployment to Azure App Service, the React console on Vercel.

**`[TO DO]`** Screenshots: coordinator dashboard (each tab), review drawer with an AI proposal, mobile map, report form, My reports.

---

## 3. Key commit, pull-request and test evidence

### 3.1 Key commits

Link form: `https://github.com/IT24101153/RescueSriLanka-SEF_KDY_SE_02/commit/<hash>`

| Date | Commit | What it did |
|---|---|---|
| 2026-09-09 | `7a7be50`, `39d3690`, `ebef0cb` | Project start: auth, the `Incident` / `SafetyZone` / `AgentRun` models and their first three migrations, first Incident Analysis Agent and rule engine |
| 2026-09-18 | `078b560` | Component A web dashboard and mobile screens (49 files) |
| 2026-09-21 | `5132bd6` | Email notifications: district warnings, report receipts, `EmailNotifications` migration |
| 2026-09-22 | `039d78e` | Restructured the codebase into `Features/ComponentA` / `ComponentB` folders (115 files) |
| 2026-09-23 | `6584ae5`, `e0f54bb`, `f56c22f`, `2cbfc2f` | Merged Components C and D into the shared structure and one migration chain; 16 conflicts resolved |
| 2026-09-23 | `ba44c97`, `685ae0c`, `acba7e1` | Mobile: sign-in gated tabs, OpenStreetMap tiles, place search, shared UI kit |
| 2026-09-24 | `0605d72`, `944092e` | Dockerfile and Azure App Service deployment workflow |
| 2026-09-27 | `ec68fc7` | Forgot-password flow with OTP |
| 2026-09-28 | `de3ef48` | Opt-in paging and sorting on `GET /api/incidents`; global exception handler |
| 2026-09-29 | `aa96936`, `ca678f2` | Component A polish across web and mobile; map update |
| 2026-09-30 | `7c593ed` | Plan-and-delegate agent roles, one-decision approval guard, concurrency token, `AddAgentRunPlanAndDecision` migration, Agent activity plan view and tests |
| 2026-10-01 | `008dbf4` | PostgreSQL integration tests, PostgreSQL service in CI, load-test script |
| 2026-10-03 | `f499cff` | Action emails to coordinators and citizens |
| 2026-10-04 | `02993c9` | Incident Enrichment Agent and Zone Planning Agent, manual zone create / edit / retire, coordinator report form, 36 new backend tests and 4 React tests (45 files) |
| 2026-10-04 | `d99145a` | Push notifications |

### 3.2 Pull requests

| PR | Date | Branch → main | Content | Reviewed by |
|---|---|---|---|---|
| #1 | 2026-09-16 | `feature/component-a-incident-map` | First Component A merge: models, migrations, API, agent | **`[TO DO]`** |
| #2 | 2026-09-16 | `feature/component-b-help-requests` | Component B — merged by me | **`[TO DO]`** |
| #3 | 2026-09-16 | `copilot/…-revert-component-a-files` | Reverted Component A files merged into B's branch by mistake | **`[TO DO]`** |
| #4 | 2026-09-22 | `feature/component-a-incident-map` | Second Component A merge: dashboard, mobile screens, notifications | **`[TO DO]`** |
| #6 | 2026-09-23 | integration | Components C and D merged into the shared structure | **`[TO DO]`** |
| #7–#13 | 24 Sep – 3 Oct | integration | Deployment, login, Component A improvements, final release | **`[TO DO]`** |

**`[TO DO]`** Open each PR on GitHub and record the real reviewer; write "not reviewed" where there was none.

### 3.3 Test evidence

Run on 4 October 2026 on `main` at `d99145a`:

| Suite | Command | Result |
|---|---|---|
| Backend (whole solution) | `dotnet test RescueSriLanka.slnx` | **675 passed, 0 failed, 5 skipped** |
| React (whole app) | `npm test` | **178 passed** (15 files) |
| Flutter (whole app) | `flutter test` | **82 passed** |

The 5 skipped tests are the PostgreSQL integration tests, which run only when `TEST_POSTGRES` is set; CI sets it and runs them against a `postgres:16` service container.

**Component A test files (backend)**

| File | Tests | What it proves |
|---|--:|---|
| `IncidentAnalysisAgentTests` | 23 | Golden cases, proposes-only, tool allow-list, clamping, prompt injection, fallback, failure recording, adaptive plan |
| `IncidentEnrichmentAgentTests` | 12 | Validator drops invented values, injected duplicate id dropped, rule engine without a model, two reports cannot merge into each other |
| `ZonePlanningAgentTests` | 5 | Cluster → one area zone, retire empty zones, off-island and unfounded zones rejected |
| `AgentRunServiceTests` | 18 | Approve / revise / reject, one decision only, stale runs refused, warning sent once, rate limit |
| `ComponentACoordinatorEditingTests` | 19 | Partial field approval, merge moves photos and head-count, edited zone plans, manual zone rules, coordinator-only endpoints |
| `SeverityRulesTests` | 11 | Rule-engine scoring |
| `SafetyZoneServiceTests` | 11 | Zone derivation, worst zone wins, unapproved reports draw nothing |
| `NotificationServiceTests` | 21 | Threshold, once per incident, opt-outs, push-only recipients |
| `GeoServiceTests` | 6 | Distance and bounding box |
| `IncidentVisibilityTests`, `IncidentStatusRollbackTests`, `ReportWithPhotoTests` | 8 | Visibility rule, reject-then-approve, photo stored before the agent looks |
| `PostgresIntegrationTests` | 5 | Every migration applies cleanly; two simultaneous approvals — exactly one wins; a failed recompute rolls the approval back |

**Selected test cases**

| ID | Scenario | Test | Result |
|---|---|---|---|
| TC-A01 | A valid model answer is persisted as a succeeded run | `GoldenCase_ValidModelAnswer_IsPersistedAsSucceededRun` | Pass |
| TC-A02 | The agent never changes the severity in force | `GoldenCase_AgentProposesOnly_SeverityInForceIsUnchanged` | Pass |
| TC-A03 | Out-of-range model values are clamped | `OutOfRangeModelValues_AreClamped` | Pass |
| TC-A04 | Prompt injection that fools the model still changes nothing | `PromptInjection_ThatFoolsTheModel_StillCannotChangeSeverityInForce` | Pass |
| TC-A05 | Model down → rule engine, reason recorded | `ModelUnavailable_FallsBackToRuleEngine_AndRecordsWhy` | Pass |
| TC-A06 | A proposal cannot be approved twice; one warning only | `Approve_Twice_IsRefused_AndWarnsTheDistrictOnlyOnce` | Pass |
| TC-A07 | An unapproved report is hidden from the public | `UnapprovedReport_IsHiddenFromThePublic_ButNotItsReporterOrStaff` | Pass |
| TC-A08 | Merging a duplicate keeps its photos and head-count | `MergingADuplicate_FoldsItIntoTheEarlierReport_WithItsPhotosAndHeadCount` | Pass |
| TC-A09 | An edited zone outside Sri Lanka is refused before anything is written | `AnEditedZoneOffTheIsland_IsRefused_BeforeAnythingIsWritten` | Pass |
| TC-A10 | A file that is not really an image is refused | `AFileWithoutAnImageHeaderIsRefused_EvenWhenItClaimsToBeAJpeg` | Pass |

**Performance** (`tools/perf/load_test.py`, 1 October 2026, local API and PostgreSQL, model switched off):

- 100% success on every endpoint at 1, 10 and 50 concurrent users (about 5,700 requests).
- Read endpoints at 50 concurrent: p95 between 7.7 ms and 14.0 ms.
- `POST /api/incidents` at 50 concurrent: p95 72.1 ms, p99 79.1 ms.
- Agent, rule-engine path with live rainfall lookup: median 242 ms, max 1,023 ms over 6 runs.

These figures are from one development machine and do not include Gemini latency.

**CI.** `.github/workflows/ci.yml` runs three jobs on every push and on pull requests to `main`: backend (build, test against PostgreSQL, upload results), web (lint, test, type-check and build), mobile (analyze, test).

**Known gaps.** Component A's Flutter screens have no widget tests of their own; mobile coverage is on shared services (auth, push, place search). The React tests cover the agent panels, not the report form.

**`[TO DO]`** Screenshots: a passing GitHub Actions run, and my own terminal output of the three test commands.

---

## 4. Challenges and learning

**1. The agent graded reports before the photo arrived.**
*Cause:* the mobile app filed the report, then uploaded the photo in a second request. The background agent started as soon as the report was saved, so it never saw the picture. *Fix:* a single multipart endpoint (`POST /api/incidents/with-photo`) that validates the file, saves the report, stores the photo, and only then queues the agent. *Learning:* with background work, the order in which things are queued is part of the design, not a detail.

**2. Approvals could be repeated, reversed or applied to an old proposal.**
*Cause:* approval was a plain update, so a second click re-sent the district warning, a rejected proposal could later be approved, and two coordinators could both decide. *Fix:* a one-decision guard on the newest run only, a concurrency token on `ApprovedAt`, and one transaction around the decision, the change and the zone recompute. I proved it with a PostgreSQL test in which two simultaneous approvals produce exactly one winner. *Learning:* in-memory tests cannot show concurrency bugs; some behaviour has to be tested on the real database.

**3. Gemini returned truncated JSON.**
*Cause:* the model's "thinking" tokens count against the output limit, so a tight limit cut the answer off mid-object, and setting the thinking budget to zero was rejected by the API. *Fix:* more output headroom, a small non-zero thinking budget, and treating any finish reason other than `STOP` as a failure so the rule engine takes over. *Learning:* never parse a partial model answer; fail loudly and fall back.

**4. Merging four people's components into one codebase.**
*Cause:* the components were built on separate branches with their own folder layouts, database contexts and, in one case, duplicate copies of my `Incident` model. Two components both declared an `IIncidentAnalysisAgent`. *Fix:* one `Features/Component*` structure, one migration chain, deleting the duplicates in favour of the owning component's version, and re-running all tests after each merge (16 conflicts on the Component D merge alone). *Learning:* agree shared models and folder structure in week one; integration cost grows with every day of delay.

**5. Adding paging to an endpoint other people already used.**
*Cause:* the coordinator's queue needed sorting and paging, but the mobile map and Component D called the same endpoint expecting every row. *Fix:* paging is opt-in, with the total in a response header, so existing callers were unaffected. *Learning:* a published endpoint is a contract; extend it, do not change it.

**`[TO DO]`** Check each of these against what I actually experienced and reword in my own voice.

---

## 5. Individual AI usage log

| Date | Tool and model | Task / section | What the tool produced | What I changed or rejected | How I verified it |
|---|---|---|---|---|---|
| 2026-09-16 | GitHub Copilot coding agent | PR #3 | A revert of Component A files that had been merged into the Component B branch by mistake | **`[TO DO]`** | Reviewed the diff and merged; build still passed |
| 2026-09-22 | Claude Code (Claude Opus 5) | Restructure into `Features/ComponentA` / `ComponentB` (`039d78e`) and merge of `main` into B's branch (`0c03141`) | File moves, namespace updates, conflict resolution | **`[TO DO]`** | Backend and frontend builds; test suite |
| 2026-09-23 | Claude Code (Claude Opus 5) | Merging Components C and D (`6584ae5`, `e0f54bb`, `f56c22f`, `2cbfc2f` and related) | Conflict resolution, folding C into the shared database context, moving D's files | **`[TO DO]`** | 158 backend tests passing after the merge; frontend build; `flutter analyze` clean |
| 2026-09-23 | Claude Code (Claude Opus 5) | Mobile shared UI kit, place search, sign-in gated tabs (`acba7e1`, `685ae0c`, `ba44c97` and related) | Dart widgets and the place-search service with tests | **`[TO DO]`** | Ran on the emulator; `flutter test` |
| 2026-09-30 | **`[TO DO]`** tool and model | Agent redesign and approval guard (`7c593ed`) | **`[TO DO]`** | **`[TO DO]`** | `IncidentAnalysisAgentTests`, `AgentRunServiceTests` |
| 2026-10-01 | Claude Code (Claude Opus 5.5) | Test hardening and report notes (`008dbf4`) | PostgreSQL integration tests, load-test script, working notes for this report | **`[TO DO]`** | Ran the tests against local PostgreSQL; ran the load test |
| 2026-10-04 | **`[TO DO]`** tool and model | Enrichment and Zone Planning agents (`02993c9`) | **`[TO DO]`** | **`[TO DO]`** | `IncidentEnrichmentAgentTests`, `ZonePlanningAgentTests`, `ComponentACoordinatorEditingTests` |
| 2026-10-04 | Claude Code (Claude Opus 5.5) | This individual report section | Read the Component A code, ran the three test suites, and drafted this section from the repository | **`[TO DO]`** | Checked every figure against the code, `git log` and my own test runs |

**`[TO DO]`** Add any other AI use (chat assistants, IDE completion) with real dates, and remove any row I cannot stand behind.

---

## 6. AI reflection

> **`[TO DO]`** This is a draft built only from what the repository shows. The reflection must be my own account: rewrite it in my own words and correct anything that does not match what happened.

**Which tools I used, and when.** I used Claude Code as a coding assistant from the integration phase onward, and GitHub Copilot's coding agent once, for a revert. In the first two weeks I built the Component A foundation — models, migrations, the first agent and rule engine — and from 22 September I used AI heavily for work that was large but mechanical: restructuring folders, merging the other three components, and building the mobile UI kit. In the final week I used it for the agent redesign, the two additional agents, the PostgreSQL tests, and for preparing this report.

**What it did well.** It was strongest at wide, repetitive changes where consistency matters more than invention: moving a hundred files and fixing every namespace, resolving sixteen merge conflicts by a stated rule, and writing tests that name the behaviour they prove. It was also useful as a reviewer of my own design. The idea that an agent should only propose, and that a deterministic validator should have the last word, became much sharper once I had to explain it precisely enough for the tool to implement it.

**What it got wrong.** It wrote plausible details that were not true. Comments in my code still describe a contract with another student's component that was never built, and a header comment names a different Gemini model from the one the code uses. An earlier draft of an AI usage log was itself AI-written, with blank dates and the wrong model name; I threw it away. It also tends to produce a lot at once, which is why some of my late commits are very large — that makes them harder to review and harder to explain, and it is something I would do differently.

**What I changed or rejected.** **`[TO DO]`** Give two or three concrete examples of output I rewrote or refused, and why.

**What I learned about my own skills.** The parts I understand best are the parts I had to decide: where the approval gate sits, what the validator must refuse, why a report is hidden until a human confirms it. AI made me faster at producing code, but it did not make those decisions, and it could not tell me whether an approval was safe under concurrency — I only knew that once a test on a real database proved it. The lesson I take from this project is that with AI assistance the scarce skill is no longer typing code but specifying, reading and verifying it, and that I should commit in smaller steps so that every change under my name is one I can explain line by line.

---

## 7. Signed declaration

I confirm that the work in this section is my own, that all AI use has been disclosed in my AI usage log, and that I can explain, test and modify the work submitted under my name.

| | |
|---|---|
| **Name** | Lelum Jayasooriya |
| **Student ID** | IT24101153 |
| **Signature** | ______________________ |
| **Date** | ______________________ |
