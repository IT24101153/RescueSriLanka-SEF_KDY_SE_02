using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentB.Models;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Features.ComponentB.Data;

/// <summary>
/// Adds clearly marked demo records only in Development. The seeded citizen is
/// inactive and has a random password; sample data can never be mistaken for a
/// live report or used as a login.
/// </summary>
public static class ComponentBDataSeeder
{
    private static readonly Guid DemoCitizenId = Guid.Parse("b0000000-0000-0000-0000-000000000001");
    private static readonly Guid DemoRequestId = Guid.Parse("b0000000-0000-0000-0000-000000000002");
    private static readonly Guid DemoAdvisoryId = Guid.Parse("b0000000-0000-0000-0000-000000000003");

    public static async Task SeedAsync(
        AppDbContext db,
        IPasswordHasher<User> passwordHasher,
        CancellationToken cancellationToken = default)
    {
        var citizen = await db.Users.SingleOrDefaultAsync(user => user.Id == DemoCitizenId, cancellationToken);
        if (citizen is null)
        {
            citizen = new User
            {
                Id = DemoCitizenId,
                FullName = "Component B demo citizen (inactive)",
                Email = "component-b-demo@rescue.local",
                PasswordHash = string.Empty,
                Role = UserRole.Citizen,
                IsActive = false
            };
            citizen.PasswordHash = passwordHasher.HashPassword(
                citizen, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            db.Users.Add(citizen);
        }

        if (!await db.HelpRequests.AnyAsync(request => request.Id == DemoRequestId, cancellationToken))
        {
            db.HelpRequests.Add(new HelpRequest
            {
                Id = DemoRequestId,
                CitizenId = DemoCitizenId,
                Type = HelpRequestType.Water,
                Description = "[DEMO ONLY] Sample request for the request tracking screen. Not a real emergency.",
                Latitude = 7.2906,
                Longitude = 80.6337,
                UrgencyScore = 20,
                Status = HelpRequestStatus.Resolved,
                VerificationStatus = VerificationStatus.Verified,
                CreatedAt = DateTime.UtcNow.AddHours(-3),
                UpdatedAt = DateTime.UtcNow.AddHours(-1)
            });

            db.RequestStatusHistories.AddRange(
                History(HelpRequestStatus.Pending, HelpRequestStatus.Assigned, "[DEMO] Request assigned."),
                History(HelpRequestStatus.Assigned, HelpRequestStatus.InProgress, "[DEMO] Response started."),
                History(HelpRequestStatus.InProgress, HelpRequestStatus.Resolved, "[DEMO] Sample request resolved."));
        }

        if (!await db.TravelAdvisories.AnyAsync(advisory => advisory.Id == DemoAdvisoryId, cancellationToken))
        {
            db.TravelAdvisories.Add(new TravelAdvisory
            {
                Id = DemoAdvisoryId,
                AreaName = "Development fixture (not a real location alert)",
                Latitude = 0,
                Longitude = 0,
                RadiusMeters = 10,
                SafetyLevel = SafetyLevel.Safe,
                Reason = "[DEMO ONLY] Fixture outside Sri Lanka; this is not a live safety advisory."
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static RequestStatusHistory History(HelpRequestStatus previous, HelpRequestStatus next, string notes) => new()
    {
        HelpRequestId = DemoRequestId,
        OldStatus = previous,
        NewStatus = next,
        ChangedByUserId = DemoCitizenId,
        Notes = notes
    };
}
