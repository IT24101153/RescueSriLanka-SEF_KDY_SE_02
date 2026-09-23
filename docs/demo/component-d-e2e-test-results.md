# Component D end-to-end test results

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

## Automated baseline

- `dotnet build --no-restore`: PASS, 0 warnings / 0 errors.
- `dotnet test backend/RescueSriLanka.Api.Tests/ --no-restore`: PASS, 65 passed, 0 failed, 0 skipped.
