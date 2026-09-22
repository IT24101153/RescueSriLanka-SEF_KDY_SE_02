using System;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.DTOs
{
    // What the client sends when creating a new help request
    public class CreateHelpRequestDto
    {
        public HelpRequestType Type { get; set; }
        public string Description { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public Guid? RelatedIncidentId { get; set; }
        public string? ImageUrl { get; set; }
    }

    // What the client sends to change a request's status
    public class UpdateHelpRequestStatusDto
    {
        public HelpRequestStatus NewStatus { get; set; }
        public string? Notes { get; set; }
    }

    // What the admin sends when verifying a citizen report as real or fake
    public class VerifyHelpRequestDto
    {
        public bool IsReal { get; set; }
        public string? Notes { get; set; }
    }

    // What the API returns to clients (React + Flutter)
    public class HelpRequestResponseDto
    {
        public Guid Id { get; set; }
        public Guid CitizenId { get; set; }
        public HelpRequestType Type { get; set; }
        public string Description { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int UrgencyScore { get; set; }
        public HelpRequestStatus Status { get; set; }
        public VerificationStatus VerificationStatus { get; set; }
        public string? VerificationNotes { get; set; }
        public string? ImageUrl { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    // A single entry in the status history, for the tracking screen
    public class StatusHistoryDto
    {
        public HelpRequestStatus OldStatus { get; set; }
        public HelpRequestStatus NewStatus { get; set; }
        public string? Notes { get; set; }
        public DateTime ChangedAt { get; set; }
    }

    // A compact, citizen-safe summary of the most recent incident-analysis step.
    public class AiPriorityResponseDto
    {
        public string Priority { get; set; } = "Pending";
        public bool AiAnalysisAvailable { get; set; }
    }

    public class AnalyzeRequestDraftDto
    {
        public HelpRequestType Type { get; set; }
        public string Description { get; set; } = string.Empty;
    }
}
