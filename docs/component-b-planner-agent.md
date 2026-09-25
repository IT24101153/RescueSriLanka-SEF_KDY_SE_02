# Component B Planner Agent

## Responsibility

The Component B Planner Agent turns one citizen Help Request into a proposed,
auditable response plan. It does not dispatch a team or change a request status.
A Help Request Manager or Emergency Coordinator must approve or reject the plan.

## Contract

- Input: `TriggerWorkflowDto` with `objectiveType: HelpRequest` and a Help Request ID.
- Output: `AgentWorkflowResponseDto` with the saved plan, per-step results,
  validation result, approval status and final outcome.
- Endpoint access: all workflow endpoints require `EmergencyCoordinator` or
  `HelpRequestManager` JWT roles.

## Allow-listed operations

The agent is restricted to its injected services and Component B database data:

1. Read the identified Help Request snapshot.
2. Calculate deterministic severity/zone from the stored urgency score.
3. Request optional Gemini explanation through `IAiAnalysisService`.
4. Calculate an indicative nearest-facility route from its fixed reference set.
5. Check Component B workflow data for duplicate active plans and request state.

It cannot write dispatches, modify a rescue team, invoke arbitrary tools, expose
the Gemini key, or make an irreversible decision.

## Deterministic validation

The Safety Validation step fails when either condition is true:

- another workflow for the same request is awaiting approval or approved;
- the request is resolved or cancelled.

The result is persisted in `ValidationResultJson`; no approval action occurs
when validation fails.

## Error handling and security

- Gemini is optional. A missing key, timeout or malformed response records
  `aiAnalysisAvailable: false`; deterministic severity still completes.
- The API key is server-only and never returned to Flutter or React.
- A citizen can access AI data only for their own request; coordinators can
  review any request permitted by their role.
- Workflow decisions record the authenticated coordinator ID and timestamp.

## Golden cases

| Case | Input | Expected deterministic outcome |
|---|---|---|
| High urgency | Rescue request, urgency 90 | High severity, Danger zone, AwaitingApproval when no duplicate exists |
| Medium urgency | Shelter request, urgency 50 | Medium severity, Caution zone, AwaitingApproval when no duplicate exists |
| Duplicate prevention | Trigger while a workflow is AwaitingApproval | Failed workflow; duplicateActiveWorkflow true |
| Not actionable | Resolved or Cancelled Help Request | Failed workflow; requestStillActionable false |
| Gemini unavailable | Missing/failed Gemini call | Workflow remains deterministic, AI fields absent and `aiAnalysisAvailable` false |

The service-level tests should cover each golden case with a fake
`IAiAnalysisService`, plus controller tests for role and ownership checks.
