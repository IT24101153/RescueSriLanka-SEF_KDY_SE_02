using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Data;

/// <summary>
/// Creates the accounts the team develops and demonstrates against.
/// Idempotent: existing emails are left untouched, so it is safe to run on
/// every start-up.
/// </summary>
public static class DbSeeder
{
    private record SeedUser(string FullName, string Email, string Password, UserRole Role, string? Phone);

    private static readonly SeedUser[] Accounts =
    [
        new("Lelum Jayasooriya", "coordinator@rescue.lk", "Rescue@123", UserRole.EmergencyCoordinator, "+94711000001"),
        new("Help Request Manager", "helprequests@rescue.lk", "Rescue@123", UserRole.HelpRequestManager, null),
        new("Resource Manager", "resources@rescue.lk", "Rescue@123", UserRole.ResourceManager, null)
    ];

    public static async Task SeedAsync(
        AppDbContext db,
        IPasswordHasher<User> passwordHasher,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var existing = await db.Users
            .Select(u => u.Email)
            .ToListAsync(cancellationToken);

        var added = 0;

        foreach (var account in Accounts)
        {
            var email = account.Email.ToLowerInvariant();
            if (existing.Contains(email))
            {
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

        if (added > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded {Count} account(s).", added);
        }
        else
        {
            logger.LogInformation("Seed accounts already present — nothing seeded.");
        }
    }
}
