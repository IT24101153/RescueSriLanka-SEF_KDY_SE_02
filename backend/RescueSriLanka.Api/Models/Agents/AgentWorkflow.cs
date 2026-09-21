// Shared infrastructure for the group's Agentic AI subsystem — not owned
// by any single component. Persists the multi-step plan, each step's
// input/output, and the overall approval/outcome, per the proposal's
// requirement: "workflow state (objective, plan, steps, tool results,
// validation results, approval status, final outcome) is persisted in
// PostgreSQL."

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RescueSriLanka.Api.Models.Agents
{
    public enum WorkflowObjectiveType { NewIncident, NewHelpRequest }

    public enum WorkflowStatus { Planning, InProgress, AwaitingApproval, Approved, Rejected, Completed, Failed }

    public enum AgentStepStatus { Pending, Running, Succeeded, Failed, Skipped }

    public class AgentWorkflow
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public WorkflowObjectiveType ObjectiveType { get; set; }

        // The Incident or HelpRequest this workflow was triggered for —
        // owned by Students A/B respectively; referenced by Id only.
        public Guid ObjectiveId { get; set; }

        public SkillType RequiredSkill { get; set; }

        public WorkflowStatus Status { get; set; } = WorkflowStatus.Planning;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }

        // The dispatch this workflow produced, once resource/logistics
        // and safety validation succeed. Null if the workflow failed
        // before reaching that point.
        public Guid? DispatchId { get; set; }

        [MaxLength(500)]
        public string? FinalOutcome { get; set; }

        public ICollection<AgentStep> Steps { get; set; } = new List<AgentStep>();
    }

    public class AgentStep
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid AgentWorkflowId { get; set; }

        [ForeignKey(nameof(AgentWorkflowId))]
        public AgentWorkflow? Workflow { get; set; }

        public int StepOrder { get; set; }

        // Free-text agent identifier rather than an enum, since the set
        // of agents is defined across 4 independently-owned components —
        // an enum here would need editing by whichever student adds a
        // new agent, which is exactly the kind of shared-file conflict
        // to avoid.
        [Required, MaxLength(100)]
        public string AgentName { get; set; } = string.Empty;

        public AgentStepStatus Status { get; set; } = AgentStepStatus.Pending;

        // Structured input/output, serialized as JSON text. Keeping this
        // as a string (rather than jsonb via a value converter) is a
        // deliberate simplicity trade-off for the MVP — worth revisiting
        // in the ADR's "database schema strategy for Agentic AI workflow
        // state" decision if the group wants queryable JSON later.
        public string? InputJson { get; set; }
        public string? OutputJson { get; set; }

        [MaxLength(500)]
        public string? ErrorMessage { get; set; }

        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }
}
