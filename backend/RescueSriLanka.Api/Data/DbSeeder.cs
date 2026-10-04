using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Data;

/// <summary>
/// Creates the accounts the team develops and demonstrates against.
/// Idempotent and safe to run on every start-up. Existing accounts keep their
/// password and profile, but their role is realigned with the seed list so a
/// stale role (e.g. from an earlier seed) can't route a user to the wrong
/// dashboard.
/// </summary>
public static class DbSeeder
{
    private record SeedUser(string FullName, string Email, string Password, UserRole Role, string? Phone);

    private static readonly SeedUser[] Accounts =
    [
        new("Lelum Jayasooriya", "emergency@rescue.lk", "Rescue@123", UserRole.EmergencyCoordinator, "+94711000001"),
        new("Help Request Manager", "helprequests@rescue.lk", "Rescue@123", UserRole.HelpRequestManager, null),
        new("Resource Manager", "resources@rescue.lk", "Rescue@123", UserRole.ResourceManager, null),
        new("Rescue Coordinator", "rescue@rescue.lk", "Rescue@123", UserRole.RescueTeam, null)
    ];

    public static async Task SeedAsync(
        AppDbContext db,
        IPasswordHasher<User> passwordHasher,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var seedEmails = Accounts.Select(a => a.Email.ToLowerInvariant()).ToList();
        var existing = await db.Users
            // A citizen may have registered with one of these addresses; that is a separate account.
            .Where(u => seedEmails.Contains(u.Email) && u.Role != UserRole.Citizen)
            .ToDictionaryAsync(u => u.Email, cancellationToken);

        var added = 0;
        var updated = 0;

        foreach (var account in Accounts)
        {
            var email = account.Email.ToLowerInvariant();
            if (existing.TryGetValue(email, out var current))
            {
                if (current.Role != account.Role)
                {
                    current.Role = account.Role;
                    updated++;
                }
                continue;
            }

            var user = new User
            {
                FullName = account.FullName,
                Email = email,
                PasswordHash = string.Empty,
                Role = account.Role,
                PhoneNumber = account.Phone
            };
            user.PasswordHash = passwordHasher.HashPassword(user, account.Password);

            db.Users.Add(user);
            added++;
        }

        if (added > 0 || updated > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded {Added} account(s), corrected role on {Updated}.", added, updated);
        }
        else
        {
            logger.LogInformation("Seed accounts already present — nothing seeded.");
        }
    }
}
