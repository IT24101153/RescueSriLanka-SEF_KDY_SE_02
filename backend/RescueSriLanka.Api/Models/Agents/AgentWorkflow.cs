using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RescueSriLanka.Api.Models.Agents;

// These numeric values are persisted by PostgreSQL and must remain stable.
public enum WorkflowObjectiveType
{
    Incident = 0,
    HelpRequest = 1
}

public enum WorkflowStatus
{
    Planning = 0,
    AwaitingApproval = 1,
    Approved = 2,
    Rejected = 3,
    Executing = 4,
    Completed = 5,
    Failed = 6
}

public enum AgentType
{
    IncidentAnalysisAgent = 0,
    CoordinatorPlannerAgent = 1,
    ResourceLogisticsPlanningAgent = 2,
    SafetyValidationAgent = 3
}

public enum StepStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3
}

public class AgentWorkflow
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public WorkflowObjectiveType ObjectiveType { get; set; }
    public Guid ObjectiveId { get; set; }
    public string ObjectiveSnapshotJson { get; set; } = "{}";
    public string PlanJson { get; set; } = "{}";
    public WorkflowStatus Status { get; set; } = WorkflowStatus.Planning;
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovalDecisionAt { get; set; }
    public string? ApprovalNotes { get; set; }
    public string? FinalOutcomeJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<AgentStep> Steps { get; set; } = new List<AgentStep>();
}

public class AgentStep
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AgentWorkflowId { get; set; }

    [ForeignKey(nameof(AgentWorkflowId))]
    public AgentWorkflow? Workflow { get; set; }

    public int StepNumber { get; set; }
    public AgentType TargetAgent { get; set; }
    public string Action { get; set; } = string.Empty;
    public string InputParamsJson { get; set; } = "{}";
    public string? ToolResultJson { get; set; }
    public string? ValidationResultJson { get; set; }
    public StepStatus Status { get; set; } = StepStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
