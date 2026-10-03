using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using RescueSriLanka.Api.DTOs;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.DTOs.Auth;

public record LoginRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public required string Email { get; init; }

    [Required, MinLength(8), MaxLength(128)]
    public required string Password { get; init; }
}

public record RegisterRequest
{
    [Required, MaxLength(150), PersonName]
    public required string FullName { get; init; }

    [Required, EmailAddress, MaxLength(256)]
    public required string Email { get; init; }

    [Required, MinLength(10), MaxLength(128), RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).+$", ErrorMessage = "Use at least 10 characters, including a letter and a number.")]
    public required string Password { get; init; }

    [SriLankaPhone]
    public string? PhoneNumber { get; init; }

    /// <summary>
    /// Where the citizen lives, for district warnings. Optional — it can be set
    /// later from the profile. Must be a name from
    /// <see cref="RescueSriLanka.Api.Models.SriLankaDistricts.All"/>.
    /// </summary>
    [MaxLength(50)]
    public string? District { get; init; }
}

/// <summary>
/// Notification settings, changed from the app's profile screen.
///
/// Both fields are optional so a client can change one without having to know,
/// or resend, the other.
/// </summary>
public record UpdatePreferencesRequest
{
    /// <summary>
    /// Three-state on purpose, which is why it is a <see cref="JsonElement"/>
    /// rather than a <c>string?</c>:
    ///
    /// <list type="bullet">
    /// <item>omitted (<c>Undefined</c>) — leave the district as it is</item>
    /// <item><c>null</c> — clear it and stop sending area warnings</item>
    /// <item>a name from <see cref="RescueSriLanka.Api.Models.SriLankaDistricts.All"/> — subscribe</item>
    /// </list>
    ///
    /// A plain <c>string?</c> cannot tell the first two apart: both arrive as
    /// null, so either unsubscribing becomes impossible or every call that only
    /// meant to toggle email silently wipes the district.
    /// </summary>
    public JsonElement District { get; init; }

    public bool? EmailNotificationsEnabled { get; init; }

    /// <summary>
    /// Turns push notifications to this account on or off. Each phone is
    /// registered separately, through the push devices endpoint.
    /// </summary>
    public bool? PushNotificationsEnabled { get; init; }

    /// <summary>
    /// Same three-state shape as <see cref="District"/> and for the same reason:
    /// omitted leaves the phone number as it is, null clears it, a string sets it.
    /// </summary>
    public JsonElement PhoneNumber { get; init; }
}

public record ForgotPasswordRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public required string Email { get; init; }
}

public record VerifyResetCodeRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public required string Email { get; init; }

    [Required, StringLength(6, MinimumLength = 6)]
    public required string Code { get; init; }
}

/// <summary>Handed back once the emailed code checks out. The app spends this,
/// not the code itself, to actually change the password.</summary>
public record VerifyResetCodeResponse
{
    public required string ResetToken { get; init; }
    public required DateTime ExpiresAt { get; init; }
}

public record ResetPasswordRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public required string Email { get; init; }

    [Required]
    public required string ResetToken { get; init; }

    [Required, MinLength(10), MaxLength(128), RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).+$", ErrorMessage = "Use at least 10 characters, including a letter and a number.")]
    public required string NewPassword { get; init; }
}

/// <summary>The user as the clients see it — never exposes the password hash.</summary>
public record UserDto
{
    public required Guid Id { get; init; }
    public required string FullName { get; init; }
    public required string Email { get; init; }
    public required string Role { get; init; }
    public string? PhoneNumber { get; init; }

    /// <summary>Home district for disaster warnings. Null when none is set.</summary>
    public string? District { get; init; }

    /// <summary>Absolute or site-relative URL of the profile photo. Null until one is uploaded.</summary>
    public string? PhotoUrl { get; init; }

    public required bool EmailNotificationsEnabled { get; init; }

    public bool PushNotificationsEnabled { get; init; }

    public static UserDto FromUser(User user) => new()
    {
        Id = user.Id,
        FullName = user.FullName,
        Email = user.Email,
        Role = user.Role.ToString(),
        PhoneNumber = user.PhoneNumber,
        District = user.District,
        PhotoUrl = user.PhotoUrl,
        EmailNotificationsEnabled = user.EmailNotificationsEnabled,
        PushNotificationsEnabled = user.PushNotificationsEnabled
    };
}

public record AuthResponse
{
    public required string Token { get; init; }
    public required DateTime ExpiresAt { get; init; }
    public required UserDto User { get; init; }
}
