# RescueSriLanka — Frontend UI Guide

A walkthrough of the web console for anyone working on its UI: what each screen does, where its data comes from, the design rules, and what still needs building.

The web app is the **Emergency Coordination Console**. Staff use it to watch incidents across Sri Lanka, check them, and approve or reject the AI's severity assessments. Citizens report incidents from the Flutter mobile app in [`mobile/`](../mobile). Both apps call the same ASP.NET Core API in [`backend/`](../backend).

---

## 1. Run it locally

```bash
cd frontend
npm install
npm run dev          # http://localhost:5173
```

The console needs the API running at `http://localhost:5093`:

```bash
cd backend/RescueSriLanka.Api
dotnet run           # needs .NET 10
```

The backend reads its database connection string from `appsettings.Development.json`. That file is gitignored, so **ask Lelum for a copy** and don't commit it. When `SeedSampleIncidents` is `true` in that file, the API loads sample incidents, which gives the dashboard data to show.

**Sign in with** (one login page; the account decides which dashboard opens):

| Account | Role | Opens |
|---|---|---|
| `coordinator@rescue.lk` / `Rescue@123` | Emergency Coordinator | Disaster dashboard (Component A) |
| `helprequests@rescue.lk` / `Rescue@123` | Help Request Manager | Help request dashboard (Component B) |

### Environment variables (`frontend/.env`, gitignored)

| Variable | Default | Purpose |
|---|---|---|
| `VITE_API_URL` | `http://localhost:5093` | API base URL |
| `VITE_MAPBOX_TOKEN` | *(none)* | Uses Mapbox tiles. Without a token, the maps use OpenStreetMap. |
| `VITE_MAPBOX_STYLE` | `mapbox/streets-v12` | Mapbox style id |

Never commit a Mapbox token. GitHub push protection blocks them. See [ADR 0003](adr/0003-map-tile-provider.md).

### Scripts

`npm run dev` · `npm run build` (type-check + build) · `npm run lint` (oxlint) · `npm run preview`

CI builds the frontend on every push, so `npm run build` must pass before you push.

---

## 2. Stack

- **React 19 + TypeScript + Vite 8**
- **Leaflet / react-leaflet 5** for maps
- **Plain CSS** with custom properties (design tokens). No Tailwind, no component library.
- **React Router** for top-level console pages only. `App.tsx` shows the login page or the console depending on whether a session exists; the console shell maps URLs to pages. Dashboard tabs are React state, not URLs.
- **No state library.** `DisasterDashboard.tsx` fetches the data and passes it down as props.

```
frontend/src/
├── App.tsx                        # session gate: Login ↔ Console
├── index.css                      # global tokens + base styles  ← start here
├── shared/                        # used by every component
│   ├── api/client.ts              # apiFetch(): bearer token, 401 → sign out
│   ├── auth/                      # signIn(), session storage, role labels
│   ├── config/tiles.ts            # Mapbox / OSM tile switch
│   ├── layout/ConsoleShell.tsx    # top bar, nav bar and routes
│   ├── pages/Login/               # LoginPage.tsx + .css — the one sign-in page
│   └── types.ts                   # types shared across components (AgentRun)
└── components/
    ├── componentA/                # Incident & Disaster Map
    │   ├── DisasterDashboard.tsx  # Disaster dashboard (/): tabs, filters, detail drawer
    │   ├── DisasterDashboard.css  # ~1,450 lines — all dashboard styling
    │   ├── severity.ts            # colour / size / label maps, timeAgo()
    │   ├── types.ts               # TS types mirroring the incident API DTOs
    │   └── sections/              # one file per panel (see §4)
    └── componentB/                # Help requests
        ├── HelpRequestDashboard.tsx  # Help request dashboard (/dashboard): map, priority queue
        ├── HelpRequestsReview.tsx   # coordinator review and verification
        └── api.ts                 # authFetch() on the shared session
```

---

## 3. Screen map

```
┌─ Login ───────────────────────────────────────────┐
│  Brand panel (left)       │   Sign-in card (right) │
└───────────────────────────────────────────────────┘
                     │ signed in
                     ▼
┌─ Console shell ───────────────────────────────────┐
│ [logo] RescueSriLanka          Name · Role [Sign out]
├───────────────────────────────────────────────────┤
│ Incident & Disaster Map            Updated… [Refresh]
│ [Overview][Disaster map][Safety zones][Queue][Agent]
│ (filters: severity · status · type — Map & Queue only)
│                                                   │
│   …active tab content…                ┌─ Drawer ─┐│
│                                       │ incident ││
│                                       │ detail   ││
│                                       └──────────┘│
└───────────────────────────────────────────────────┘
```

Clicking an incident on any tab (map marker, table row, feed item, map-side list) opens the **incident detail drawer** on the right.

---

## 4. Screens in detail

### 4.1 Login — `shared/pages/Login/LoginPage.tsx`

Two columns: an amber brand panel with a tagline, three feature ticks and a Safe/Caution/Danger legend, and the sign-in card. Below 900px they stack into one column.

- Fields: email, password with a Show/Hide toggle, and a "Keep me signed in" checkbox. When ticked, the session goes in `localStorage`; otherwise it goes in `sessionStorage`.
- Client-side validation: email is required and must be valid; password is required and at least 8 characters. Errors are linked to fields with `aria-describedby`.
- Server errors (wrong credentials, API down) show in an alert banner above the form.
- While submitting, the button shows a spinner and "Signing in…", and the inputs are disabled.
- ⚠️ The "Forgot password?" link goes to `#forgot` and does nothing yet.

### 4.2 Console shell — `shared/layout/ConsoleShell.tsx`

A top bar with the logo, the user's full name and role label, and a ghost **Sign out** button, then a nav bar for the top-level pages. The matching route renders inside `<main>`. Each component has its own dashboard page: **Disaster dashboard** (`/`, Component A) and **Help request dashboard** (`/dashboard`, Component B), plus B's **Request review**. There is one sign-in page; the account's role decides which pages the nav shows (`pagesFor()`): Help Request Managers get B's pages, everyone else A's. Any other URL redirects to the account's own dashboard.

### 4.3 Dashboard frame — `components/componentA/DisasterDashboard.tsx`

- Header: title, subtitle, "Updated HH:MM:SS" stamp, **Refresh** button (shows "Refreshing…" while loading).
- **Tabs:** each tab shows a label with a small hint underneath.
- **Filters** (shown on the *Disaster map* and *Incident queue* tabs only): Severity, Status (Reported / Verified / In progress), Type. A "Clear filters" button appears when any filter is set.
- One data load feeds every tab: statistics, incidents (filtered, active only) and safety zones, fetched in parallel. A new filter cancels the previous request so stale results never overwrite new ones.
- Errors show as one alert banner below the tabs.
- There's no auto-refresh; data only reloads when the user clicks Refresh or changes a filter.

### 4.4 Tab: Overview — `sections/OverviewSection.tsx`

| Panel | File | Shows |
|---|---|---|
| Stat row | `StatRow.tsx` | 6 tiles: Active incidents, Critical, Awaiting verification, People affected, Danger zones, Awaiting AI analysis. Tiles have a coloured left border for tone (critical / caution / default). |
| Recent activity | `ActivityFeed.tsx` | 6 newest incidents: severity chip, title, district · status · time ago. Clickable. |
| Processing coverage | `CoveragePanel.tsx` | Two progress meters: *AI analysed* and *Human verified*, as x/total and %. |
| Distributions | `DistributionPanel.tsx` | 3 horizontal bar lists: by severity (severity colours), by disaster type, and by district (top 8), both in a neutral colour. |
| Safety zones | `ZonePanel.tsx` | Zones sorted Danger → Caution → Safe, with radius and derived/manual source. |

### 4.5 Tab: Disaster map — `sections/MapSection.tsx` + `IncidentMap.tsx`

- A Leaflet map, 520px tall, with a 280px side list ranking incidents Critical → Low.
- **The map can't be panned outside Sri Lanka** (bounds `[5.70, 79.40]`–`[10.00, 82.10]`, zoom 7–16). A **Fit island** button resets the view.
- **Incident markers** are circles. Fill colour = severity, and **radius also encodes severity** (6 / 7.5 / 9.5 / 12px). The selected marker gets +4px and a dark outline, and the map flies to it. Clicking a marker opens a popup and the drawer.
- **Zones** are translucent circles at their real radius in metres. Dashed outline = derived automatically from an incident; solid = manual override. A "Safety zones" switch in the panel header shows or hides them.
- A legend below the map repeats both the colours and the marker sizes.

### 4.6 Tab: Safety zones — `sections/ZonesSection.tsx`

- Stat row: Danger / Caution / Safe counts, Auto-derived vs manual, Area covered (km²).
- A 400px map showing **zones only** (incident markers hidden).
- A table with Status chip, Zone name, District, Radius, Source, Rationale.

### 4.7 Tab: Incident queue — `sections/IncidentTable.tsx`

A full table with columns Severity · Incident · Type · District · Status · AI score · Affected · Reported. An **"overridden"** tag appears when a coordinator changed the AI's severity. The AI score shows *pending* until the agent has run. Clicking a row opens the drawer, and the selected row is highlighted.

### 4.8 Tab: Agent activity — `sections/AgentActivity.tsx`

Shows the log of every AI agent run (up to the latest 100).

- Stat row: Total runs, Model-backed, Rule-engine fallback, Approved, Avg duration (ms).
- Table: Agent, Status badge (ok / warn / bad; `SucceededWithFallback` shows as "Fallback"), Model, Duration, Approval (Awaiting / Approved / Rejected), Started, Note.

### 4.9 Incident detail drawer (in `DisasterDashboard.tsx`)

A 420px panel that slides in from the right over a scrim (full width on phones). Clicking the scrim or × closes it. From top to bottom:

1. **Header:** severity chip and close button.
2. **Title**, then *type · district · time ago*, then the description.
3. **Facts grid:** Status, Affected radius (km), People affected, Coordinates.
4. **Photos** (`IncidentPhotos.tsx`): a thumbnail strip. Clicking a thumbnail opens a full-screen lightbox, and clicking again closes it. Shows an empty message when there are no photos.
5. **Report triage** (`ReviewPanel.tsx`): moves the report through its lifecycle.
   ```
   Reported ──► Verified ──► In progress ──► Resolved
       └──────► Rejected          └──────────► Resolved
   ```
   Resolved and Rejected ask for confirmation first, because they remove the incident from the live map and drop its zone. Closed reports show "Nothing further to decide."
6. **Incident Analysis Agent** (`AgentPanel.tsx`): the human approval step for the AI.
   - **Run analysis / Re-run analysis** button.
   - The proposal shows a score out of 100, a severity chip, the recommended zone status and radius, and a confidence %, followed by the AI's rationale.
   - A badge shows which model produced it, or **"Rule engine"** (amber) when the LLM was unavailable and fallback rules were used.
   - Actions: **Approve** (with an optional "Revise" severity dropdown, which changes the label to "Approve as High") or **Reject proposal**, which reveals a required reason input.
   - Once a decision is made, a green or red line says what happened.

> Triage and AI approval are **separate decisions on purpose**. A coordinator can verify that a report is real while still rejecting the AI's severity for it. Keep them visually separate in any redesign.

After any triage or agent action, the dashboard reloads and the drawer fetches the incident again, so it stays open showing the new state.

---

## 5. Design system

Tokens are in [`frontend/src/index.css`](../frontend/src/index.css). Use the variables; don't hard-code hex values in CSS.

### 5.1 The colour rule (important)

> **Chrome is white / grey / near-black. Amber is the only brand accent.**
> **Green, amber, orange and red are reserved for severity and zone status.**
> Never use them for decoration, or a red incident marker stops reading as urgent.

### 5.2 Neutral chrome

| Token | Value | Use |
|---|---|---|
| `--bg` / `--surface` | `#ffffff` | Page and card background |
| `--surface-alt` | `#f5f7fa` | Subtle fills, table headers |
| `--border` | `#e4e8ee` | Default borders |
| `--border-strong` | `#cbd3de` | Emphasised borders |
| `--text` | `#5b6675` | Body text |
| `--text-strong` | `#0b0e13` | Headings, key values |

### 5.3 Brand accent (amber)

| Token | Value | Use |
|---|---|---|
| `--accent` | `#faa71b` | Fills: primary buttons, dots (matches the logo) |
| `--accent-hover` | `#e59310` | Hover on fills |
| `--accent-strong` | `#b45309` | Amber **text** and links (passes contrast on white) |
| `--accent-ink` | `#17120a` | Text sitting on an amber fill |
| `--accent-soft` / `--accent-ring` | amber at 12% / 32% | Soft backgrounds, focus rings |

### 5.4 Severity and zone scale (reserved)

| Severity | Zone | Fill | Text on soft bg | Soft bg |
|---|---|---|---|---|
| Low | Safe | `--safe` `#12946a` | `--safe-ink` `#0b6b4c` | `--safe-soft` |
| Moderate | Caution | `--caution` `#e5a800` | `--caution-ink` `#8a6500` | `--caution-soft` |
| High | — | `--high` `#e35d0b` | `--high-ink` `#a03f06` | `--high-soft` |
| Critical | Danger | `--critical` `#9c1c3d` | `--critical-ink` `#7a1530` | `--critical-soft` |

- These values were **checked for colour-blind separation** (protan, deutan and tritan). Don't re-tune them by eye; the obvious amber/orange and green/amber values fail those checks.
- **Never show severity by colour alone.** Always pair it with the text label, and on the map with marker size too. Amber is below 3:1 contrast on white.
- The TypeScript mirrors of these are in `components/componentA/severity.ts`: `SEVERITY_TOKEN`, `SEVERITY_HEX`, `SEVERITY_RADIUS`, `ZONE_HEX`, `ZONE_TOKEN`.
- Errors and destructive actions use `--danger` `#dc2626`, which is separate from the severity red.

### 5.5 Shape, depth and type

| Token | Value |
|---|---|
| `--radius-sm` / `--radius` / `--radius-lg` | 8 / 12 / 18px |
| `--shadow-sm` | small resting shadow for cards |
| `--shadow` | large shadow for the drawer and popovers |
| `--sans` | system UI font stack, 16px / 1.5 |
| `--mono` | system monospace |

Headings use weight 600 with −0.02em letter spacing. There's **no web font**; everything uses the system stack.

### 5.6 Breakpoints

| Width | What changes |
|---|---|
| ≤ 960px | The map's side list moves below the map |
| ≤ 900px | Login goes to a single column |
| ≤ 720px | Top bar, dashboard header, bar lists, facts grid and map legend compact for phones |
| ≤ 420px | Stat tiles stack in one column |

Panel grids don't need breakpoints: they use `auto-fit` with minimum widths (320px for 2-up, 260px for 3-up, 168px for stat tiles), so they wrap on their own.

`prefers-reduced-motion` is respected on the login page. The app is **light mode only** (`color-scheme: light`).

### 5.7 Reusable class names

The CSS uses BEM-style names (`block__element--modifier`). The main building blocks:

| Class | What it is |
|---|---|
| `.panel`, `.panel__head`, `.panel__title`, `.panel__meta` | Card with a header row |
| `.stat-row`, `.stat`, `.stat--critical`, `.stat--caution` | Stat tile grid (auto-fit, min 168px) |
| `.chip.chip--safe \| caution \| high \| critical` | Severity/zone pill; always contains the label |
| `.badge.badge--ok \| warn \| bad` | Run status pill (not for severity) |
| `.table-wrap`, `.table`, `.num`, `.col-wide`, `.cell-title` | Data tables |
| `.btn` (login), `.btn-ghost`, `.btn-small`, `.btn-approve`, `.btn-reject`, `.btn-link` | Buttons |
| `.alert` | Error banner |
| `.empty` | Empty-state text |
| `.drawer`, `.drawer__scrim` | Detail side panel |
| `.lightbox` | Full-screen photo viewer |

---

## 6. Data and API

All calls go through `apiFetch()` in `shared/api/client.ts`. It adds `Authorization: Bearer <token>`, and a **401 clears the session** so the user lands back on the login page. The response types are in [`frontend/src/components/componentA/types.ts`](../frontend/src/components/componentA/types.ts) and [`frontend/src/shared/types.ts`](../frontend/src/shared/types.ts).

| Method | Endpoint | Used by | Who |
|---|---|---|---|
| POST | `/api/auth/login` | Login | anyone |
| GET | `/api/incidents/statistics` | Stat tiles, meters, distributions | anyone |
| GET | `/api/incidents?severity=&status=&type=&activeOnly=true` | Map, queue, feed | anyone |
| GET | `/api/incidents/{id}` | Drawer refresh | anyone |
| GET | `/api/safetyzones` | Map zones, zone panels | anyone |
| PATCH | `/api/incidents/{id}/status` `{ status }` | Report triage | Coordinator |
| POST | `/api/incidents/{id}/analyse` | Run analysis | Coordinator |
| GET | `/api/agentruns?incidentId=&take=` | Agent panel, agent log | signed in |
| POST | `/api/agentruns/{id}/approve` `{ severity \| null }` | Approve / approve as… | Coordinator |
| POST | `/api/agentruns/{id}/reject` `{ reason }` | Reject proposal | Coordinator |

The API has these endpoints that **the web UI doesn't use yet**: `POST /api/auth/register`, `GET /api/auth/me`, `PATCH /api/auth/me/preferences`, `GET /api/districts`, `GET /api/incidents/nearby`, `POST /api/incidents` (create), `POST /api/incidents/{id}/images`, `PATCH /api/incidents/{id}/severity` (direct override), `GET /api/safetyzones/check`, `POST /api/safetyzones/recompute`, `POST /api/notifications/test`, `GET /api/notifications/inbox`.

Note that `UserDto` now also carries `district` and `emailNotificationsEnabled`, which drive the email warnings described in [docs/email-notifications.md](email-notifications.md). Verifying an incident or approving its severity in the console can now send an email to every citizen subscribed to that district, so those buttons reach further than the map.

### Enum values (they must match the API exactly)

- **Severity:** `Low` `Moderate` `High` `Critical`
- **Status:** `Reported` `Verified` `InProgress` `Resolved` `Rejected` (display `InProgress` as "In progress" via `STATUS_LABEL`)
- **Type:** `Flood` `Landslide` `Fire` `Accident` `Storm` `Tsunami` `Other`
- **Zone status:** `Safe` `Caution` `Danger`, with zone source `DerivedFromIncident` or `ManualOverride`
- **Roles:** `Citizen` `EmergencyCoordinator` `ResourceManager` `RescueTeam` `HelpRequestManager`

### Things to watch out for

- **Photo URLs are either absolute or relative.** Cloudinary returns full `https://…` URLs, while the local-disk fallback returns a path relative to the API. Use the `resolve()` helper in `IncidentPhotos.tsx`.
- **`outputJson` on an agent run is a JSON string.** Parse it into `AnalysisProposal`, and handle a parse failure.
- **Mapbox tiles are 512px** and need `tileSize: 512, zoomOffset: -1`. `shared/config/tiles.ts` already handles this.
- **Only Emergency Coordinators can act.** Other roles get 403 on triage and approval. The UI doesn't hide those buttons by role yet.

---

## 7. Open UI work and gaps

These are the places where UI help would matter most:

- [ ] **Forgot password** flow (the link is currently dead)
- [ ] **URL routing**: tabs and the open incident aren't in the URL, so a refresh resets the view and nothing can be linked
- [ ] **Keyboard support**: Esc doesn't close the drawer or lightbox, and focus isn't trapped inside the drawer
- [ ] **Hide actions by role**: only show triage and approval controls to `EmergencyCoordinator`
- [ ] **Loading skeletons**: panels currently appear all at once when data arrives
- [ ] **Auto-refresh or live updates** for the operational picture
- [ ] **Teams, shelters and supplies** screens (the login page mentions them, but they aren't built)
- [ ] **Manual zone tools**: create or override a zone, and a "Recompute zones" button (the endpoint exists)
- [ ] **Dark mode** (the tokens are in place, but nothing redefines them for dark)
- [ ] **Sidebar navigation** in the console shell, once there's more than one page

---

## 8. Conventions

- One component per file under `sections/`, with a short doc comment explaining *why* it exists.
- Fetches inside `useEffect` use an `AbortController` and check `signal.aborted` before setting state.
- Every list has an empty state, and every async action has a busy label ("Applying…", "Analysing…") and an inline error.
- Pair every severity colour with its text label.
- Put new colours in `index.css` as tokens, never inline hex in components. The exception is the Leaflet `pathOptions`, which need raw hex from `severity.ts`.
