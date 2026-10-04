using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RescueSriLanka.Api.Controllers;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.DTOs;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Services.Push;

namespace RescueSriLanka.Api.Tests;

/// <summary>Who gets a push, how a device is registered, and when a dead token is dropped.</summary>
public class PushNotificationTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"push-{Guid.NewGuid()}")
            .Options);

    private static User NewUser(string email, bool push = true, bool active = true) => new()
    {
        FullName = email, Email = email, PasswordHash = "-", PushNotificationsEnabled = push, IsActive = active
    };

    private sealed class RecordingQueue : IPushQueue
    {
        public List<PushDelivery> Queued { get; } = [];

        public bool TryQueue(PushDelivery delivery)
        {
            Queued.Add(delivery);
            return true;
        }
    }

    // ---------------------------------------------------------- recipients

    [Fact]
    public async Task OnlyActiveOptedInUsersWithADeviceAreQueued()
    {
        using var db = NewDb();
        var optedIn = NewUser("in@example.com");
        var optedOut = NewUser("out@example.com", push: false);
        var inactive = NewUser("gone@example.com", active: false);
        var noDevice = NewUser("quiet@example.com");
        db.Users.AddRange(optedIn, optedOut, inactive, noDevice);
        db.DeviceTokens.AddRange(
            new DeviceToken { UserId = optedIn.Id, Token = "phone-one-token-0001", Platform = "android" },
            new DeviceToken { UserId = optedIn.Id, Token = "tablet-token-0002", Platform = "ios" },
            new DeviceToken { UserId = optedOut.Id, Token = "silent-token-0003", Platform = "android" },
            new DeviceToken { UserId = inactive.Id, Token = "closed-token-0004", Platform = "android" });
        await db.SaveChangesAsync();

        var queue = new RecordingQueue();
        var service = new PushNotificationService(db, queue, Options.Create(new PushOptions()));

        var queued = await service.SendToUsersAsync(
            [optedIn.Id, optedOut.Id, inactive.Id, noDevice.Id], new PushMessage("Title", "Body"));

        Assert.Equal(2, queued);
        Assert.Equal(["phone-one-token-0001", "tablet-token-0002"], queue.Queued.Select(d => d.Token).Order());
    }

    [Fact]
    public async Task NothingIsQueuedWhenPushIsSwitchedOffInConfiguration()
    {
        using var db = NewDb();
        var user = NewUser("in@example.com");
        db.Users.Add(user);
        db.DeviceTokens.Add(new DeviceToken { UserId = user.Id, Token = "phone-one-token-0001", Platform = "android" });
        await db.SaveChangesAsync();

        var queue = new RecordingQueue();
        var service = new PushNotificationService(db, queue, Options.Create(new PushOptions { Enabled = false }));

        Assert.Equal(0, await service.SendToUsersAsync([user.Id], new PushMessage("Title", "Body")));
        Assert.Empty(queue.Queued);
    }

    // ---------------------------------------------------------- Firebase

    [Fact]
    public void TheFirebaseAssertionIsSignedByTheServiceAccountKey()
    {
        using var rsa = RSA.Create(2048);
        var credentials = new FcmAccessTokens.Credentials
        {
            ClientEmail = "sender@project.iam.gserviceaccount.com",
            TokenUri = "https://oauth2.googleapis.com/token",
            Key = rsa
        };

        var jwt = FcmAccessTokens.BuildAssertion(credentials, DateTimeOffset.UtcNow);

        var parts = jwt.Split('.');
        Assert.Equal(3, parts.Length);
        var signed = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        Assert.True(rsa.VerifyData(signed, FromBase64Url(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        var claims = JsonDocument.Parse(FromBase64Url(parts[1])).RootElement;
        Assert.Equal("sender@project.iam.gserviceaccount.com", claims.GetProperty("iss").GetString());
        Assert.Equal(FcmAccessTokens.Scope, claims.GetProperty("scope").GetString());
    }

    [Fact]
    public void AServiceAccountFileIsReadIntoASigningKey()
    {
        using var rsa = RSA.Create(2048);
        var path = Path.Combine(Path.GetTempPath(), $"fcm-{Guid.NewGuid()}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            client_email = "sender@project.iam.gserviceaccount.com",
            private_key = rsa.ExportPkcs8PrivateKeyPem(),
            token_uri = "https://oauth2.googleapis.com/token"
        }));

        try
        {
            var credentials = FcmAccessTokens.Credentials.Load(path);

            Assert.Equal("sender@project.iam.gserviceaccount.com", credentials.ClientEmail);
            Assert.Equal(2048, credentials.Key.KeySize);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AServiceAccountGivenAsASettingValueIsReadTheSameWay()
    {
        using var rsa = RSA.Create(2048);
        var json = JsonSerializer.Serialize(new
        {
            client_email = "sender@project.iam.gserviceaccount.com",
            private_key = rsa.ExportPkcs8PrivateKeyPem(),
            token_uri = "https://oauth2.googleapis.com/token"
        });

        var credentials = FcmAccessTokens.Credentials.Parse(json);

        Assert.Equal("sender@project.iam.gserviceaccount.com", credentials.ClientEmail);
        Assert.True(new PushOptions.FcmOptions { ProjectId = "project", CredentialsJson = json }.IsConfigured);
        Assert.False(new PushOptions.FcmOptions { ProjectId = "project" }.IsConfigured);
    }

    [Theory]
    [InlineData(404, "{\"error\":{\"details\":[{\"errorCode\":\"UNREGISTERED\"}]}}", true)]
    [InlineData(400, "The registration token is not a valid FCM registration token", true)]
    [InlineData(400, "{\"error\":{\"message\":\"Invalid JSON payload\"}}", false)]
    [InlineData(500, "UNREGISTERED", false)]
    [InlineData(404, "{\"error\":{\"status\":\"NOT_FOUND\"}}", false)]
    public void OnlyFirebaseSaysADeviceIsGoneRemovesItsToken(int status, string body, bool expected)
    {
        Assert.Equal(expected, FcmPushSender.IsDeadToken((HttpStatusCode)status, body));
    }

    [Fact]
    public async Task AStaleTokenFirebaseReportsIsRemovedByTheWorker()
    {
        var databaseName = $"push-worker-{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddScoped<IPushSender>(_ => new FirebaseDeadSender());
        var provider = services.BuildServiceProvider();

        var user = NewUser("citizen@example.com");
        using (var seed = provider.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(user);
            db.DeviceTokens.Add(new DeviceToken { UserId = user.Id, Token = "uninstalled-token-0001", Platform = "android" });
            await db.SaveChangesAsync();
        }

        var queue = new PushQueue(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<PushQueue>.Instance);
        await queue.StartAsync(CancellationToken.None);
        try
        {
            queue.TryQueue(new PushDelivery("uninstalled-token-0001", new PushMessage("Title", "Body")));

            var removed = false;
            for (var attempt = 0; attempt < 250 && !removed; attempt++)
            {
                await Task.Delay(20);
                using var check = provider.CreateScope();
                removed = !await check.ServiceProvider.GetRequiredService<AppDbContext>()
                    .DeviceTokens.AnyAsync(device => device.Token == "uninstalled-token-0001");
            }

            Assert.True(removed);
        }
        finally
        {
            await queue.StopAsync(CancellationToken.None);
        }
    }

    private sealed class FirebaseDeadSender : IPushSender
    {
        public string Name => "test";

        public Task<PushOutcome> SendAsync(string token, PushMessage message, CancellationToken ct = default) =>
            Task.FromResult(PushOutcome.TokenInvalid);
    }

    // ---------------------------------------------------------- registration

    [Fact]
    public async Task ARegisteredPhoneBelongsToTheCitizenWhoLastSignedInOnIt()
    {
        using var db = NewDb();
        var first = NewUser("first@example.com");
        var second = NewUser("second@example.com");
        db.Users.AddRange(first, second);
        await db.SaveChangesAsync();
        const string token = "shared-phone-token-0001";

        var controller = new PushDevicesController(db) { ControllerContext = SignedInAs(first.Id) };
        await controller.Register(new RegisterPushDeviceRequest { Token = token, Platform = "android" }, default);

        controller.ControllerContext = SignedInAs(second.Id);
        await controller.Register(new RegisterPushDeviceRequest { Token = token, Platform = "android" }, default);

        Assert.Equal(second.Id, Assert.Single(db.DeviceTokens).UserId);

        // The first person cannot remove a registration that is no longer theirs.
        controller.ControllerContext = SignedInAs(first.Id);
        await controller.Unregister(token, default);
        Assert.Single(db.DeviceTokens);

        controller.ControllerContext = SignedInAs(second.Id);
        await controller.Unregister(token, default);
        Assert.Empty(db.DeviceTokens);
    }

    private static ControllerContext SignedInAs(Guid userId) => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "test"))
        }
    };

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '='));
    }
}
