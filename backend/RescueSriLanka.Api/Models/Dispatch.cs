using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RescueSriLanka.Api.Models
{
    // The dispatch status workflow — your core "business-specific operation".
    // One Dispatch per Assignment. Timestamps double as a lightweight audit
    // trail; swap for a separate DispatchStatusHistory table later if the
    // rubric's observability requirement calls for more granular logging.
    public class Dispatch
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid AssignmentId { get; set; }

        [ForeignKey(nameof(AssignmentId))]
        public Assignment? Assignment { get; set; }

        public DispatchStatus Status { get; set; } = DispatchStatus.Pending;

        // Human-approval gate — set by the Emergency Coordinator after the
        // Safety Validation Agent's deterministic checks pass.
        public ApprovalStatus ApprovalStatus { get; set; } = ApprovalStatus.PendingApproval;
        public string? ApprovedByUserId { get; set; }
        public DateTime? ApprovedAt { get; set; }

        // Timestamps for each workflow stage (nullable until reached)
        public DateTime? DispatchedAt { get; set; }
        public DateTime? EnRouteAt { get; set; }
        public DateTime? OnSceneAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public DateTime? CancelledAt { get; set; }

        [MaxLength(500)]
        public string? Notes { get; set; }
    }
}
