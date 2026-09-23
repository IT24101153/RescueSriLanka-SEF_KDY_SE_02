using System.ComponentModel.DataAnnotations;

namespace RescueSriLanka.Api.Features.ComponentD.Models
{
    public class RescueTeam
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required, MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        public TeamStatus Status { get; set; } = TeamStatus.Available;

        // Base/HQ location — useful later for ETA calculations via OSRM
        public double? BaseLatitude { get; set; }
        public double? BaseLongitude { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public ICollection<TeamMember> Members { get; set; } = new List<TeamMember>();
        public ICollection<Vehicle> Vehicles { get; set; } = new List<Vehicle>();
        public ICollection<Assignment> Assignments { get; set; } = new List<Assignment>();

        // Convenience: aggregate skills currently available on this team,
        // computed from its members. Not mapped to the DB.
        [System.Text.Json.Serialization.JsonIgnore]
        public IEnumerable<SkillType> AvailableSkills =>
            Members.Where(m => m.IsAvailable).Select(m => m.Skill).Distinct();
    }
}
