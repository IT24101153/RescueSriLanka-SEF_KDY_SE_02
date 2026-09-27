using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RescueSriLanka.Api.DTOs.Auth;
using RescueSriLanka.Api.Services;

namespace RescueSriLanka.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(IAuthService authService) : ControllerBase
{
    /// <summary>Exchanges email and password for a JWT.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authService.LoginAsync(request, cancellationToken);

        // Deliberately vague: never reveal whether the email exists.
        return result is null
            ? Unauthorized(new { message = "Invalid email or password." })
            : Ok(result);
    }

    /// <summary>Citizen self-registration for the Flutter app.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponse>> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await authService.RegisterCitizenAsync(request, cancellationToken);

            return result is null
                ? Conflict(new { message = "An account with that email already exists." })
                : Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Emails a one-time code for a forgotten password. Always reports
    /// success — the response must never reveal whether the address has an account.</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        await authService.RequestPasswordResetAsync(request, cancellationToken);
        return Ok(new { message = "If an account exists for that email, a reset code has been sent." });
    }

    /// <summary>Checks the emailed code and, if correct, issues the token
    /// <see cref="ResetPassword"/> spends to actually change the password.</summary>
    [HttpPost("verify-reset-code")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(VerifyResetCodeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<VerifyResetCodeResponse>> VerifyResetCode(
        [FromBody] VerifyResetCodeRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authService.VerifyResetCodeAsync(request, cancellationToken);

        return result is null
            ? BadRequest(new { message = "That code is invalid or has expired." })
            : Ok(result);
    }

    /// <summary>Spends the token from <see cref="VerifyResetCode"/> to set a new password.</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var ok = await authService.ResetPasswordAsync(request, cancellationToken);

        return ok
            ? Ok(new { message = "Password updated. Please sign in." })
            : BadRequest(new { message = "That reset request is invalid or has expired." });
    }

    /// <summary>Returns the signed-in user — used by both clients to restore a session.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserDto>> Me(CancellationToken cancellationToken)
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(id, out var userId))
        {
            return Unauthorized();
        }

        var user = await authService.FindByIdAsync(userId, cancellationToken);
        return user is null ? Unauthorized() : Ok(UserDto.FromUser(user));
    }

    /// <summary>
    /// Sets the home district for disaster warnings, and the email opt-out.
    /// This is what the app's Profile → Notification settings screen saves.
    /// </summary>
    [HttpPatch("me/preferences")]
    [Authorize]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserDto>> UpdatePreferences(
        [FromBody] UpdatePreferencesRequest request,
        CancellationToken cancellationToken)
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(id, out var userId))
        {
            return Unauthorized();
        }

        try
        {
            var user = await authService.UpdatePreferencesAsync(userId, request, cancellationToken);
            return user is null ? Unauthorized() : Ok(UserDto.FromUser(user));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Replaces the signed-in user's profile photo. This is what the app's
    /// Profile screen calls when someone taps the avatar to change it.</summary>
    [HttpPost("me/photo")]
    [Authorize]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserDto>> UpdatePhoto(
        IFormFile photo,
        CancellationToken cancellationToken)
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(id, out var userId))
        {
            return Unauthorized();
        }

        try
        {
            var user = await authService.UpdatePhotoAsync(userId, photo, cancellationToken);
            return user is null ? Unauthorized() : Ok(UserDto.FromUser(user));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
