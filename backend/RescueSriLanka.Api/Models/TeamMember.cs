using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RescueSriLanka.Api.Models
{
    public class TeamMember
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required, MaxLength(150)]
        public string FullName { get; set; } = string.Empty;

        [Required, MaxLength(20)]
        public string Phone { get; set; } = string.Empty;

        public SkillType Skill { get; set; }

        // Personal availability — distinct from the team's overall status,
        // since individual members can be off-duty while the team is Available.
        public bool IsAvailable { get; set; } = true;

        // Foreign key
        public Guid RescueTeamId { get; set; }

        [ForeignKey(nameof(RescueTeamId))]
        public RescueTeam? RescueTeam { get; set; }
    }
}
