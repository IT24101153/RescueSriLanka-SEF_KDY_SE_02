using System;

namespace RescueSriLanka.Api.Models
{
    public class RequestStatusHistory
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid HelpRequestId { get; set; }
        public HelpRequest? HelpRequest { get; set; }

        public HelpRequestStatus OldStatus { get; set; }
        public HelpRequestStatus NewStatus { get; set; }

        // Who changed it — a coordinator, the system/agent, or the citizen (e.g. cancelling)
        public Guid ChangedByUserId { get; set; }

        public string? Notes { get; set; }

        public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    }
}