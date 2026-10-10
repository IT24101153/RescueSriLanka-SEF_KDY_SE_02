using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentB.Models;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Tests;

/// <summary>
/// Does a Medical help request reach the Help Request Manager the same way a
/// Food or Water one does?
///
/// These go through the real HTTP API: a citizen files the request, then the
/// manager lists it, verifies it and moves it through its lifecycle. The three
/// types run the identical script, so any step where Medical is treated
/// differently from Food or Water fails here.
/// </summary>
public class ComponentBMedicalFlowTests
{
    [Theory]
    [InlineData(HelpRequestType.Water)]
    [InlineData(HelpRequestType.Food)]
    [InlineData(HelpRequestType.Medical)]
    public async Task ACitizensRequest_ReachesTheManagerAndRunsItsWholeLifecycle(HelpRequestType type)
    {
        await using var api = new HelpRequestApiFactory();
        var citizenId = await api.SeedCitizen();
        var citizen = api.Client(UserRole.Citizen, citizenId);
        var manager = api.Client(UserRole.HelpRequestManager);

        // 1. The citizen files it.
        var filed = await citizen.PostAsJsonAsync("/api/helprequests", new
        {
            type = (int)type,
            description = $"{type} needed at the school hall",
            estimatedPeopleCount = 12,
            latitude = 6.9271,
            longitude = 79.8612
        });
        Assert.Equal(HttpStatusCode.Created, filed.StatusCode);
        var requestId = (await filed.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        // 2. It is in the manager's queue.
        var queue = await manager.GetFromJsonAsync<JsonElement>("/api/helprequests");
        var mine = queue.EnumerateArray()
            .Single(row => row.GetProperty("id").GetGuid() == requestId);
        Assert.Equal((int)type, mine.GetProperty("type").GetInt32());

        // 3. The manager verifies it as real.
        var verified = await manager.PatchAsJsonAsync(
            $"/api/helprequests/{requestId}/verify", new { isReal = true });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        Assert.Equal(
            (int)VerificationStatus.Verified,
            (await verified.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("verificationStatus").GetInt32());

        // 4. And drives it to Resolved by hand, which is the whole point: this is
        //    the manager's own queue, not the Rescue Coordinator's.
        foreach (var next in new[]
                 {
                     HelpRequestStatus.Assigned, HelpRequestStatus.InProgress, HelpRequestStatus.Resolved
                 })
        {
            var moved = await manager.PatchAsJsonAsync(
                $"/api/helprequests/{requestId}/status", new { newStatus = (int)next });
            Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        }

        await api.InDb(async db =>
        {
            var saved = await db.HelpRequests.FindAsync(requestId);
            Assert.Equal(HelpRequestStatus.Resolved, saved!.Status);
            Assert.Equal(type, saved.Type);
            Assert.Equal(3, await db.RequestStatusHistories.CountAsync(h => h.HelpRequestId == requestId));
        });
    }

    [Fact]
    public async Task OnlyRescueIsHandedToTheRescueCoordinator()
    {
        await using var api = new HelpRequestApiFactory();
        var citizenId = await api.SeedCitizen();
        var citizen = api.Client(UserRole.Citizen, citizenId);
        var manager = api.Client(UserRole.HelpRequestManager);

        var filed = await citizen.PostAsJsonAsync("/api/helprequests", new
        {
            type = (int)HelpRequestType.Rescue,
            description = "Trapped on the roof, water rising",
            estimatedPeopleCount = 3,
            latitude = 6.9271,
            longitude = 79.8612
        });
        var requestId = (await filed.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        await manager.PatchAsJsonAsync($"/api/helprequests/{requestId}/verify", new { isReal = true });
        var moved = await manager.PatchAsJsonAsync(
            $"/api/helprequests/{requestId}/status", new { newStatus = (int)HelpRequestStatus.Assigned });

        // Rescue is the one exception — Component D's dispatch drives its status.
        // Medical, Food and Water are not exceptions, which is what the theory above pins.
        Assert.Equal(HttpStatusCode.Conflict, moved.StatusCode);
    }

    [Fact]
    public async Task MedicalOutranksFoodAndWaterInTheQueue()
    {
        await using var api = new HelpRequestApiFactory();
        var citizenId = await api.SeedCitizen();
        var citizen = api.Client(UserRole.Citizen, citizenId);

        foreach (var type in new[] { HelpRequestType.Food, HelpRequestType.Water, HelpRequestType.Medical })
        {
            await citizen.PostAsJsonAsync("/api/helprequests", new
            {
                type = (int)type,
                description = $"{type} needed at the school hall",
                estimatedPeopleCount = 4,
                latitude = 6.9271,
                longitude = 79.8612
            });
        }

        var queue = await api.Client(UserRole.HelpRequestManager)
            .GetFromJsonAsync<JsonElement>("/api/helprequests");

        // Same queue, ordered by urgency — Medical scores 80, Water 40, Food 30 —
        // so it arrives at the top rather than somewhere else entirely.
        Assert.Equal(
            [(int)HelpRequestType.Medical, (int)HelpRequestType.Water, (int)HelpRequestType.Food],
            queue.EnumerateArray().Select(row => row.GetProperty("type").GetInt32()).ToArray());
    }

    /// <summary>The real API on an in-memory database, with a token per role.</summary>
    private sealed class HelpRequestApiFactory : WebApplicationFactory<Program>
    {
        private const string Issuer = "ComponentB.Tests";
        private const string Audience = "ComponentB.Test.Client";
        private const string Key = "component-b-local-test-signing-key-never-used-in-production";
        private readonly string _database = Guid.NewGuid().ToString();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Database:MigrateOnStartup", "false");
            builder.UseSetting("ComponentD:SeedDemoData", "false");
            builder.UseSetting("Email:Enabled", "false");
            builder.UseSetting("Jwt:Issuer", Issuer);
            builder.UseSetting("Jwt:Audience", Audience);
            builder.UseSetting("Jwt:Key", Key);
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<AppDbContext>();
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
                services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_database));
                // No background triage: urgency is then the service's own
                // deterministic score, not whatever a model later overwrote it with.
                services.RemoveAll<IHostedService>();
            });
        }

        public HttpClient Client(UserRole role, Guid? userId = null)
        {
            var client = CreateClient();
            var token = new JwtSecurityToken(Issuer, Audience,
                [new Claim(ClaimTypes.NameIdentifier, (userId ?? Guid.NewGuid()).ToString()),
                 new Claim(ClaimTypes.Role, role.ToString())],
                expires: DateTime.UtcNow.AddMinutes(10),
                signingCredentials: new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", new JwtSecurityTokenHandler().WriteToken(token));
            return client;
        }

        public async Task<Guid> SeedCitizen()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var citizen = new User
            {
                FullName = "Nimal Perera",
                Email = "nimal@example.lk",
                PasswordHash = "x",
                Role = UserRole.Citizen,
                PhoneNumber = "0712345678",
                District = "Colombo"
            };
            db.Users.Add(citizen);
            await db.SaveChangesAsync();
            return citizen.Id;
        }

        public async Task InDb(Func<AppDbContext, Task> action)
        {
            using var scope = Services.CreateScope();
            await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }
    }
}
