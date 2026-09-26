using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;
using RescueSriLanka.Api.Features.ComponentB.Models;

namespace RescueSriLanka.Api.Tests;

public sealed class ComponentBAuthorizationIntegrationTests : IClassFixture<ComponentBAuthorizationIntegrationTests.ApiFactory>
{
    private readonly HttpClient _client;

    public ComponentBAuthorizationIntegrationTests(ApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task PlannerTrigger_RequiresAuthentication()
    {
        var response = await _client.PostAsJsonAsync("/api/agentworkflows/trigger", new
        {
            objectiveType = (int)PlannerWorkflowObjectiveType.Incident,
            objectiveId = Guid.NewGuid()
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PlannerTrigger_RequiresCoordinatorRole()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/agentworkflows/trigger")
        {
            Content = JsonContent.Create(new { objectiveType = (int)PlannerWorkflowObjectiveType.Incident, objectiveId = Guid.NewGuid() })
        };
        request.Headers.Authorization = new("Bearer", CreateToken("Citizen"));

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CoordinatorReachesControllerAndGetsObjectiveValidation()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/agentworkflows/trigger")
        {
            Content = JsonContent.Create(new { objectiveType = (int)PlannerWorkflowObjectiveType.Incident, objectiveId = Guid.NewGuid() })
        };
        request.Headers.Authorization = new("Bearer", CreateToken("EmergencyCoordinator"));

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static string CreateToken(string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiFactory.SigningKey));
        var token = new JwtSecurityToken(
            issuer: ApiFactory.Issuer,
            audience: ApiFactory.Audience,
            claims: [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, role)],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public sealed class ApiFactory : WebApplicationFactory<Program>
    {
        public const string Issuer = "RescueSriLanka.ComponentB.Tests";
        public const string Audience = "RescueSriLanka.ComponentB.TestClients";
        public const string SigningKey = "component-b-test-signing-key-long-enough-for-hmac-sha256";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Database:MigrateOnStartup", "false");
            builder.UseSetting("Jwt:Issuer", Issuer);
            builder.UseSetting("Jwt:Audience", Audience);
            builder.UseSetting("Jwt:Key", SigningKey);
        }
    }
}
