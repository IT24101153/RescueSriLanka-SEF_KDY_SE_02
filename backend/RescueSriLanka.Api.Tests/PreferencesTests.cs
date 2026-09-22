using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RescueSriLanka.Api.DTOs.Auth;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Services;

namespace RescueSriLanka.Api.Tests;

/// <summary>
/// The district setting has three states, and the difference between two of
/// them is invisible in a plain string: "leave it alone" and "clear it" both
/// arrive as null unless the request models them apart.
///
/// Getting that wrong is silent in both directions — either the app's "No
/// district" option does nothing, or toggling the email switch quietly
/// unsubscribes someone from their district's warnings. These tests exist
/// because the first of those shipped and was caught by hand.
/// </summary>
public class PreferencesTests
{
    private sealed class StubTokenService : IJwtTokenService
    {
        public (string Token, DateTime ExpiresAt) CreateToken(User user) =>
            ("stub", DateTime.UtcNow.AddHours(1));
    }

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"prefs-{Guid.NewGuid()}")
            .Options);

    private static AuthService NewService(AppDbContext db) =>
        new(db, new StubTokenService(), new PasswordHasher<User>(),
            NullLogger<AuthService>.Instance);

    /// <summary>Builds the request as the JSON body would actually arrive.</summary>
    private static UpdatePreferencesRequest FromJson(string json) =>
        JsonSerializer.Deserialize<UpdatePreferencesRequest>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private static async Task<User> NewUserAsync(AppDbContext db, string? district = "Colombo")
    {
        var user = new User
        {
            FullName = "Test Citizen",
            Email = "citizen@example.com",
            PasswordHash = "-",
            District = district
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task ExplicitNullClearsTheDistrict()
    {
        using var db = NewDb();
        var user = await NewUserAsync(db);

        var updated = await NewService(db)
            .UpdatePreferencesAsync(user.Id, FromJson("""{"district":null}"""));

        Assert.Null(updated!.District);
    }

    [Fact]
    public async Task OmittingTheDistrictLeavesItAlone()
    {
        using var db = NewDb();
        var user = await NewUserAsync(db);

        // Toggling only the email switch must not unsubscribe anyone.
        var updated = await NewService(db).UpdatePreferencesAsync(
            user.Id, FromJson("""{"emailNotificationsEnabled":false}"""));

        Assert.Equal("Colombo", updated!.District);
        Assert.False(updated.EmailNotificationsEnabled);
    }

    [Fact]
    public async Task ADistrictIsStoredInItsCanonicalSpelling()
    {
        using var db = NewDb();
        var user = await NewUserAsync(db, district: null);

        var updated = await NewService(db)
            .UpdatePreferencesAsync(user.Id, FromJson("""{"district":"nuwara-eliya"}"""));

        Assert.Equal("Nuwara Eliya", updated!.District);
    }

    [Fact]
    public async Task AnEmptyStringAlsoClears()
    {
        using var db = NewDb();
        var user = await NewUserAsync(db);

        var updated = await NewService(db)
            .UpdatePreferencesAsync(user.Id, FromJson("""{"district":"  "}"""));

        Assert.Null(updated!.District);
    }

    [Fact]
    public async Task AnUnknownDistrictIsRejected()
    {
        using var db = NewDb();
        var user = await NewUserAsync(db);
        var service = NewService(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdatePreferencesAsync(user.Id, FromJson("""{"district":"Chennai"}""")));

        // The stored value is untouched rather than half-applied.
        Assert.Equal("Colombo", db.Users.Single().District);
    }

    [Fact]
    public async Task AWronglyTypedDistrictIsRejected()
    {
        using var db = NewDb();
        var user = await NewUserAsync(db);
        var service = NewService(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdatePreferencesAsync(user.Id, FromJson("""{"district":42}""")));
    }

    [Fact]
    public async Task BothFieldsChangeTogether()
    {
        using var db = NewDb();
        var user = await NewUserAsync(db, district: null);

        var updated = await NewService(db).UpdatePreferencesAsync(
            user.Id,
            FromJson("""{"district":"Kandy","emailNotificationsEnabled":true}"""));

        Assert.Equal("Kandy", updated!.District);
        Assert.True(updated.EmailNotificationsEnabled);
    }
}
