using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RescueSriLanka.Api.DTOs.Auth;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Services;

namespace RescueSriLanka.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(IAuthService authService) : ControllerBase
{
    /// <summary>Citizen sign-in for the mobile app. Staff accounts are refused here.</summary>
    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public Task<ActionResult<AuthResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken) =>
        SignInAsync(request, LoginClient.CitizenApp, cancellationToken);

    /// <summary>Staff sign-in for the web portal. Citizen accounts are refused here.</summary>
    [HttpPost("portal/login")]
    [EnableRateLimiting("auth")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public Task<ActionResult<AuthResponse>> PortalLogin(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken) =>
        SignInAsync(request, LoginClient.StaffPortal, cancellationToken);

    private async Task<ActionResult<AuthResponse>> SignInAsync(
        LoginRequest request, LoginClient client, CancellationToken cancellationToken)
    {
        var result = await authService.LoginAsync(request, client, cancellationToken);

        // Deliberately vague: never reveal whether the email exists or which kind of account it is.
        return result is null
            ? Unauthorized(new { message = "Invalid email or password." })
            : Ok(result);
    }

    /// <summary>Citizen self-registration for the Flutter app.</summary>
    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            // The same answer whether or not the address was already registered, so sign-up cannot be used to find accounts.
            await authService.RegisterCitizenAsync(request, cancellationToken);
            return Accepted(new { message = "If this email can be registered, the account is ready. Sign in with your email and password." });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Emails a one-time code for a forgotten password. Always reports
    /// success — the response must never reveal whether the address has an account.</summary>
    [HttpPost("forgot-password")]
    [EnableRateLimiting("auth")]
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
    [EnableRateLimiting("auth")]
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
    [EnableRateLimiting("auth")]
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
