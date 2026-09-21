using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RescueSriLanka.Api.Models
{
    public class Vehicle
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required, MaxLength(20)]
        public string PlateNumber { get; set; } = string.Empty;

        public VehicleType Type { get; set; }

        public VehicleStatus Status { get; set; } = VehicleStatus.Available;

        // e.g. number of people/patients it can carry — feeds capacity checks
        public int Capacity { get; set; }

        // Foreign key
        public Guid RescueTeamId { get; set; }

        [ForeignKey(nameof(RescueTeamId))]
        public RescueTeam? RescueTeam { get; set; }

        public ICollection<Assignment> Assignments { get; set; } = new List<Assignment>();
    }
}
