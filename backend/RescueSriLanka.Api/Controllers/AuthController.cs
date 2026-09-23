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
}
