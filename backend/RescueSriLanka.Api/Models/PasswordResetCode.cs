using System.ComponentModel.DataAnnotations;

namespace RescueSriLanka.Api.Models;

/// <summary>
/// One forgot-password attempt: the emailed one-time code, and — once that
/// code has been verified — the follow-up token the app spends to actually
/// change the password. Kept as a single row rather than two tables because
/// the two steps are never useful apart from each other.
/// </summary>
public class PasswordResetCode
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required Guid UserId { get; set; }

    /// <summary>Hash of the 6-digit code, produced the same way a password is —
    /// there is no reason to trust this table with the code in the clear.</summary>
    [MaxLength(256)]
    public required string CodeHash { get; set; }

    /// <summary>How many wrong codes have been tried against this row. Once this
    /// reaches the service's limit, the row is dead even if unexpired.</summary>
    public int Attempts { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When the emailed code itself stops being acceptable.</summary>
    public required DateTime ExpiresAt { get; set; }

    /// <summary>Set once the code has been checked and found correct.</summary>
    public DateTime? VerifiedAt { get; set; }

    /// <summary>Hash of the opaque token handed back on verification. Null until
    /// <see cref="VerifiedAt"/> is set.</summary>
    [MaxLength(256)]
    public string? ResetTokenHash { get; set; }

    /// <summary>When that token stops being acceptable. Null until verified.</summary>
    public DateTime? ResetTokenExpiresAt { get; set; }

    /// <summary>Set once the password has actually been changed with this row —
    /// the point at which it can never be used again.</summary>
    public DateTime? ConsumedAt { get; set; }
}
