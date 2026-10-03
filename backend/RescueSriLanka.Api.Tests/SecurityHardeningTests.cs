using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.DTOs.Auth;
using RescueSriLanka.Api.Features.ComponentA.Models;
using RescueSriLanka.Api.Features.ComponentA.Services;
using RescueSriLanka.Api.Features.ComponentA.Services.Notifications;
using RescueSriLanka.Api.Features.ComponentC.DTOs;
using RescueSriLanka.Api.Features.ComponentC.Models;
using RescueSriLanka.Api.Features.ComponentC.Services;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Services;
using RescueSriLanka.Api.Services.Email;
using RescueSriLanka.Api.Services.Storage;

namespace RescueSriLanka.Api.Tests;

public sealed class SecurityHardeningTests : IDisposable
{
    private readonly AppDbContext db = new(new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private readonly PasswordHasher<User> hasher = new();
    private readonly CountingEmailSender emails = new();

    public void Dispose() => db.Dispose();

    private AuthService Auth() => new(
        db, new StubTokens(), hasher, new NoOpQueue(), new NoImages(), emails, NullLogger<AuthService>.Instance);

    private async Task<User> AddUser(string email, UserRole role = UserRole.Citizen, bool active = true)
    {
        var user = new User { FullName = email, Email = email, PasswordHash = string.Empty, Role = role, IsActive = active };
        user.PasswordHash = hasher.HashPassword(user, "Correct-Horse-1");
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    // ---- authentication

    [Fact]
    public async Task ResetRequestsAreCappedPerAccount()
    {
        await AddUser("reset@example.com");

        for (var i = 0; i < 5; i++)
        {
            await Auth().RequestPasswordResetAsync(new ForgotPasswordRequest { Email = "reset@example.com" });
        }

        Assert.Equal(3, emails.Sent);
    }

    [Fact]
    public async Task AGuessCounterCannotBeResetByRequestingAFreshCode()
    {
        var user = await AddUser("guess@example.com");
        db.PasswordResetCodes.Add(new PasswordResetCode
        {
            UserId = user.Id,
            CodeHash = hasher.HashPassword(user, "123456"),
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            Attempts = 5
        });
        await db.SaveChangesAsync();

        // Even the right code is refused once the account has used up its guesses this hour.
        var result = await Auth().VerifyResetCodeAsync(
            new VerifyResetCodeRequest { Email = "guess@example.com", Code = "123456" });

        Assert.Null(result);
    }

    [Fact]
    public async Task RegisteringAnExistingEmailReportsNothingAndCreatesNothing()
    {
        await AddUser("taken@example.com");
        var request = new RegisterRequest
        {
            FullName = "Someone", Email = "taken@example.com", Password = "Another-Pass-2"
        };

        var created = await Auth().RegisterCitizenAsync(request);

        Assert.False(created);
        Assert.Single(db.Users);
    }

    [Fact]
    public async Task DisabledAccountsCannotSignIn()
    {
        await AddUser("disabled@example.com", active: false);

        var result = await Auth().LoginAsync(
            new LoginRequest { Email = "disabled@example.com", Password = "Correct-Horse-1" }, LoginClient.CitizenApp);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("short1")]
    [InlineData("nodigitshere")]
    [InlineData("1234567890")]
    public void NewPasswordsNeedTenCharactersALetterAndANumber(string password)
    {
        var request = new RegisterRequest { FullName = "A", Email = "a@example.com", Password = password };

        var valid = Validator.TryValidateObject(request, new ValidationContext(request), null, validateAllProperties: true);

        Assert.False(valid);
    }

    // ---- uploads

    [Fact]
    public void AFileWithoutAnImageHeaderIsRefused_EvenWhenItClaimsToBeAJpeg()
    {
        var bytes = "<html><script>alert(1)</script></html>"u8.ToArray();
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "photo", "page.html")
        {
            Headers = new HeaderDictionary { ["Content-Type"] = "image/jpeg" }
        };

        Assert.Throws<ArgumentException>(() => ImageStorageService.Validate(file));
    }

    [Fact]
    public void AJpegHeaderIsAccepted()
    {
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1 };
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "photo", "photo.jpg")
        {
            Headers = new HeaderDictionary { ["Content-Type"] = "image/jpeg" }
        };

        Assert.Equal("image/jpeg", ImageStorageService.Validate(file));
    }

    [Fact]
    public async Task APhotoIsRefusedWithAClearErrorWhenCloudinaryIsNotConfigured()
    {
        var store = new CloudinaryImageStore(
            Options.Create(new CloudinaryOptions()), HttpClients(), NullLogger<CloudinaryImageStore>.Instance);
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1 };
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "photo", "photo.jpg")
        {
            Headers = new HeaderDictionary { ["Content-Type"] = "image/jpeg" }
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(Guid.NewGuid(), "incidents", file));

        // The client-facing message names no settings; the log does.
        Assert.Equal("Photo storage is not configured on the server.", error.Message);
    }

    [Theory]
    [InlineData("/uploads/incidents/abc/photo.jpg")]
    [InlineData("http://insecure.example/photo.jpg")]
    public async Task ALocationThatIsNotAnHttpsUrlIsSkippedWithoutAFetch(string location)
    {
        var store = new CloudinaryImageStore(
            Options.Create(new CloudinaryOptions { CloudName = "demo", ApiKey = "key", ApiSecret = "secret" }),
            HttpClients(), NullLogger<CloudinaryImageStore>.Instance);

        Assert.Null(await store.ReadAsync(location));
    }

    private static IHttpClientFactory HttpClients() =>
        new ServiceCollection().AddHttpClient().BuildServiceProvider().GetRequiredService<IHttpClientFactory>();

    // ---- resources

    [Fact]
    public async Task AllocationNeedsAChosenResource()
    {
        await using var context = CreateResourceContext();
        var service = new ResourceManagementService(context);

        await Assert.ThrowsAsync<ArgumentException>(() => service.AllocateAsync(
            new AllocateResourceRequest("DonatedSupply", Guid.Empty, 1, null, null), CancellationToken.None));
    }

    [Fact]
    public async Task MedicalSuppliesAreAllocatedInWholeUnits()
    {
        await using var context = CreateResourceContext();
        var supplyId = Guid.NewGuid();
        context.MedicalSupplies.Add(new MedicalSupply { Id = supplyId, Name = "Bandages", Unit = "packs", QuantityOnHand = 10, LowStockThreshold = 2 });
        await context.SaveChangesAsync();
        var service = new ResourceManagementService(context);

        await Assert.ThrowsAsync<ArgumentException>(() => service.AllocateAsync(
            new AllocateResourceRequest("MedicalSupply", supplyId, 2.5m, null, null), CancellationToken.None));
    }

    [Fact]
    public async Task ReleasingADonatedAllocationGivesTheStockBack()
    {
        await using var context = CreateResourceContext();
        var supplyId = Guid.NewGuid();
        context.DonatedSupplies.Add(new DonatedSupply
        {
            Id = supplyId, DonationId = Guid.NewGuid(), Name = "Blankets", DonorName = "Donor", QuantityOnHand = 4, Unit = "pieces"
        });
        await context.SaveChangesAsync();
        var service = new ResourceManagementService(context);

        var allocation = await service.AllocateAsync(
            new AllocateResourceRequest("DonatedSupply", supplyId, 4, null, null), CancellationToken.None);
        Assert.False((await context.DonatedSupplies.FindAsync(supplyId))!.IsActive);

        await service.ReleaseAsync(allocation.Id, CancellationToken.None);

        var supply = (await context.DonatedSupplies.FindAsync(supplyId))!;
        Assert.Equal(4, supply.QuantityOnHand);
        Assert.True(supply.IsActive);
    }

    private AppDbContext CreateResourceContext() => new(new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    // ---- test doubles

    private sealed class CountingEmailSender : IEmailSender
    {
        public int Sent { get; private set; }
        public string Name => "test";

        public Task<bool> SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            Sent++;
            return Task.FromResult(true);
        }
    }

    private sealed class StubTokens : IJwtTokenService
    {
        public (string Token, DateTime ExpiresAt) CreateToken(User user) => ("stub", DateTime.UtcNow.AddHours(1));
    }

    private sealed class NoOpQueue : INotificationQueue
    {
        public void Enqueue(NotificationJob job)
        {
        }
    }

    private sealed class NoImages : IImageStore
    {
        public string Name => "test";

        public Task<StoredImage> SaveAsync(Guid ownerId, string category, IFormFile file, CancellationToken ct = default) =>
            Task.FromResult(new StoredImage("https://cdn.test/none.jpg", "none"));

        public Task<byte[]?> ReadAsync(string location, CancellationToken ct = default) => Task.FromResult<byte[]?>(null);
    }
}
