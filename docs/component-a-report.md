# Component A — Incident & Disaster Map
### Report material — Student A (Lelum Jayasooriya, IT24101153)

> **How to use this file:** this is source material for your consolidated report, not the report itself.
> Sections 1–7 are factual/technical — copy and adapt freely into the Group Report (technical report,
> testing report, agent evaluation report, security section). Sections 8–11 feed your **Individual Report**
> section — but see the note before Section 11: the reflection must be in your own words, the spec is
> explicit that an AI-generated reflection gets no credit.

---

## 1. Component overview

Component A owns disaster **reporting and the live situational picture**: a citizen files a report (with
photo and GPS) from the Flutter app, the Incident Analysis Agent proposes a severity and safety-zone
classification, an Emergency Coordinator approves/revises/rejects it, and the result reaches back out through
the React dashboard, the Flutter map, and — for high-severity verified incidents — a district warning email.

Business entities owned: **Incident**, **IncidentImage**, **SafetyZone**. Component A also owns the shared
`AgentRun` audit table (used by every component's agents) and the notification/email pipeline.

## 2. Architecture & data model

### Endpoints (20 total — spec minimum is 4 per component)

| Method | Route | Auth | Purpose |
|---|---|---|---|
| GET | `/api/incidents` | Anonymous | Filtered/sorted/paginated incident list |
| GET | `/api/incidents/{id}` | Anonymous | Single incident detail |
| GET | `/api/incidents/mine` | Any signed-in user | The caller's own reports ("My reports" on every device) |
| GET | `/api/incidents/nearby` | Anonymous | Geo query — "what's near me" for the citizen map |
| GET | `/api/incidents/statistics` | Anonymous | Dashboard header counts/breakdowns |
| POST | `/api/incidents` | Any signed-in user | File a report |
| POST | `/api/incidents/with-photo` | Any signed-in user | File a report with a photo in one request |
| PATCH | `/api/incidents/{id}/status` | EmergencyCoordinator | Verify / resolve / reject |
| PATCH | `/api/incidents/{id}/severity` | EmergencyCoordinator | Manual severity override |
| DELETE | `/api/incidents/{id}` | EmergencyCoordinator | Remove a duplicate/spam report |
| POST | `/api/incidents/{id}/images` | Any signed-in user | Attach an additional photo |
| POST | `/api/incidents/{id}/analyse` | EmergencyCoordinator | Manually (re-)run the Incident Analysis workflow. Rate limited (`ai` policy); a failed run answers 502 and is recorded on the agent run |
| GET | `/api/agentruns` | Any signed-in user | Agent execution history / observability, including each run's plan |
| POST | `/api/agentruns/{id}/approve` | EmergencyCoordinator | Human approval gate — applies or revises the AI proposal. One decision per proposal, newest run only; otherwise 409 |
| POST | `/api/agentruns/{id}/reject` | EmergencyCoordinator | Human approval gate — rejects, incident unchanged. Same one-decision rule |
| GET | `/api/safetyzones` | Anonymous | Active safe/caution/danger zones |
| GET | `/api/safetyzones/check` | Anonymous | Point-in-zone check — used by Component A's own React dashboard and Flutter map. **Correction:** the code comment on this endpoint says it's "consumed by... Student B's travel advisory," but a repo-wide search found no such call — Component B built its own independent `POST /api/TravelAdvisories/check-safety` against its own `TravelAdvisory` entities instead. Fix the stale comment or actually wire the integration before claiming it in the report. |
| POST | `/api/safetyzones/recompute` | EmergencyCoordinator | Force a zone rebuild |
| POST | `/api/notifications/test` | EmergencyCoordinator | Sends a real sample district-warning email |
| GET | `/api/notifications/inbox` | EmergencyCoordinator | Reads back the test mailbox (testmail.app) |

Business-specific operations beyond CRUD: geo-radius search (`nearby`), aggregated statistics, severity
override with audit trail, zone point-check consumed by another component, and the whole agent
approve/reject/revise flow.

### Data model

- **Incident** — title/description/type/severity/status, lat/lng + radius, district, estimated people
  affected, the agent's proposed `Ai*` fields kept separate from the severity in force, override provenance
  (`SeverityOverriddenBy/At`), verification provenance, `CreatedAt`/`UpdatedAt` audit fields.
- **IncidentImage** — one-to-many, stored via `IImageStore` (local disk or Cloudinary — see ADR 0002).
- **SafetyZone** — derived from active incidents (`DerivedFromIncident`) or manually set
  (`ManualOverride`), status Safe/Caution/Danger, radius, source incident link.
- **AgentRun** (shared with Component B's planner) — agent name, objective, input / plan / tool-call / output
  JSON, status (Running/Succeeded/SucceededWithFallback/Failed), model used and number of model attempts,
  the human decision (`Decision`: Pending/Approved/Revised/Rejected, with `DecisionNote`, who and when), timing.
  `ApprovedAt` is a concurrency token so two coordinators cannot both decide one proposal.

*(Insert your ER diagram export here — entities above plus their FKs to `User`.)*

## 3. Agentic AI contribution — Incident Analysis workflow

Full design writeup: **[docs/component-a-agent.md](component-a-agent.md)** — use it directly for the Agentic AI
evaluation report. Summary for the technical report:

- **Objective:** classify an incident's severity (Low/Moderate/High/Critical, 0–100 score, confidence) and
  safety-zone recommendation (status + radius) from its text, tool evidence and photos.
- **Four distinct agents, coordinated by `IncidentAnalysisAgent`:**

  | Agent | Responsibility | May touch |
  |---|---|---|
  | Planner | Builds the ordered plan and names the agent for each step. Deterministic; adapts the plan to the incident (rainfall only for weather-driven hazards, photos only if the report has any) and records what it left out | Nothing |
  | Evidence | Runs the planned tools | The three allow-listed tools and the photo store — the only role that calls tools |
  | Severity | Classifies with Gemini (vision-capable, JSON-schema constrained) or, when the model is unconfigured or unusable, the deterministic rule engine (`SeverityRules`) | The model / rule engine; no tools, no database |
  | Validator | Clamps out-of-range numbers (adjustments recorded) and rejects an empty rationale, after which the rule engine takes over | Nothing (pure function) |

- **Tools (allow-listed):** `count_nearby_active_incidents`, `get_rainfall_last_48h` (Open-Meteo),
  `load_incident_images` — each with validated inputs and a documented failure mode.
- **Persisted state:** the plan, each step's agent, status, duration and detail, tool calls, model attempts,
  output and errors are stored per run in `agent_runs` and shown in the React "Plan" column.
- **Safe failure:** a model failure falls back to the rule engine and is recorded. Any other exception marks
  the run `Failed`, changes nothing on the incident and is recorded — a run is never left `Running`.
- **Human approval:** the workflow only proposes. `AgentRunsController.Approve/Reject` is the *only* path by
  which a proposal changes the incident in force. A proposal takes exactly one decision, on the newest run
  of the incident; the decision, incident change and zone recompute commit in one transaction, and the
  district warning is queued only after the commit.
- **Observability:** every run persisted with plan, input, tool calls, output, status, model, attempts,
  duration and decision.

Relation to the group's other agents: Component B's Planner Agent, Component C's allocation and forecast
agents and Component D's orchestrator, dispatch-recommendation and safety-validation agents are separate
workflows; Component A's workflow is self-contained and does not call them (see the correction note under the
`safetyzones/check` endpoint above about cross-component calls).

## 4. Third-party integrations

| Service | Purpose | Notes for the report |
|---|---|---|
| Google Gemini | Vision-capable LLM for the agent's classification | [ADR 0001](adr/0001-llm-provider.md) — justified deviation from the original Ollama plan; confirm you're on the free tier for the cost-declaration section |
| Open-Meteo | Rainfall tool input, no API key needed | Free, keyless — no secret to protect |
| Cloudinary (optional) / local disk | Incident photo storage | [ADR 0002](adr/0002-incident-photo-storage.md) |
| SMTP / testmail.app | District warning emails, with a test-inbox loop | [ADR 0004](adr/0004-email-notifications.md) |

## 5. Security & validation controls

- Role-based authorization: writes and coordinator actions require `[Authorize]` / `[Authorize(Roles = "EmergencyCoordinator")]`; read endpoints are deliberately anonymous (a citizen must see hazards before creating an account).
- Secrets (Gemini key, Cloudinary, SMTP credentials) come from configuration/environment variables, never committed (`appsettings.Development.json` is git-ignored).
- Output validation: strict enum parsing, numeric clamping, empty-rationale rejection on every agent response before it's trusted.
- Prompt-injection handling: fixed system instruction; citizen text only enters the prompt as labelled data; even a fooled model can only produce a *proposal*.
- Timeouts/retries: Gemini 30s with up to 3 attempts on 429/5xx (attempts recorded on the run); Open-Meteo 5s, fails safe (returns null, not an error).
- Approval integrity: one decision per proposal, newest analysis only, an EF Core concurrency token on the decision timestamp, one transaction for decision + incident change + zone recompute, and the warning email queued only after commit. Violations return 409 Conflict.
- Cost/abuse control: the manual-analysis endpoint is rate limited (`ai` policy).
- Deterministic planning: the Planner never sees a model, so report text cannot change which tools run.
- Global exception handling: unhandled exceptions across the whole API are now caught by `GlobalExceptionHandler` (`IExceptionHandler`), logged, and returned as a safe `ProblemDetails` body with no internal detail leaked outside Development.

## 6. Testing evidence

Results below were produced on 30 September 2026 by running the suites locally; re-run them before you
submit and update the numbers if the code has changed.

- Backend: **375 tests passing** (`dotnet test RescueSriLanka.slnx`, whole suite), of which Component A's
  agent and approval tests are:
  - `IncidentAnalysisAgentTests.cs` — 27 test cases: golden case, planning and delegation (persisted plan
    with three distinct agents, adaptive planner, invalid coordinates), tool recording and skipped tools,
    retry recording, deterministic validation and clamp recording, prompt-injection resistance (3 cases),
    failure recovery, safe failure (unknown incident, unexpected failure marks the run `Failed`).
  - `AgentRunServiceTests.cs` — 19 test cases: approve / revise / reject; one decision per proposal (double
    approve, approve-after-reject, reject-after-approve); Failed/Running runs cannot be decided; newest-run
    rule; note stored; concurrency token configured; no warning email if the zone recompute fails; analyse
    endpoint rate limited.
  - `SeverityRulesTests.cs` — 11 test cases on the rule-engine fallback.
  - `SafetyZoneServiceTests.cs`, `NotificationServiceTests.cs`, `ReportWithPhotoTests.cs`,
    `IncidentReadTests.cs`, `IncidentStatusRollbackTests.cs`.
  - `GlobalExceptionHandlerTests.cs` — 3 tests confirming the exception message is hidden outside Development.
- React: **27 tests passing** (`npm test`, whole suite). Component A's `AgentActivity.test.tsx` — 4 tests:
  plan rendering (delegated agents, tools, planner notes, attempts), a rejection shown as a decision with its
  reason, empty and error states, malformed-plan handling. CI now runs `npm test`.
- Mobile: `auth_test.dart` covers the Component A API client (bearer-token attachment, unauthorized handling,
  `createIncident`); 65 Flutter tests pass in total (`flutter test`).
- CI: GitHub Actions restores, builds and tests the backend, lints/tests/builds React, and analyses/tests
  Flutter on every push.
- **Gaps to disclose honestly:** no React component tests for `DisasterDashboard`, `IncidentTable` or
  `ReportDecision`; no Flutter widget tests for `report_screen.dart` / `disaster_map_screen.dart`; the
  backend tests use the EF Core in-memory provider, so there is no PostgreSQL integration test for the
  migrations, the constraints, the transactional approval or a live concurrency conflict; no load/performance
  test has been run. Say so in the testing report rather than implying full coverage.

## 7. Git / commit evidence

Representative commits touching Component A (`git log -- backend/.../ComponentA frontend/.../componentA mobile/.../component_a docs/component-a-agent.md docs/adr/`):

| Date | Commit | Message |
|---|---|---|
| 2026-09-18 | `078b560` | update5 |
| 2026-09-18 | `b93d73a` | update6 |
| 2026-09-21 | `5132bd6` | update 10 |
| 2026-09-22 | `039d78e` | Restructure code into Component A / Component B folders |
| 2026-09-22 | `aea51b1` | merge- component a and b |
| 2026-09-23 | `685ae0c` | Mobile: place search on the disaster map via OpenStreetMap Nominatim |
| 2026-09-23 | `ba44c97` | Mobile: gate tabs on sign-in, OpenStreetMap tiles, bigger map |
| 2026-09-23 | `5dc69cc` | Mobile: app icon on the splash, Rescue SriLanka in the map header |
| 2026-09-24 | `0605d72` | fix issues with backend and created docker |
| 2026-09-26 | `8ff429a` | profile-fixed, enhanced improvements in report section |
| 2026-09-27 | `d5b3e94` | disaster - improvements |
| 2026-09-28 | `de3ef48` | handler and controller fix — pagination/sorting on incidents, global exception handling |

> The 30 September work (four-agent plan, safe-failure handling, approval guard, the `AddAgentRunPlanAndDecision`
> migration, the React plan panel and the updated tests and docs) was written after this table was compiled.
> Add its commit hashes here once it is committed, and commit it in reviewable pieces rather than one bulk
> upload — the spec explicitly discounts final-day bulk commits.

> A few of the early messages (`update5`, `update6`, `update 10`) are generic — if you have time, this is
> the kind of thing a viva question can probe ("what did this commit actually change?"), so be ready to
> explain each one even though the message itself is thin. Don't rewrite history to fix this now.

## 8. Individual contribution statement (draft — adapt freely)

> I owned Component A end-to-end: incident reporting, the live disaster map, derived safety zones, and the
> Incident Analysis Agent. On the backend this is 20 REST endpoints across four controllers
> (`IncidentsController`, `AgentRunsController`, `SafetyZonesController`, `NotificationsController`), the
> `Incident`/`IncidentImage`/`SafetyZone` data model, and the district-warning email pipeline. The Agentic AI
> contribution is the Incident Analysis workflow: a coordinator that has a deterministic Planner build a
> structured plan and delegates it to three further agents — an Evidence agent (the only one with tools), a
> Severity agent (Gemini with a deterministic rule-engine fallback) and a Validator — with the plan and every
> step persisted, failures recorded safely, and a human-approval gate (one decision per proposal, atomic and
> transactional) that is the only path by which its proposal reaches the live incident. On the frontend, the React "Incident & Disaster Map" dashboard gives
> coordinators filtering, sorting, pagination, a live map, safety-zone view, and the approve/reject/revise
> controls for agent runs and an agent-log view of each run's plan. On mobile, the Flutter report screen uses the camera and GPS to let a citizen file
> a report, and the disaster map shows live incidents and zones. I wrote 4 of the group's Architecture
> Decision Records (LLM provider, photo storage, map tile provider, email delivery) and the backend test
> suite's agent-evaluation tests (46 test cases specifically on the agent and its approval flow).

## 9. Challenges & learning (notes — expand in your own words)

Pick 2–3 of these (or your own) and write a paragraph each with a specific example:

- Deciding to deviate from the group's original "local model, no-cost" plan for Gemini — weighing image
  support and schema reliability against the no-cost-service expectation (this is literally ADR 0001 —
  good source material for "a decision you're prepared to defend in the viva").
- Making the agent *fail safely* rather than fail loudly — designing `SeverityRules` as a genuine fallback,
  not a placeholder, so an incident is never left unscored even with no model configured.
- Photo storage portability — realizing local-disk storage doesn't survive a container redeploy, and what
  that meant for the agent's vision step (ADR 0002).
- Retrofitting pagination/sorting onto an endpoint three other screens already depended on (the Flutter map,
  and Component D's mobile coordination screen) without breaking either — had to design the API change to be
  fully backward-compatible (optional params, unchanged response shape when they're omitted) rather than
  just changing the contract.
- Turning one agent into a plan-and-delegate workflow without letting report text steer it — deciding that the
  Planner, Evidence and Validator should be deterministic code and only the Severity agent should use a model.
- Making approval trustworthy: a run could be approved twice, approved after a rejection, or approved after a
  newer analysis existed, and each approval re-sent the district warning. Fixing it meant a one-decision rule,
  a concurrency token, a transaction and queuing the email only after commit.
- Adding global exception handling late — realizing individual try/catch blocks in a couple of endpoints
  weren't the same thing as a safety net for the whole API.

## 10. Individual AI usage log (real entries from this project's AI-assisted work)

Fill in exact dates/tool-model details; the factual content (task → output → what you verified) below is
accurate to what happened and can be used as-is:

| Date | Tool / model | Task | What it produced | What I verified |
|---|---|---|---|---|
| *(fill in)* | Claude Code (claude-sonnet-5) | Review Component A against the assignment spec | A gap analysis (missing pagination/sorting, no global exception handler, no root README, test coverage gaps) | Read the flagged code myself before accepting each claim |
| *(fill in)* | Claude Code (claude-sonnet-5) | Add pagination + sorting to `GET /api/incidents` and the React queue table | Backend query/controller changes, a non-breaking `apiFetchPage` helper, sortable table headers, pagination controls | Ran `dotnet build`, `dotnet test` (233/233 relevant tests passing), `npm run build`, `npm run lint` before accepting; checked it didn't break the Flutter map or Component D's mobile screen, which call the same endpoint |
| *(fill in)* | Claude Code (claude-sonnet-5) | Add global exception-handling middleware | `GlobalExceptionHandler : IExceptionHandler`, registration in `Program.cs`, 3 new tests | Ran the full test suite (236/236 passing), reviewed the handler's logic (Development-only exception detail) myself |
| *(fill in)* | Claude Code (claude-sonnet-5) | Audit Component A against the assignment rubric | A list of gaps: single agent with no plan or delegation, runs left `Running` after unexpected errors, no guard on repeat or reversed approvals, plan not visible in the UI, and others | *(write what you personally checked in the code before agreeing with each gap)* |
| *(fill in)* | Claude Code (claude-sonnet-5) | Redesign the agent as coordinator + Planner / Evidence / Severity / Validator, add safe failure handling and the one-decision approval guard | `IncidentAnalysisRoles.cs`, rewritten `IncidentAnalysisAgent.cs`, `AgentRunService` changes, new `Decision` / `DecisionNote` / `ModelAttempts` / `PlanJson` columns and migration, 502 handling and rate limit on `analyse`, and new tests | Backend suite run after each change (375 passing at the end). *(add: which code you read line by line, what you changed or rejected, and how you would explain the transaction and concurrency token in the viva)* |
| *(fill in)* | Claude Code (claude-sonnet-5) | Add the plan panel to the React agent log and its tests; run `npm test` in CI | `AgentActivity.tsx` Plan column, `agentPlan.ts`, `AgentActivity.test.tsx`, CI step | `npm test` (27 passing), `tsc -b` and `npm run lint` clean. *(add what you checked in the browser)* |
| *(fill in)* | Claude Code (claude-sonnet-5) | Update `component-a-agent.md` and this report | The current versions of both documents | *(cross-check every figure and file name against the code and your own test run before using it)* |
| *(fill in)* | Claude Code (claude-sonnet-5) | Draft this report scaffold and a root README | This file; a project README (later removed/reworked) | Cross-checked factual claims (endpoint counts, test counts, seed accounts, config keys) against the actual source files before use |

## 11. AI reflection (~1 page) — write this yourself

**Do not submit AI-written reflection prose.** The spec is explicit: *"A reflection that is AI-generated...
will not receive credit."* What follows are honest prompts based on what actually happened in this project —
answer them in your own words, from your own memory of doing the work:

1. **Which AI tools, and at which stages?** — e.g. code generation/refactoring for the backend, drafting
   documentation, gap-analysis against the spec. Be specific about stages (design vs. debugging vs. writing).
2. **What did it do well, and what did it get wrong?** — Did it correctly identify real gaps? Did any
   suggested change need correcting once you tested it? Did it ever propose something that would have broken
   another component (the pagination change touched an endpoint 3 other screens use — did you catch that, or
   did the AI, and what did you check before trusting it)?
3. **What did you change, add or reject, and why?** — Any AI output you rewrote, rejected, or extended
   yourself. Even accepting a suggestion "as-is" is a decision — say why it was the right call, not just that
   it worked.
4. **What did you learn about your own skills and understanding?** — This is the part that can't be
   generated for you. What can you now explain that you couldn't before? Where did you have to slow the AI
   down and check its work line-by-line before you understood *why* it was right?

## 12. Signed declaration (template)

> I confirm that I can explain, test, modify and debug all code submitted under my name for Component A,
> that all AI tool use is disclosed above and complies with Section 18 of the assignment specification, and
> that this report accurately reflects my own work and understanding.
>
> **Name:** Lelum Jayasooriya (IT24101153) **Date:** *(fill in)* **Signature:** *(fill in)*
