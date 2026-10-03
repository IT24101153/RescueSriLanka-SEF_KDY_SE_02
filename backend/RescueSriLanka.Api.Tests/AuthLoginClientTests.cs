using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.DTOs.Auth;
using RescueSriLanka.Api.Features.ComponentA.Services.Notifications;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Services;
using RescueSriLanka.Api.Services.Email;
using RescueSriLanka.Api.Services.Storage;

namespace RescueSriLanka.Api.Tests;

public sealed class AuthLoginClientTests : IDisposable
{
    private const string Password = "Correct-Horse-1";

    private readonly AppDbContext db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private readonly PasswordHasher<User> hasher = new();

    public void Dispose() => db.Dispose();

    private AuthService Service() => new(
        db,
        new StubTokenService(),
        hasher,
        new NoOpNotificationQueue(),
        new NoOpImageStore(),
        new NoOpEmailSender(),
        NullLogger<AuthService>.Instance);

    private async Task<User> AddAccount(string email, UserRole role)
    {
        var user = new User { FullName = email, Email = email, PasswordHash = string.Empty, Role = role };
        user.PasswordHash = hasher.HashPassword(user, Password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static LoginRequest Request(string email, string password = Password) =>
        new() { Email = email, Password = password };

    [Fact]
    public async Task CitizenSignsInThroughTheAppButNotThePortal()
    {
        await AddAccount("citizen@example.com", UserRole.Citizen);

        Assert.NotNull(await Service().LoginAsync(Request("citizen@example.com"), LoginClient.CitizenApp));
        Assert.Null(await Service().LoginAsync(Request("citizen@example.com"), LoginClient.StaffPortal));
    }

    [Theory]
    [InlineData(UserRole.EmergencyCoordinator)]
    [InlineData(UserRole.HelpRequestManager)]
    [InlineData(UserRole.ResourceManager)]
    [InlineData(UserRole.RescueTeam)]
    public async Task StaffSignInThroughThePortalButNotTheApp(UserRole role)
    {
        var email = $"{role}@example.com".ToLowerInvariant();
        await AddAccount(email, role);

        Assert.NotNull(await Service().LoginAsync(Request(email), LoginClient.StaffPortal));
        Assert.Null(await Service().LoginAsync(Request(email), LoginClient.CitizenApp));
    }

    [Fact]
    public async Task WrongClientAndWrongPasswordGiveTheSameAnswer()
    {
        await AddAccount("staff@example.com", UserRole.ResourceManager);

        var refusedClient = await Service().LoginAsync(Request("staff@example.com"), LoginClient.CitizenApp);
        var wrongPassword = await Service().LoginAsync(Request("staff@example.com", "wrong-password"), LoginClient.StaffPortal);

        Assert.Null(refusedClient);
        Assert.Null(wrongPassword);
    }

    private sealed class StubTokenService : IJwtTokenService
    {
        public (string Token, DateTime ExpiresAt) CreateToken(User user) => ("stub", DateTime.UtcNow.AddHours(1));
    }

    private sealed class NoOpNotificationQueue : INotificationQueue
    {
        public void Enqueue(NotificationJob job)
        {
        }
    }

    private sealed class NoOpImageStore : IImageStore
    {
        public string Name => "test";

        public Task<StoredImage> SaveAsync(
            Guid ownerId, string category, Microsoft.AspNetCore.Http.IFormFile file, CancellationToken ct = default) =>
            Task.FromResult(new StoredImage("https://cdn.test/none.jpg", "none"));

        public Task<byte[]?> ReadAsync(string location, CancellationToken ct = default) =>
            Task.FromResult<byte[]?>(null);
    }

    private sealed class NoOpEmailSender : IEmailSender
    {
        public string Name => "test";

        public Task<bool> SendAsync(EmailMessage message, CancellationToken ct = default) =>
            Task.FromResult(true);
    }
}
