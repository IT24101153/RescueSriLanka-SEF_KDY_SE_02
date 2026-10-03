using System.ComponentModel.DataAnnotations;

namespace RescueSriLanka.Api.DTOs;

/// <summary>What the app sends to register its phone for push notifications.</summary>
public record RegisterPushDeviceRequest
{
    /// <summary>The Firebase registration token for this install of the app.</summary>
    [Required, StringLength(512, MinimumLength = 16)]
    public string Token { get; init; } = string.Empty;

    [Required, RegularExpression("^(android|ios)$", ErrorMessage = "Platform must be android or ios.")]
    public string Platform { get; init; } = string.Empty;
}
