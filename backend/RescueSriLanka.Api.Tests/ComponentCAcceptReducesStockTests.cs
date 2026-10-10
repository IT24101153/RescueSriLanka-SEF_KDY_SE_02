using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
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
using RescueSriLanka.Api.Features.ComponentC.Models;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Services.Llm;

namespace RescueSriLanka.Api.Tests;

/// <summary>
/// The question that matters for Component C: when a resource manager accepts a
/// request, does the inventory actually go down?
///
/// These go through the real HTTP API, in the exact order the dashboard calls
/// it, and then read the stock row back out of the database. Nothing here is
/// mocked except the database and the language model, so a break anywhere in
/// the controller, the service or the agent shows up as a failure.
/// </summary>
public class ComponentCAcceptReducesStockTests
{
    [Fact]
    public async Task AcceptingARequest_ReducesTheStockItAsksFor()
    {
        await using var api = new ResourceApiFactory();
        var (requestId, supplyId) = await api.SeedRiceRequest(onHand: 50);
        var client = api.Client();

        // 1. Accept — what the dashboard's Accept button does first.
        var accepted = await client.PatchAsJsonAsync(
            $"/api/resources/help-requests/{requestId}/status", new { status = "Accepted" });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        // 2. Ask what to send, 3. send it — the rest of the same click.
        var sent = await api.SendWhatTheAgentRecommends(client, requestId);
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);

        // The point of the whole exercise: 50 kg on hand, 20 kg asked for, 30 left.
        Assert.Equal(30, await api.OnHand(supplyId));

        await api.InDb(async db =>
        {
            Assert.Equal("Fulfilled", (await db.ResourceHelpRequests.FindAsync(requestId))!.Status);
            var allocation = Assert.Single(await db.ResourceAllocations.ToListAsync());
            Assert.Equal(20, allocation.Quantity);
            Assert.Equal(supplyId, allocation.ResourceId);
            Assert.Equal("ManagedSupply", allocation.ResourceType);
            Assert.Equal(requestId, allocation.HelpRequestId);
        });
    }

    [Fact]
    public async Task AcceptingStillReducesStock_WhenTheModelAnswersWithNonsense()
    {
        // A configured model that names a resource that does not exist. Before the
        // rules fallback covered this, the recommendation came back NoMatch and
        // the manager's click deducted nothing at all.
        await using var api = new ResourceApiFactory(
            llm: new StubLlm("""
                {"decision":"Recommend","resourceId":"ffffffff-ffff-4fff-8fff-ffffffffffff",
                 "resourceType":"ManagedSupply","quantity":20,"confidence":0.9,
                 "reason":"Matches.","warnings":[],"requiresApproval":true}
                """));
        var (requestId, supplyId) = await api.SeedRiceRequest(onHand: 50);
        var client = api.Client();

        await client.PatchAsJsonAsync(
            $"/api/resources/help-requests/{requestId}/status", new { status = "Accepted" });
        var sent = await api.SendWhatTheAgentRecommends(client, requestId);

        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
        Assert.Equal(30, await api.OnHand(supplyId));
    }

    [Fact]
    public async Task AcceptingDeductsNothing_WhenTheRequestStatesNoAmount()
    {
        // The honest limit: with no amount to read, any number would be invented,
        // so the agent declines and the stock is left alone.
        await using var api = new ResourceApiFactory();
        var (requestId, supplyId) = await api.SeedRiceRequest(onHand: 50, description: "we need rice urgently");
        var client = api.Client();

        await client.PatchAsJsonAsync(
            $"/api/resources/help-requests/{requestId}/status", new { status = "Accepted" });
        var advice = await api.Recommend(client, requestId);

        Assert.Equal("NoMatch", advice.GetProperty("decision").GetString());
        Assert.Equal(50, await api.OnHand(supplyId));
        await api.InDb(async db =>
            Assert.Equal("Accepted", (await db.ResourceHelpRequests.FindAsync(requestId))!.Status));
    }

    [Fact]
    public async Task SendingTwiceDoesNotDeductTwice()
    {
        await using var api = new ResourceApiFactory();
        var (requestId, supplyId) = await api.SeedRiceRequest(onHand: 50);
        var client = api.Client();

        await client.PatchAsJsonAsync(
            $"/api/resources/help-requests/{requestId}/status", new { status = "Accepted" });
        await api.SendWhatTheAgentRecommends(client, requestId);
        Assert.Equal(30, await api.OnHand(supplyId));

        // A double click, or a retry on a row whose first send did get through.
        var again = await client.PostAsJsonAsync("/api/resources/allocations", new
        {
            resourceType = "ManagedSupply",
            resourceId = supplyId,
            quantity = 20m,
            helpRequestId = requestId,
            incidentId = (Guid?)null
        });

        // Refused, and the shelf is untouched — 10 kg must not quietly become the
        // new figure because the button was pressed twice.
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(30, await api.OnHand(supplyId));
        await api.InDb(async db =>
            Assert.Single(await db.ResourceAllocations.ToListAsync()));
    }

    private sealed class StubLlm(string response) : ILlmClient
    {
        public bool IsConfigured => true;
        public string ModelName => "stub";

        public Task<string> GenerateAsync(
            string systemInstruction, string prompt, object? jsonSchema = null,
            IReadOnlyList<LlmImage>? images = null, CancellationToken ct = default) =>
            Task.FromResult(response);
    }

    /// <summary>The real API, on an in-memory database, with the model under test control.</summary>
    private sealed class ResourceApiFactory(ILlmClient? llm = null) : WebApplicationFactory<Program>
    {
        private const string Issuer = "ComponentC.Tests";
        private const string Audience = "ComponentC.Test.Client";
        private const string Key = "component-c-local-test-signing-key-never-used-in-production";
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
                services.RemoveAll<IHostedService>();

                // Unconfigured by default, so the deterministic rules decide.
                services.RemoveAll<ILlmClient>();
                services.AddSingleton(llm ?? new UnconfiguredLlm());
            });
        }

        public HttpClient Client()
        {
            var client = CreateClient();
            var token = new JwtSecurityToken(Issuer, Audience,
                [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                 new Claim(ClaimTypes.Role, nameof(UserRole.ResourceManager))],
                expires: DateTime.UtcNow.AddMinutes(10),
                signingCredentials: new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", new JwtSecurityTokenHandler().WriteToken(token));
            return client;
        }

        /// <summary>20 kg of rice wanted, and rice on the shelf to send.</summary>
        public async Task<(Guid RequestId, Guid SupplyId)> SeedRiceRequest(
            decimal onHand, string description = "Rice - 20 kg")
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var supply = new ManagedSupply
            {
                Id = Guid.NewGuid(), Category = "Food", Name = "Rice", Unit = "kg",
                QuantityOnHand = onHand, LowStockThreshold = 5
            };
            var request = new HelpRequest
            {
                Id = Guid.NewGuid(), RequesterName = "Nimal Perera", ContactNumber = "0712345678",
                NeedType = "Food", Description = description
            };
            db.ManagedSupplies.Add(supply);
            db.ResourceHelpRequests.Add(request);
            await db.SaveChangesAsync();
            return (request.Id, supply.Id);
        }

        public async Task<System.Text.Json.JsonElement> Recommend(HttpClient client, Guid requestId)
        {
            var response = await client.PostAsync(
                $"/api/resources/help-requests/{requestId}/allocation-recommendation", null);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        }

        /// <summary>Asks the agent what to send, then sends exactly that — one click's worth.</summary>
        public async Task<HttpResponseMessage> SendWhatTheAgentRecommends(HttpClient client, Guid requestId)
        {
            var advice = await Recommend(client, requestId);
            Assert.Equal("Recommend", advice.GetProperty("decision").GetString());

            return await client.PostAsJsonAsync("/api/resources/allocations", new
            {
                resourceType = advice.GetProperty("resourceType").GetString(),
                resourceId = advice.GetProperty("resourceId").GetGuid(),
                quantity = advice.GetProperty("quantity").GetDecimal(),
                helpRequestId = requestId,
                incidentId = (Guid?)null
            });
        }

        public Task<decimal> OnHand(Guid supplyId) =>
            InDb(async db => (await db.ManagedSupplies.FindAsync(supplyId))!.QuantityOnHand);

        public async Task<T> InDb<T>(Func<AppDbContext, Task<T>> action)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal("Microsoft.EntityFrameworkCore.InMemory", db.Database.ProviderName);
            return await action(db);
        }

        public async Task InDb(Func<AppDbContext, Task> action)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await action(db);
        }

        private sealed class UnconfiguredLlm : ILlmClient
        {
            public bool IsConfigured => false;
            public string ModelName => "unconfigured";

            public Task<string> GenerateAsync(
                string systemInstruction, string prompt, object? jsonSchema = null,
                IReadOnlyList<LlmImage>? images = null, CancellationToken ct = default) =>
                throw new LlmUnavailableException("Not configured.");
        }
    }
}
