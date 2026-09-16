using System;
using System.Collections.Generic;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.DTOs
{
    public class TriggerWorkflowDto
    {
        public WorkflowObjectiveType ObjectiveType { get; set; }
        public Guid ObjectiveId { get; set; }
    }

    public class AgentStepDto
    {
        public Guid Id { get; set; }
        public int StepNumber { get; set; }
        public AgentType TargetAgent { get; set; }
        public string Action { get; set; } = string.Empty;
        public string InputParamsJson { get; set; } = "{}";
        public string? ToolResultJson { get; set; }
        public string? ValidationResultJson { get; set; }
        public StepStatus Status { get; set; }
    }

    public class AgentWorkflowResponseDto
    {
        public Guid Id { get; set; }
        public WorkflowObjectiveType ObjectiveType { get; set; }
        public Guid ObjectiveId { get; set; }
        public string PlanJson { get; set; } = "{}";
        public WorkflowStatus Status { get; set; }
        public string? ApprovalNotes { get; set; }
        public string? FinalOutcomeJson { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<AgentStepDto> Steps { get; set; } = new();
    }

    // Source: Proposal Section 6 — Coordinator can approve, reject, or revise.
    // ASSUMPTION: "revise" is treated the same as reject for now (sends it back to Planning),
    // since the documents don't specify a distinct revise flow.
    public class ApprovalDecisionDto
    {
        public bool Approved { get; set; }
        public string? Notes { get; set; }
    }
}