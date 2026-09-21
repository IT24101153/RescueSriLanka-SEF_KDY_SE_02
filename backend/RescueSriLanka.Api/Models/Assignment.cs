using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RescueSriLanka.Api.Models
{
    // Represents a rescue team being matched/assigned to a piece of work.
    // Cross-references Incident (Student A) and/or HelpRequest (Student B)
    // by Id only — those entities live in other students' components, so we
    // don't take a hard EF navigation dependency on them here. Coordinate
    // with A/B on the exact FK names once their models are merged into dev.
    public class Assignment
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        // One of these two should be set depending on what triggered the assignment
        public Guid? IncidentId { get; set; }
        public Guid? HelpRequestId { get; set; }

        public Guid RescueTeamId { get; set; }

        [ForeignKey(nameof(RescueTeamId))]
        public RescueTeam? RescueTeam { get; set; }

        // Nullable for existing persisted assignments created before a
        // vehicle was selected. New/revised operational assignments must
        // provide this value through AssignmentService validation.
        public Guid? VehicleId { get; set; }

        [ForeignKey(nameof(VehicleId))]
        public Vehicle? Vehicle { get; set; }

        // The skill the plan required — used by the Safety Validation Agent
        // to confirm the assigned team actually has this skill available.
        public SkillType RequiredSkill { get; set; }

        public int RequiredCapacity { get; set; } = 1;

        public AssignmentStatus Status { get; set; } = AssignmentStatus.Proposed;

        public int PlanVersion { get; set; } = 1;

        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(500)]
        public string? Notes { get; set; }

        // A dispatch is created once an assignment is approved and sent out
        public Dispatch? Dispatch { get; set; }
    }
}
