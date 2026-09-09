using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.DTOs.Auth;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Services;

public interface IAuthService
{
    Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<AuthResponse?> RegisterCitizenAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
}

public class AuthService(
    AppDbContext db,
    IJwtTokenService tokenService,
    IPasswordHasher<User> passwordHasher,
    ILogger<AuthService> logger) : IAuthService
{
    /// <returns>null when the credentials are wrong or the account is disabled.</returns>
    public async Task<AuthResponse?> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user is null)
        {
            // Hash anyway so a missing account and a wrong password take a
            // similar amount of time — this is a cheap defence against
            // probing which emails exist.
            passwordHasher.HashPassword(
                new User { FullName = "-", Email = "-", PasswordHash = "-" },
                request.Password);
            logger.LogInformation("Login failed: no account for {Email}", email);
            return null;
        }

        if (!user.IsActive)
        {
            logger.LogInformation("Login blocked: account {Email} is disabled", email);
            return null;
        }

        var verification = passwordHasher.VerifyHashedPassword(
            user, user.PasswordHash, request.Password);

        if (verification == PasswordVerificationResult.Failed)
        {
            logger.LogInformation("Login failed: bad password for {Email}", email);
            return null;
        }

        // Transparently upgrade the stored hash when the algorithm moves on.
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        }

        user.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return BuildResponse(user);
    }

    /// <summary>Self-registration from the Flutter app. Always creates a Citizen —
    /// staff roles are provisioned by an administrator, never self-selected.</summary>
    public async Task<AuthResponse?> RegisterCitizenAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        if (await db.Users.AnyAsync(u => u.Email == email, cancellationToken))
        {
            return null;
        }

        var user = new User
        {
            FullName = request.FullName.Trim(),
            Email = email,
            PasswordHash = string.Empty,
            PhoneNumber = request.PhoneNumber,
            Role = UserRole.Citizen
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Registered citizen {Email}", email);
        return BuildResponse(user);
    }

    public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    private AuthResponse BuildResponse(User user)
    {
        var (token, expiresAt) = tokenService.CreateToken(user);
        return new AuthResponse
        {
            Token = token,
            ExpiresAt = expiresAt,
            User = UserDto.FromUser(user)
        };
    }
}
