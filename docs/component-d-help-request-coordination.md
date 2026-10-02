# Help Request rescue coordination

The Rescue Coordinator uses backend role `RescueTeam`. Component B retains ownership of Help Request creation, verification, status changes and cancellation. Component D reads these records through `IHelpRequestReadService`, using `AsNoTracking`, and never updates them.

## Endpoints and eligibility

- `GET /api/rescue/help-requests`: limited coordination fields for Pending + Verified requests without current response work. No citizen identity, contact details, images or verification notes are returned. District and beneficiary count are omitted because the source has no request-specific values for them. Non-finite coordinates are represented as null in this read DTO; the source is unchanged.
- `POST /api/rescue/help-requests/{id}/recommend-team`: requires an explicit `requiredSkill` and positive integer `requiredCapacity`. Both endpoints require `RescueTeam`; other roles receive 403 (anonymous callers receive 401). Component B management permissions are unchanged.

Required capacity means people/patients requiring vehicle transport, not total beneficiaries. The Rescue Coordinator must confirm it; the Help Request form does not assume one person or infer a skill from the request category.

## Trusted candidates and AI

Candidates require an available team, an available member with the selected skill, valid request and registered base coordinates, and an available team-owned vehicle with sufficient carrying capacity. Missing, non-finite and out-of-range coordinates prevent matching; zero coordinates are valid.

`ActiveResponseWork` preserves the existing conflict predicate: assignments that are neither Rejected nor Cancelled with no dispatch, or with a dispatch other than Resolved/Cancelled, block the Help Request, team and vehicle. Pending, Dispatched, EnRoute and OnScene dispatches therefore remain active. Revision excludes its own assignment from these checks.

Haversine distance uses the existing `GeoService`, from registered team base to request. Ranking is distance ascending, team ID ascending, vehicle capacity ascending, then vehicle ID ascending. Distance is straight-line; it provides neither road routing nor an ETA.

The focused Gemini adapter receives candidate facts only and has no database or mutation tools. It explains the nearest suitable pair. The backend accepts only that pair's IDs and a bounded list of allowed rationale codes, including NEAREST_BASE. It renders those AI-selected reasons using trusted distance, skill, capacity and availability facts; arbitrary model prose and invented facts are never displayed. Trusted facts always come from the deterministic candidate DTO. AI failure, timeout, malformed output or invalid IDs retains the deterministic choices and nearest recommendation. AI prose is explanatory, never an authorization input. The auto-creating `AgentOrchestrator` is not used.

## Rescue Coordinator workflow

In Assignments, choose Help Request responses, review a request, select a skill, confirm transport demand, and request a recommendation. The map displays the request and eligible registered bases, with distinct recommended and selected team highlights. It explicitly states: "Team markers show registered bases, not live positions." Distances are labeled "Straight-line distance".

Use recommended team and Choose another eligible team only select resources. Only Create response plan posts an assignment: selected `helpRequestId`, null `incidentId`, selected team/vehicle, explicit skill and capacity, and notes. Creation rechecks request eligibility, duplicate work and current resource suitability. Relational Help Request creation uses a serializable transaction; concurrent changes require refreshing. No team or vehicle status is changed by proposal creation. Existing incident assignments and Help Request plan revision remain supported.

Creation opens the existing AI safety review. The Rescue Coordinator runs validation and makes the final human decision; the backend then performs live revalidation and transactional dispatch. The ten mandatory checks remain ASSIGNMENT_EXISTS, PLAN_VERSION_MATCHES, TEAM_AVAILABLE, REQUIRED_SKILL_PRESENT, TEAM_CONFLICT, VEHICLE_EXISTS, VEHICLE_OWNERSHIP, VEHICLE_AVAILABLE, VEHICLE_CAPACITY and VEHICLE_CONFLICT.

This phase does not synchronize Component B status with deployment. A still-Pending + Verified request can become eligible again after its Component D work terminates; Component B remains responsible for its status. The existing safety checks validate the response plan and resources, not a new cross-component Help Request lifecycle transaction.

## Verification

`HelpRequestCoordinationTests` covers request eligibility, assignment objectives, duplicate work, revision, skill/availability/capacity/location filters, active versus terminal conflicts, deterministic ranking, invalid AI IDs and read-only fallback. `HelpRequestCoordinationApiTests` covers authorization, minimal DTO fields, input validation and recommendation non-mutation. These tests use isolated in-memory databases and fake AI.

`HelpRequestCoordination.test.tsx` covers explicit inputs, manual override, mutation only on submission, stale recommendations, retry/empty queue, refresh, duplicate submission, invalid coordinates, map highlights and safety-review integration. Existing assignment, incident selection, safety-agent and dispatch suites provide regression coverage.
