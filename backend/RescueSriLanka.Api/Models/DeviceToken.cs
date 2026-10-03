using System.ComponentModel.DataAnnotations;

namespace RescueSriLanka.Api.Models;

/// <summary>
/// A phone's push address, registered by the app once its owner turns push
/// notifications on. One row per device. A phone that changes hands is
/// re-registered to whoever signs in on it, so it never delivers the previous
/// owner's notifications.
/// </summary>
public class DeviceToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>The Firebase registration token. Opaque to us; never logged.</summary>
    [MaxLength(512)]
    public required string Token { get; set; }

    /// <summary>"android" or "ios".</summary>
    [MaxLength(16)]
    public required string Platform { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
