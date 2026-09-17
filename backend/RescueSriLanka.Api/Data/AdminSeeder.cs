using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Data
{
    // Idempotent — checks before inserting, so it's safe to run on every startup.
    // Local dev/testing only. Remove or replace once the team's real Auth/seeding
    // approach is merged in.
    public static class AdminSeeder
    {
        public static async Task SeedAdminAsync(ApplicationDbContext db)
        {
            const string adminEmail = "admin@rescuesrilanka.lk";
            const string adminPassword = "Admin@12345";

            bool exists = await db.Users.AnyAsync(u => u.Email == adminEmail);
            if (exists) return;

            var admin = new User
            {
                FullName = "Coordinator Admin",
                Email = adminEmail,
                PasswordHash = string.Empty,
                Role = UserRole.EmergencyCoordinator,
                IsActive = true
            };

            var hasher = new PasswordHasher<User>();
            admin.PasswordHash = hasher.HashPassword(admin, adminPassword);

            db.Users.Add(admin);
            await db.SaveChangesAsync();
        }
    }
}