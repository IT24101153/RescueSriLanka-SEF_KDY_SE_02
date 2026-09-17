using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RescueSriLanka.Api.DTOs.Auth;
using RescueSriLanka.Api.Services;

namespace RescueSriLanka.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
        {
            var result = await _authService.LoginAsync(request, cancellationToken);
            return result is null
                ? Unauthorized(new { message = "Invalid email or password." })
                : Ok(result);
        }

        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
        {
            var result = await _authService.RegisterCitizenAsync(request, cancellationToken);
            return result is null
                ? Conflict(new { message = "An account with that email already exists." })
                : Ok(result);
        }

        [HttpGet("me")]
        [Authorize]
        public async Task<ActionResult<UserDto>> Me(CancellationToken cancellationToken)
        {
            var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(id, out var userId)) return Unauthorized();

            var user = await _authService.FindByIdAsync(userId, cancellationToken);
            return user is null ? Unauthorized() : Ok(UserDto.FromUser(user));
        }
    }
}