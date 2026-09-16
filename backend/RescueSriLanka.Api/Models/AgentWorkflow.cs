using System;
using System.Collections.Generic;

namespace RescueSriLanka.Api.Models
{
    // Source: ADR Seed List — "dedicated AgentWorkflow/AgentStep tables with jsonb columns."
    // Source: Proposal Section 6 — persists objective, plan, steps, tool results,
    // validation results, approval status, final outcome.
    public class AgentWorkflow
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public WorkflowObjectiveType ObjectiveType { get; set; }

        // The id of the HelpRequest or Incident that triggered this workflow
        public Guid ObjectiveId { get; set; }

        // jsonb snapshot of the objective at the time the workflow started
        // (e.g. the HelpRequest's type, location, urgencyScore at trigger time)
        public string ObjectiveSnapshotJson { get; set; } = "{}";

        // jsonb: the structured multi-step plan the Planner Agent built
        public string PlanJson { get; set; } = "{}";

        public WorkflowStatus Status { get; set; } = WorkflowStatus.Planning;

        // Who approved/rejected, and when — set once a Coordinator acts
        public Guid? ApprovedByUserId { get; set; }
        public DateTime? ApprovalDecisionAt { get; set; }
        public string? ApprovalNotes { get; set; }

        // jsonb: final recorded outcome (success detail, or safe failure detail)
        public string? FinalOutcomeJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<AgentStep> Steps { get; set; } = new List<AgentStep>();
    }

    public class AgentStep
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid AgentWorkflowId { get; set; }
        public AgentWorkflow? AgentWorkflow { get; set; }

        // Order within the plan
        public int StepNumber { get; set; }

        public AgentType TargetAgent { get; set; }

        // Short description of what this step asks the target agent to do
        public string Action { get; set; } = string.Empty;

        // jsonb: input parameters passed to the target agent's tool
        public string InputParamsJson { get; set; } = "{}";

        // jsonb: what the target agent's tool returned
        public string? ToolResultJson { get; set; }

        // jsonb: result of the Safety Validation Agent's deterministic checks, if applicable to this step
        public string? ValidationResultJson { get; set; }

        public StepStatus Status { get; set; } = StepStatus.Pending;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
    }
}