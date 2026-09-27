using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentB.Data;
using RescueSriLanka.Api.Features.ComponentB.Models;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Tests;

public class ComponentBDataSeederTests
{
    [Fact]
    public async Task DevelopmentSeed_IsIdempotentAndContainsOnlyClearlyMarkedFixtures()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var hasher = new PasswordHasher<User>();

        await ComponentBDataSeeder.SeedAsync(db, hasher);
        await ComponentBDataSeeder.SeedAsync(db, hasher);

        var demoUser = await db.Users.SingleAsync(user => user.Email == "component-b-demo@rescue.local");
        var request = await db.HelpRequests.SingleAsync();
        var advisory = await db.TravelAdvisories.SingleAsync();

        Assert.False(demoUser.IsActive);
        Assert.Equal(VerificationStatus.Verified, request.VerificationStatus);
        Assert.Contains("DEMO ONLY", request.Description);
        Assert.Equal(3, await db.RequestStatusHistories.CountAsync());
        Assert.Equal(SafetyLevel.Safe, advisory.SafetyLevel);
        Assert.Contains("DEMO ONLY", advisory.Reason);
    }

    [Fact]
    public void ComponentBModel_HasExpectedDatabaseConstraintsIndexesAndRelationships()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var request = db.Model.FindEntityType(typeof(HelpRequest))!;
        var advisory = db.Model.FindEntityType(typeof(TravelAdvisory))!;

        Assert.Contains(request.GetCheckConstraints(), check => check.Name == "CK_HelpRequests_UrgencyScore");
        Assert.Contains(request.GetIndexes(), index => index.Properties.Select(p => p.Name)
            .SequenceEqual([nameof(HelpRequest.Status), nameof(HelpRequest.UrgencyScore)]));
        Assert.Contains(request.GetForeignKeys(), foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(User));
        Assert.Contains(advisory.GetCheckConstraints(), check => check.Name == "CK_TravelAdvisories_RadiusMeters");
        Assert.Contains(advisory.GetIndexes(), index => index.Properties.Single().Name == nameof(TravelAdvisory.ExpiresAt));
    }
}
