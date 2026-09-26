using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RescueSriLanka.Api.DTOs.Auth;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentA.Services.Notifications;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Services;
using RescueSriLanka.Api.Services.Storage;

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

    /// <summary>Records what emails were asked for instead of sending them.</summary>
    private sealed class RecordingQueue : INotificationQueue
    {
        public List<NotificationJob> Jobs { get; } = [];

        public void Enqueue(NotificationJob job) => Jobs.Add(job);
    }

    /// <summary>Not exercised here — these tests never touch the photo.</summary>
    private sealed class StubImageStore : IImageStore
    {
        public string Name => "stub";

        public Task<StoredImage> SaveAsync(
            Guid ownerId, string category, IFormFile file, CancellationToken ct = default) =>
            Task.FromResult(new StoredImage("https://cdn.test/avatar.jpg", "avatar"));

        public Task<byte[]?> ReadAsync(string location, CancellationToken ct = default) =>
            Task.FromResult<byte[]?>(null);
    }

    private static AuthService NewService(AppDbContext db, RecordingQueue? queue = null) =>
        new(db, new StubTokenService(), new PasswordHasher<User>(),
            queue ?? new RecordingQueue(), new StubImageStore(), NullLogger<AuthService>.Instance);

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    /// <summary>Builds the request as the JSON body would actually arrive.</summary>
    private static UpdatePreferencesRequest FromJson(string json) =>
        JsonSerializer.Deserialize<UpdatePreferencesRequest>(json, WebJson)!;

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

    // --------------------------------------------------------- phone number

    private static async Task<User> NewUserWithPhoneAsync(AppDbContext db, string? phone = "0771234567")
    {
        var user = new User
        {
            FullName = "Test Citizen",
            Email = "citizen@example.com",
            PasswordHash = "-",
            PhoneNumber = phone
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task ExplicitNullClearsThePhoneNumber()
    {
        using var db = NewDb();
        var user = await NewUserWithPhoneAsync(db);

        var updated = await NewService(db)
            .UpdatePreferencesAsync(user.Id, FromJson("""{"phoneNumber":null}"""));

        Assert.Null(updated!.PhoneNumber);
    }

    [Fact]
    public async Task OmittingThePhoneNumberLeavesItAlone()
    {
        using var db = NewDb();
        var user = await NewUserWithPhoneAsync(db);

        var updated = await NewService(db).UpdatePreferencesAsync(
            user.Id, FromJson("""{"emailNotificationsEnabled":false}"""));

        Assert.Equal("0771234567", updated!.PhoneNumber);
    }

    [Fact]
    public async Task AnEmptyStringAlsoClearsThePhoneNumber()
    {
        using var db = NewDb();
        var user = await NewUserWithPhoneAsync(db);

        var updated = await NewService(db)
            .UpdatePreferencesAsync(user.Id, FromJson("""{"phoneNumber":"  "}"""));

        Assert.Null(updated!.PhoneNumber);
    }

    [Fact]
    public async Task APhoneNumberCanBeSet()
    {
        using var db = NewDb();
        var user = await NewUserWithPhoneAsync(db, phone: null);

        var updated = await NewService(db)
            .UpdatePreferencesAsync(user.Id, FromJson("""{"phoneNumber":"0711234567"}"""));

        Assert.Equal("0711234567", updated!.PhoneNumber);
    }

    // ------------------------------------------------------ emails triggered

    private static RegisterRequest NewRegistration(string? district) => new()
    {
        FullName = "New Citizen",
        Email = "New@Example.com",
        Password = "correct-horse",
        District = district
    };

    [Fact]
    public async Task RegisteringQueuesAWelcomeAndStoresTheDistrictCanonically()
    {
        using var db = NewDb();
        var queue = new RecordingQueue();

        var response = await NewService(db, queue)
            .RegisterCitizenAsync(NewRegistration("  nuwara-eliya "));

        Assert.NotNull(response);
        Assert.Equal("Nuwara Eliya", response.User.District);
        Assert.Equal(
            new NotificationJob(NotificationKind.Welcome, response.User.Id),
            Assert.Single(queue.Jobs));
    }

    [Fact]
    public async Task RegisteringWithAnUnknownDistrictIsRejected()
    {
        using var db = NewDb();
        var queue = new RecordingQueue();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            NewService(db, queue).RegisterCitizenAsync(NewRegistration("Atlantis")));

        Assert.Empty(queue.Jobs);
        Assert.Empty(db.Users);
    }

    [Fact]
    public async Task ChangingTheDistrictQueuesABriefing()
    {
        using var db = NewDb();
        var user = await NewUserAsync(db, district: "Colombo");
        var queue = new RecordingQueue();

        await NewService(db, queue).UpdatePreferencesAsync(
            user.Id, FromJson("""{ "district": "Kandy" }"""));

        Assert.Equal(
            new NotificationJob(NotificationKind.DistrictBriefing, user.Id),
            Assert.Single(queue.Jobs));
    }

    [Theory]
    [InlineData("""{ "emailNotificationsEnabled": false }""")]
    [InlineData("""{ "district": "colombo" }""")]
    [InlineData("""{ "district": null }""")]
    public async Task NoBriefingUnlessTheDistrictActuallyChanges(string json)
    {
        using var db = NewDb();
        var user = await NewUserAsync(db, district: "Colombo");
        var queue = new RecordingQueue();

        await NewService(db, queue).UpdatePreferencesAsync(user.Id, FromJson(json));

        Assert.Empty(queue.Jobs);
    }
}
