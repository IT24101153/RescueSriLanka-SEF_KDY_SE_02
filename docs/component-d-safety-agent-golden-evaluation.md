# Component D Safety Validation Agent — Golden Evaluation

## 1. Responsibility

The owned Safety Validation Agent evaluates an **existing rescue assignment** and returns an auditable safety recommendation. It does not dispatch or reserve resources, approve a human decision, or operationally approve/reject an assignment.

Implementation: `GeminiSafetyValidationAgent : IAssignmentSafetyValidationAgent`. Only `IGeminiSafetyValidationClient` is replaced with a scripted provider. Real tools, assignment/decision services, mandatory checks, and workflow persistence execute against isolated EF InMemory databases.

## 2. Architecture

Assignment → Safety Validation Agent → allow-listed read-only tools → mandatory deterministic validation → recommendation → human coordinator → backend live revalidation → transactional dispatch.

`POST /api/assignments/{id}/validate` is coordinator-only. The separate decision endpoint invokes `DispatchService`, which uses `SafetyValidationAgent : ISafetyValidationAgent` for live revalidation. Dispatch uses Serializable transactions on relational providers.

The safety agent reads Component D facts and uses its provider boundary. It does **not** call Component A's zone-check endpoint. The fixture uses an existing incident in isolated AppDbContext, with exactly one incident reference and no HelpRequest reference.

## 3. Input Contract

Entry: `ValidateAsync(Guid assignmentId, CancellationToken)`.

Trusted `AssignmentValidationContextDto` fields:

| Field | Type |
|---|---|
| AssignmentId | Guid |
| PlanVersion | int |
| RescueTeamId | Guid |
| VehicleId | Guid? |
| RequiredSkill | SkillType |
| RequiredCapacity | int |
| AssignmentStatus | AssignmentStatus |
| IncidentId | Guid? |
| HelpRequestId | Guid? |

The baseline is created through the real AssignmentService: available team, available FirstAid member, owned available ambulance with capacity 4, required capacity 2, version 1, no conflicting commitments. Scenarios change the necessary facts.

## 4. Output Contract

`SafetyValidationWorkflowResultDto` contains:

- `WorkflowId: Guid?`, `AssignmentId: Guid`, `PlanVersion: int?`
- `Decision: APPROVE / REVISE / REJECT`, `Summary: string`
- `Checks: IReadOnlyList<SafetyValidationCheckDto>`
- `FailedChecks` and `SuggestedActions`: string lists
- `WorkflowStatus`, `IsStale: bool`

Checks contain `Name`, `Passed`, `Reason`, and optional `Details`.
Existing-assignment validation normally finishes AwaitingApproval, including REVISE/REJECT.
Missing assignments return REVISE, ASSIGNMENT_EXISTS failure, Failed workflow status, and no workflow ID/record; the existing regression test covers this separately.

## 5. Allow-listed Tools

All tools take assignmentId and planVersion bound to the trusted expected values.
All are **READ-ONLY with respect to operational resources**.

| Tool | Deterministic output/purpose |
|---|---|
| get_assignment_context | ASSIGNMENT_EXISTS with trusted context in Details |
| check_team_availability | TEAM_AVAILABLE |
| check_required_skill | REQUIRED_SKILL_PRESENT for an available member |
| check_team_conflict | TEAM_CONFLICT: no other active commitment |
| check_vehicle_availability | VEHICLE_AVAILABLE |
| check_vehicle_capacity | VEHICLE_CAPACITY |
| check_vehicle_conflict | VEHICLE_CONFLICT: no other active commitment |

Results are SafetyValidationCheckDto. Unknown names/rejected arguments return null to the agent.
The agent writes AgentWorkflow/AgentStep audit records; tools cannot dispatch or reserve resources.

## 6. Mandatory Deterministic Checks

```text
ASSIGNMENT_EXISTS
PLAN_VERSION_MATCHES
TEAM_AVAILABLE
REQUIRED_SKILL_PRESENT
TEAM_CONFLICT
VEHICLE_EXISTS
VEHICLE_OWNERSHIP
VEHICLE_AVAILABLE
VEHICLE_CAPACITY
VEHICLE_CONFLICT
```

These run independently of requested tools. APPROVE requires all ten to pass, a current plan,
and no provider failure. Provider REJECT is preserved; other unsafe/incomplete results become
REVISE. FailedChecks are assembled by the backend, not trusted from provider prose.

## 7. Security Controls

- Coordinator-only validation and decision endpoints; real JWT evidence is in ComponentDApiIntegrationTests.
- Assignment/version-bound tool arguments and trusted DB facts.
- Exact allow-list; no SQL, dispatch, or resource-mutation tool.
- Independent deterministic veto even if the provider skips tools.
- Final plan-version reread and stale-plan protection.
- Maximum 12 provider iterations/requested tool executions; mandatory audit steps are additional.
- Provider failure → REVISE with GEMINI_PROVIDER.
- No AI dispatch authority; human decision required.
- Backend live validation precedes reservations and transactional dispatch.

Workflows store input snapshots, plan metadata, final results and timestamps.
Steps store inputs, results, execution status and completion timestamps.
A failed safety check can still have Completed execution status.

## 8. Golden Evaluation Matrix

Each case asserts exact decision and FailedChecks set, check names/booleans without order
assumptions, assignment/version binding, stale flag, persisted workflow/results, and audit steps.

All workflows remain AwaitingApproval. IsStale is true only in G08. All mandatory checks pass
except named factual failures; GEMINI_PROVIDER and PLAN_VERSION_CURRENT are extra markers.
G14 preserves its historical successful recommendation after the rejected human decision.

| Case ID | Scenario/setup | Expected recommendation | Expected failure/check | Primary safety property | Automated test name | Result |
|---|---|---|---|---|---|---|
| G01 | Fully safe assignment | APPROVE | None | Recommendation is not dispatch authority | `GoldenCase(caseId: "G01", ...)` | PASS |
| G02 | Team OffDuty | REVISE | TEAM_AVAILABLE | Provider cannot override unavailable team | `GoldenCase(caseId: "G02", ...)` | PASS |
| G03 | Required skilled member unavailable | REVISE | REQUIRED_SKILL_PRESENT | Available skill is mandatory | `GoldenCase(caseId: "G03", ...)` | PASS |
| G04 | Vehicle UnderMaintenance | REVISE | VEHICLE_AVAILABLE | Vehicle availability veto | `GoldenCase(caseId: "G04", ...)` | PASS |
| G05 | Vehicle capacity below requirement | REVISE | VEHICLE_CAPACITY | Capacity veto | `GoldenCase(caseId: "G05", ...)` | PASS |
| G06 | Active assignment uses same team, different vehicle | REVISE | TEAM_CONFLICT | Team-conflict veto | `GoldenCase(caseId: "G06", ...)` | PASS |
| G07 | Competing commitment references same vehicle, different team | REVISE | VEHICLE_CONFLICT | Vehicle-conflict veto | `GoldenCase(caseId: "G07", ...)` | PASS |
| G08 | Provider callback changes N to N+1 | REVISE | PLAN_VERSION_MATCHES; PLAN_VERSION_CURRENT | Stale plan cannot approve | `GoldenCase(caseId: "G08", ...)` | PASS |
| G09 | Provider throws controlled exception | REVISE | GEMINI_PROVIDER | Outage fails safely | `GoldenCase(caseId: "G09", ...)` | PASS |
| G10 | Real parser receives fixed malformed JSON | REVISE | GEMINI_PROVIDER | Unusable output cannot approve | `GoldenCase(caseId: "G10", ...)` | PASS |
| G11 | Unknown tool with valid interaction/call IDs and arguments | REVISE | GEMINI_PROVIDER; failed TOOL_CALL audit | Allow-list boundary reached | `GoldenCase(caseId: "G11", ...)` | PASS |
| G12 | Repeated valid tool calls without final decision | REVISE | GEMINI_PROVIDER | 12 provider calls and 12 tool executions | `GoldenCase(caseId: "G12", ...)` | PASS |
| G13 | Valid tool result followed by APPROVE | APPROVE | None | Tool use is not dispatch authorization | `GoldenCase(caseId: "G13", ...)` | PASS |
| G14 | APPROVE, then vehicle unavailable before human decision | Historical APPROVE; decision rejected | Live deterministic guard fails | Historical recommendation cannot authorize unsafe dispatch | `GoldenCase(caseId: "G14", ...)` | PASS |
| G15 | Safe facts; provider REJECT | REJECT | None | No operational assignment rejection | `GoldenCase(caseId: "G15", ...)` | PASS |

Operational snapshots verify no validation changes to team/vehicle states, member availability,
assignment status/bindings/capacity, or dispatch records. G08 intentionally changes PlanVersion
in the callback and asserts N in the result versus N+1 in persistence. G14 externally changes
vehicle availability after validation; the decision must preserve that state and reserve nothing.

G07 deliberately introduces another team's conflicting vehicle reference to isolate VEHICLE_CONFLICT.
It is not a valid proposal that AssignmentService would accept.
G13 verifies actual tool output and matching continuation/call IDs. G12 asserts exactly 12
provider calls/tool steps plus ten mandatory audit steps.

## 9. Execution Evidence

- Base commit: `80ca1544ef11b225315fbf6297ce386189a03384`.
- Tested state: base commit **plus the uncommitted golden suite and two test-setup corrections**.
  The new tests are not claimed to be present in the base commit.
- Execution finish from TRX: `2026-09-29T19:49:09.3864057+05:30`.
- Golden: **15 passed, 0 failed, 0 skipped**.
- Existing deterministic guard: **5/5 passed**.
- Existing Gemini safety tests, including corrected argument setups: **31/31 passed**.
- Existing safe-decision tests: **5/5 passed**.
- All Component D tests: **152/152 passed**.
- Backend/test-project build: **PASS, zero warnings and zero errors**.

From repository root:

```powershell
$env:Database__MigrateOnStartup='false'
$env:ComponentD__SeedDemoData='false'
dotnet test backend/RescueSriLanka.Api.Tests/RescueSriLanka.Api.Tests.csproj --no-restore --filter FullyQualifiedName~ComponentDSafetyAgentGoldenTests --logger 'trx;LogFileName=component-d-safety-agent-golden.trx' --results-directory backend/RescueSriLanka.Api.Tests/TestResults
```

Local evidence: `backend/RescueSriLanka.Api.Tests/TestResults/component-d-safety-agent-golden.trx`.
The repository ignores TestResults directories and *.trx; evidence remains local and can be
regenerated. No ignore rules changed.

PASS means all case assertions passed. Generated workflow GUID values, exact timestamps and
provider prose are not golden expectations. No production expectation was relaxed.

## 10. Known Limitations / Future Hardening

- Fake-provider tests evaluate application/agent safety behavior, not live-model accuracy.
- Live network, billing, quota and provider availability are outside required CI.
- Wrong JSON value kinds can still throw during argument extraction outside the structured
  failure path. G10 covers fixed malformed provider JSON, not all malformed tool argument types.
- The two existing malformed/mismatched argument tests now supply valid interaction/call IDs,
  reaching the intended argument-validation boundary.
- InMemory does not prove PostgreSQL transaction isolation, rollback or concurrency guarantees.
  Serializable dispatch is a production code property, not a measured database guarantee here.
- Recommendation conflict checks include active assignments without dispatches; the approval-time
  guard checks active dispatch conflicts. Semantics are not identical. G14 proves changed vehicle
  availability is revalidated, not equivalence of all conflict rules.
- REJECT on safe facts can have empty FailedChecks and does not operationally reject the assignment.
- G14 calls the real decision service. Its failure maps to HTTP 409; separate integration tests
  exercise the HTTP pipeline.
- Snapshot comparisons exclude audit timestamps and the deliberate G08 version change.
  Version binding and workflow auditing are separately asserted.

## 11. Optional Live Smoke Test

A manually triggered Gemini protocol smoke test may use synthetic data in an isolated local
database. It must be separately authorized/reported, avoid shared operational data, and is
**not required for deterministic golden correctness or CI**. No live smoke test ran here.
