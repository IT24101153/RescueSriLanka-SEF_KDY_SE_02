namespace RescueSriLanka.Api.Features.ComponentB.Models
{
    // Which type of thing triggered the workflow.
    // Source: Proposal Section 9 — "a new incident or help request" triggers the plan.
    public enum PlannerWorkflowObjectiveType
    {
        Incident,
        HelpRequest
    }

    // Overall workflow status.
    // Source: Proposal Section 6 — plan pauses for approval before high-impact action;
    // outcome is "an auditable success or a safe, clearly recorded failure."
    public enum PlannerWorkflowStatus
    {
        Planning,           // Planner Agent is building the plan
        AwaitingApproval,   // Plan built, validated, waiting on Coordinator
        Approved,
        Rejected,
        Executing,          // Approved plan is being carried out
        Completed,
        Failed,
        // Appended last: the status is stored as its number, so existing rows keep their meaning.
        Superseded          // A newer assessment of the same request replaced this plan
    }

    // Source: Proposal Section 6 — four named agents.
    public enum PlannerAgentType
    {
        IncidentAnalysisAgent,
        CoordinatorPlannerAgent,
        ResourceLogisticsPlanningAgent,
        SafetyValidationAgent
    }

    // ASSUMPTION (not specified in documents): per-step execution status.
    public enum PlannerStepStatus
    {
        Pending,
        Running,
        Completed,
        Failed
    }
}
