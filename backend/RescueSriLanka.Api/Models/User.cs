using System.ComponentModel.DataAnnotations;

namespace RescueSriLanka.Api.Models;

/// <summary>
/// An account that can authenticate against the API. Shared by the React admin
/// console and the Flutter citizen app; the role decides what each can reach.
/// </summary>
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(150)]
    public required string FullName { get; set; }

    /// <summary>Stored lower-cased so lookups are case-insensitive.</summary>
    [MaxLength(256)]
    public required string Email { get; set; }

    /// <summary>PBKDF2 hash produced by <see cref="Microsoft.AspNetCore.Identity.PasswordHasher{T}"/>. Never a plain password.</summary>
    public required string PasswordHash { get; set; }

    public UserRole Role { get; set; } = UserRole.Citizen;

    [MaxLength(20)]
    public string? PhoneNumber { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public DateTime? LastLoginAt { get; set; }
}
