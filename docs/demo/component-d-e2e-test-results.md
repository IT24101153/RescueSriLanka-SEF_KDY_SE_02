# Component D end-to-end test results

## Optional live Gemini scenarios

Live shared-database/Gemini scenarios are **NOT RUN** until explicit approval is given to create isolated demo records and send synthetic operational context to the configured Gemini API.

| Test ID | Scenario | Expected result | Actual result | Status | Evidence / notes |
|---|---|---|---|---|---|
| D-E2E-01 | Happy path | Assignment → validation → human approval → dispatch → release | NOT RUN | NOT RUN | Requires isolated demo data and real Gemini call. |
| D-E2E-02 | Capacity failure | No dispatch or reservation | NOT RUN | NOT RUN | Covered by deterministic unit tests. |
| D-E2E-03 | Missing skill | No dispatch or reservation | NOT RUN | NOT RUN | Covered by deterministic unit tests. |
| D-E2E-04 | Team unavailable | Request rejected | NOT RUN | NOT RUN | Covered by deterministic unit tests. |
| D-E2E-05 | Vehicle unavailable | Request rejected | NOT RUN | NOT RUN | Covered by deterministic unit tests. |
| D-E2E-06 | Stale PlanVersion | Safe decision rejected | NOT RUN | NOT RUN | Covered by safe-decision tests. |
| D-E2E-07 | Human reject | No dispatch; assignment/workflow rejected | NOT RUN | NOT RUN | Covered by safe-decision tests. |
| D-E2E-08 | Human revise | No dispatch; revalidation required after revision | NOT RUN | NOT RUN | Requires live UI walkthrough. |
| D-E2E-09 | Duplicate approval | One dispatch; idempotent retry | NOT RUN | NOT RUN | Covered by safe-decision tests. |
| D-E2E-10 | Unauthorized role | UI read-only and backend 403 | NOT RUN | NOT RUN | Existing authorization tests cover backend. |
| D-E2E-11 | Live revalidation conflict | No dispatch after resource state changes | NOT RUN | NOT RUN | Requires isolated shared-data scenario. |
| D-E2E-12 | Resource release | Resolved mission releases team/vehicle | NOT RUN | NOT RUN | Covered by safe-dispatch tests. |

## Offline automated evidence

These results use isolated InMemory databases and fake providers. They do not mean the
optional live scenarios above ran.

- [Safety Validation Agent golden evaluation](../component-d-safety-agent-golden-evaluation.md):
  **15 passed, 0 failed**, G01-G15 using real agent/tools and a fake provider.
- All Component D tests in this run: **152 passed, 0 failed**.
- The selection includes **20 HTTP integration cases** with real routing/JWT/controllers/services.
- Existing agent selections: deterministic safety **5/5**, Gemini safety **31/31**, safe decision **5/5**.
- Backend/test-project build: **PASS, zero warnings and zero errors**.
- Golden TRX remains local at backend/RescueSriLanka.Api.Tests/TestResults/component-d-safety-agent-golden.trx.
- See the golden report for base commit, uncommitted-test qualification, execution timestamp,
  repeatable command and limitations.
- Full backend suite was not rerun for this task. The preceding recorded baseline was
  **302 passed, 1 unrelated Component B metadata-test failure**.

All optional live Gemini/UI scenarios remain **NOT RUN**. Offline revalidation assertions in
an isolated database must not be presented as live shared-system results.
