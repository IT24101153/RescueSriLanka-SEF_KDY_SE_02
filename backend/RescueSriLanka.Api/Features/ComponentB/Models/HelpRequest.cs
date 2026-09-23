using System;
using System.Collections.Generic;

namespace RescueSriLanka.Api.Features.ComponentB.Models
{
    public class HelpRequest
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        // FK to the citizen/tourist who submitted this (your Identity/User table)
        public Guid CitizenId { get; set; }

        public HelpRequestType Type { get; set; }

        public string Description { get; set; } = string.Empty;

        // Location of the request
        public double Latitude { get; set; }
        public double Longitude { get; set; }

        // Optional: link to a nearby Incident, if one exists (owned by Student A)
        public Guid? RelatedIncidentId { get; set; }

        // Computed by your urgency-scoring logic, not user-entered
        public int UrgencyScore { get; set; }

        public HelpRequestStatus Status { get; set; } = HelpRequestStatus.Pending;

        // Admin verification — separate from assignment Status above.
        // A request can be "Pending" assignment-wise while still "PendingVerification" trust-wise.
        public VerificationStatus VerificationStatus { get; set; } = VerificationStatus.PendingVerification;
        public Guid? VerifiedByUserId { get; set; }
        public DateTime? VerifiedAt { get; set; }
        public string? VerificationNotes { get; set; }

        public string? ImageUrl { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation property
        public ICollection<RequestStatusHistory> StatusHistory { get; set; } = [];
    }
}