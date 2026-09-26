using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.DTOs.Auth;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentA.Services;
using RescueSriLanka.Api.Features.ComponentA.Services.Notifications;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Services.Storage;

namespace RescueSriLanka.Api.Services;

public interface IAuthService
{
    Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    /// <returns>
    /// null when the email is taken. Throws <see cref="ArgumentException"/> when
    /// the district is not one of Sri Lanka's.
    /// </returns>
    Task<AuthResponse?> RegisterCitizenAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <returns>
    /// The updated user, or null when the account is gone. Throws
    /// <see cref="ArgumentException"/> when the district is not one of Sri Lanka's.
    /// </returns>
    Task<User?> UpdatePreferencesAsync(
        Guid id, UpdatePreferencesRequest request, CancellationToken cancellationToken = default);

    /// <returns>The updated user, or null when the account is gone.</returns>
    /// <exception cref="ArgumentException">The file fails <see cref="ImageStorageService.Validate"/>.</exception>
    Task<User?> UpdatePhotoAsync(Guid id, IFormFile file, CancellationToken cancellationToken = default);
}

public class AuthService(
    AppDbContext db,
    IJwtTokenService tokenService,
    IPasswordHasher<User> passwordHasher,
    INotificationQueue notificationQueue,
    IImageStore imageStore,
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

        // Stored canonically for the same reason as in UpdatePreferencesAsync:
        // warnings are matched on the exact string.
        string? district = null;
        if (!string.IsNullOrWhiteSpace(request.District))
        {
            district = SriLankaDistricts.Normalise(request.District)
                ?? throw new ArgumentException(
                    $"'{request.District}' is not a district of Sri Lanka.");
        }

        var user = new User
        {
            FullName = request.FullName.Trim(),
            Email = email,
            PasswordHash = string.Empty,
            PhoneNumber = request.PhoneNumber,
            District = district,
            Role = UserRole.Citizen
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Registered citizen {Email} in {District}", email, district ?? "(no district)");

        // Queued, not awaited: a slow mail server must not slow sign-up down.
        notificationQueue.Enqueue(new NotificationJob(NotificationKind.Welcome, user.Id));

        return BuildResponse(user);
    }

    public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    /// <summary>
    /// Sets the district a citizen wants warnings for, and whether they want
    /// email at all. The district is stored in its canonical spelling — warnings
    /// are matched on that string, so accepting "colombo" verbatim would leave
    /// someone quietly subscribed to a district that never matches.
    /// </summary>
    public async Task<User?> UpdatePreferencesAsync(
        Guid id,
        UpdatePreferencesRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null) return null;

        var previousDistrict = user.District;

        // Omitted leaves the district alone; an explicit null clears it. See
        // UpdatePreferencesRequest.District for why this is not a string?.
        switch (request.District.ValueKind)
        {
            case JsonValueKind.Undefined:
                break;

            case JsonValueKind.Null:
                user.District = null;
                break;

            case JsonValueKind.String:
                var name = request.District.GetString();
                user.District = string.IsNullOrWhiteSpace(name)
                    ? null
                    : SriLankaDistricts.Normalise(name)
                      ?? throw new ArgumentException(
                          $"'{name}' is not a district of Sri Lanka.");
                break;

            default:
                throw new ArgumentException(
                    "district must be a district name or null.");
        }

        if (request.EmailNotificationsEnabled is bool enabled)
        {
            user.EmailNotificationsEnabled = enabled;
        }

        // Same omitted/null/string shape as District above.
        switch (request.PhoneNumber.ValueKind)
        {
            case JsonValueKind.Undefined:
                break;

            case JsonValueKind.Null:
                user.PhoneNumber = null;
                break;

            case JsonValueKind.String:
                var phone = request.PhoneNumber.GetString();
                user.PhoneNumber = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
                break;

            default:
                throw new ArgumentException("phoneNumber must be a string or null.");
        }

        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Updated notification settings for {Email}: district {District}, email {State}",
            user.Email, user.District ?? "(none)",
            user.EmailNotificationsEnabled ? "on" : "off");

        // A new district may already be under warning, and those warnings went
        // out before this person subscribed. The notification service sends
        // nothing if the district is quiet.
        if (user.District is not null && user.District != previousDistrict)
        {
            notificationQueue.Enqueue(
                new NotificationJob(NotificationKind.DistrictBriefing, user.Id));
        }

        return user;
    }

    /// <summary>Replaces the profile photo. The old upload, if any, is left where it is —
    /// neither store supports deleting by URL alone, and an orphaned file costs nothing to keep.</summary>
    public async Task<User?> UpdatePhotoAsync(
        Guid id, IFormFile file, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null) return null;

        ImageStorageService.Validate(file);

        var stored = await imageStore.SaveAsync(id, "avatars", file, cancellationToken);
        user.PhotoUrl = stored.Location;
        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Updated profile photo for {Email} via {Store}", user.Email, imageStore.Name);

        return user;
    }

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
