using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.DTOs.Auth;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentA.Services;
using RescueSriLanka.Api.Features.ComponentA.Services.Notifications;
using RescueSriLanka.Api.DTOs;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Services.Email;
using RescueSriLanka.Api.Services.Storage;

namespace RescueSriLanka.Api.Services;

public interface IAuthService
{
    Task<AuthResponse?> LoginAsync(LoginRequest request, LoginClient client, CancellationToken cancellationToken = default);

    /// <returns>
    /// null when the email is taken. Throws <see cref="ArgumentException"/> when
    /// the district is not one of Sri Lanka's.
    /// </returns>
    Task<bool> RegisterCitizenAsync(RegisterRequest request, CancellationToken cancellationToken = default);
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

    /// <summary>
    /// Emails a one-time code for a forgotten password. Always completes
    /// successfully from the caller's point of view — whether the address has
    /// an account is never revealed by this call's outcome.
    /// </summary>
    Task RequestPasswordResetAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default);

    /// <returns>
    /// A reset token to spend with <see cref="ResetPasswordAsync"/>, or null
    /// when the code is wrong, expired, already used, or over its attempt limit.
    /// </returns>
    Task<VerifyResetCodeResponse?> VerifyResetCodeAsync(
        VerifyResetCodeRequest request, CancellationToken cancellationToken = default);

    /// <returns>False when the token is wrong, expired, or already spent.</returns>
    Task<bool> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Which front end is asking to sign someone in. Citizens use the
/// mobile app; staff use the web portal. Each only accepts its own accounts.</summary>
public enum LoginClient
{
    CitizenApp,
    StaffPortal
}

public static class LoginClientRules
{
    public static bool Accepts(this LoginClient client, UserRole role) => client switch
    {
        LoginClient.CitizenApp => role == UserRole.Citizen,
        LoginClient.StaffPortal => role != UserRole.Citizen,
        _ => false
    };
}

public class AuthService(
    AppDbContext db,
    IJwtTokenService tokenService,
    IPasswordHasher<User> passwordHasher,
    INotificationQueue notificationQueue,
    IImageStore imageStore,
    IEmailSender emailSender,
    ILogger<AuthService> logger) : IAuthService
{
    // A code short enough to type from memory, alive just long enough for one
    // sitting, and dead the moment too many wrong guesses have been made
    // against it.
    private static readonly TimeSpan OtpValidity = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ResetTokenValidity = TimeSpan.FromMinutes(10);
    private const int MaxOtpAttempts = 5;
    private const int MaxCodesPerHour = 3;


    /// <returns>null when the credentials are wrong or the account is disabled.</returns>
    public async Task<AuthResponse?> LoginAsync(
        LoginRequest request,
        LoginClient client,
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
            logger.LogInformation("Login failed: no matching account");
            return null;
        }

        var verification = passwordHasher.VerifyHashedPassword(
            user, user.PasswordHash, request.Password);

        if (verification == PasswordVerificationResult.Failed)
        {
            logger.LogInformation("Login failed: bad password for account {UserId}", user.Id);
            return null;
        }

        // Checked after the password, so a disabled account costs the same time as any other.
        if (!user.IsActive)
        {
            logger.LogInformation("Login blocked: account {UserId} is disabled", user.Id);
            return null;
        }

        // Checked after the password, and answered with the same null, so the
        // response never reveals which kind of account the email belongs to.
        if (!client.Accepts(user.Role))
        {
            logger.LogInformation("Login refused: {Role} account {UserId} used the {Client} sign-in", user.Role, user.Id, client);
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
    public async Task<bool> RegisterCitizenAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        if (await db.Users.AnyAsync(u => u.Email == email, cancellationToken))
        {
            return false;
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
            PhoneNumber = PhoneNumbers.Normalize(request.PhoneNumber),
            District = district,
            Role = UserRole.Citizen
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two sign-ups for the same address raced past the check above.
            db.ChangeTracker.Clear();
            return false;
        }

        logger.LogInformation("Registered citizen {UserId} in {District}", user.Id, district ?? "(no district)");

        // Queued, not awaited: a slow mail server must not slow sign-up down.
        notificationQueue.Enqueue(new NotificationJob(NotificationKind.Welcome, user.Id));

        return true;
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

        if (request.PushNotificationsEnabled is bool pushEnabled)
        {
            user.PushNotificationsEnabled = pushEnabled;
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
                user.PhoneNumber = string.IsNullOrWhiteSpace(phone)
                    ? null
                    : PhoneNumbers.Normalize(phone)
                      ?? throw new ArgumentException("Enter a 10-digit Sri Lankan phone number, such as 0771234567.");
                break;

            default:
                throw new ArgumentException("phoneNumber must be a string or null.");
        }

        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Updated notification settings for {UserId}: district {District}, email {Email}, push {Push}",
            user.Id, user.District ?? "(none)",
            user.EmailNotificationsEnabled ? "on" : "off",
            user.PushNotificationsEnabled ? "on" : "off");

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
            "Updated profile photo for {UserId} via {Store}", user.Id, imageStore.Name);

        return user;
    }

    public async Task RequestPasswordResetAsync(
        ForgotPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(
            u => u.Email == email && u.IsActive, cancellationToken);

        if (user is null)
        {
            logger.LogInformation("Password reset requested for an address with no active account.");
            return;
        }

        // Limits per account, not per code: a fresh code would otherwise restart the guess counter.
        var since = DateTime.UtcNow.AddHours(-1);
        var recent = await db.PasswordResetCodes
            .Where(c => c.UserId == user.Id && c.CreatedAt >= since)
            .ToListAsync(cancellationToken);
        if (recent.Count >= MaxCodesPerHour || recent.Sum(c => c.Attempts) >= MaxOtpAttempts)
        {
            logger.LogInformation("Password reset throttled for account {UserId}.", user.Id);
            return;
        }

        // A fresh request retires whatever came before it — only one code should be live at a time. The
        // retired rows stay, so the hourly limits above can still count them.
        var stale = await db.PasswordResetCodes
            .Where(c => c.UserId == user.Id && c.ConsumedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var retired in stale)
        {
            retired.ConsumedAt = DateTime.UtcNow;
        }

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

        db.PasswordResetCodes.Add(new PasswordResetCode
        {
            UserId = user.Id,
            CodeHash = passwordHasher.HashPassword(user, code),
            ExpiresAt = DateTime.UtcNow.Add(OtpValidity)
        });

        await db.SaveChangesAsync(cancellationToken);

        // Awaited rather than queued: unlike a district warning, a citizen is
        // looking at their phone for this code right now, and a request that
        // reports success before the email is even attempted would leave them
        // waiting on nothing. This also bypasses EmailOptions.Enabled — that
        // switch silences optional broadcasts, not the one email account
        // recovery depends on.
        try
        {
            await emailSender.SendAsync(
                EmailTemplates.PasswordResetCode(user, code, OtpValidity), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send password reset code for account {UserId}", user.Id);
        }
    }

    public async Task<VerifyResetCodeResponse?> VerifyResetCodeAsync(
        VerifyResetCodeRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(
            u => u.Email == email && u.IsActive, cancellationToken);
        if (user is null) return null;

        var attemptsInWindow = await db.PasswordResetCodes
            .Where(c => c.UserId == user.Id && c.CreatedAt >= DateTime.UtcNow.AddHours(-1))
            .SumAsync(c => c.Attempts, cancellationToken);
        if (attemptsInWindow >= MaxOtpAttempts) return null;

        var record = await db.PasswordResetCodes
            .Where(c => c.UserId == user.Id && c.ConsumedAt == null && c.VerifiedAt == null)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (record is null || record.ExpiresAt < DateTime.UtcNow || record.Attempts >= MaxOtpAttempts)
        {
            return null;
        }

        var verification = passwordHasher.VerifyHashedPassword(user, record.CodeHash, request.Code);
        if (verification == PasswordVerificationResult.Failed)
        {
            record.Attempts++;
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }

        var resetToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var expiresAt = DateTime.UtcNow.Add(ResetTokenValidity);

        record.VerifiedAt = DateTime.UtcNow;
        record.ResetTokenHash = passwordHasher.HashPassword(user, resetToken);
        record.ResetTokenExpiresAt = expiresAt;
        await db.SaveChangesAsync(cancellationToken);

        return new VerifyResetCodeResponse { ResetToken = resetToken, ExpiresAt = expiresAt };
    }

    public async Task<bool> ResetPasswordAsync(
        ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(
            u => u.Email == email && u.IsActive, cancellationToken);
        if (user is null) return false;

        var record = await db.PasswordResetCodes
            .Where(c => c.UserId == user.Id && c.ConsumedAt == null && c.VerifiedAt != null)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (record?.ResetTokenHash is null || record.ResetTokenExpiresAt is not { } expiry
            || expiry < DateTime.UtcNow)
        {
            return false;
        }

        var verification = passwordHasher.VerifyHashedPassword(user, record.ResetTokenHash, request.ResetToken);
        if (verification == PasswordVerificationResult.Failed)
        {
            return false;
        }

        user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;
        record.ConsumedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Password reset completed for account {UserId}", user.Id);
        return true;
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
