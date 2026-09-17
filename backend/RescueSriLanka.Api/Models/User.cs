using System.ComponentModel.DataAnnotations;

namespace RescueSriLanka.Api.Models
{
    // ASSUMPTION: exact role names not yet confirmed with the team's own User.cs —
    // matched against the 4 roles named in the Project Proposal. Adjust the enum
    // values here if your leader's actual UserRole differs once branches merge.
    public enum UserRole
    {
        Citizen,
        EmergencyCoordinator,
        RescueTeam,
        ResourceManager
    }

    public class User
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [MaxLength(150)]
        public required string FullName { get; set; }

        [MaxLength(256)]
        public required string Email { get; set; }

        // PBKDF2 hash via Microsoft.AspNetCore.Identity.PasswordHasher<T>. Never plain text.
        public required string PasswordHash { get; set; }

        public UserRole Role { get; set; } = UserRole.Citizen;

        [MaxLength(20)]
        public string? PhoneNumber { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
    }
}