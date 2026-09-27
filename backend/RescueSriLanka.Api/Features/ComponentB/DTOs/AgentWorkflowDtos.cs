using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using RescueSriLanka.Api.Features.ComponentB.Models;

namespace RescueSriLanka.Api.Features.ComponentB.DTOs
{
    // Enums here are sent as numbers (the React and Flutter help-request clients
    // read them that way), overriding the API-wide JsonStringEnumConverter.

    public class TriggerWorkflowDto
    {
        [JsonConverter(typeof(JsonNumberEnumConverter<PlannerWorkflowObjectiveType>))]
        [EnumDataType(typeof(PlannerWorkflowObjectiveType))]
        public PlannerWorkflowObjectiveType ObjectiveType { get; set; }
        public Guid ObjectiveId { get; set; }
    }

    public class PlannerAgentStepDto
    {
        public Guid Id { get; set; }
        public int StepNumber { get; set; }
        [JsonConverter(typeof(JsonNumberEnumConverter<PlannerAgentType>))]
        public PlannerAgentType TargetAgent { get; set; }
        public string Action { get; set; } = string.Empty;
        public string InputParamsJson { get; set; } = "{}";
        public string? ToolResultJson { get; set; }
        public string? ValidationResultJson { get; set; }
        [JsonConverter(typeof(JsonNumberEnumConverter<PlannerStepStatus>))]
        public PlannerStepStatus Status { get; set; }
    }

    public class AgentWorkflowResponseDto
    {
        public Guid Id { get; set; }
        [JsonConverter(typeof(JsonNumberEnumConverter<PlannerWorkflowObjectiveType>))]
        public PlannerWorkflowObjectiveType ObjectiveType { get; set; }
        public Guid ObjectiveId { get; set; }
        public string PlanJson { get; set; } = "{}";
        [JsonConverter(typeof(JsonNumberEnumConverter<PlannerWorkflowStatus>))]
        public PlannerWorkflowStatus Status { get; set; }
        public string? ApprovalNotes { get; set; }
        public string? FinalOutcomeJson { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<PlannerAgentStepDto> Steps { get; set; } = [];
    }

    // Source: Proposal Section 6 — Coordinator can approve, reject, or revise.
    // ASSUMPTION: "revise" is treated the same as reject for now (sends it back to Planning),
    // since the documents don't specify a distinct revise flow.
    public class ApprovalDecisionDto
    {
        public bool Approved { get; set; }
        [StringLength(1000)]
        public string? Notes { get; set; }
    }
}
