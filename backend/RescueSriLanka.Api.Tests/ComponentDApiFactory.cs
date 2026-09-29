using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
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
using RescueSriLanka.Api.Features.ComponentA.Models;
using RescueSriLanka.Api.Features.ComponentD.Agents.SafetyValidation;
using RescueSriLanka.Api.Features.ComponentD.Data;
using RescueSriLanka.Api.Features.ComponentD.DTOs;
using RescueSriLanka.Api.Features.ComponentD.Models;

namespace RescueSriLanka.Api.Tests;

public sealed class ComponentDApiFactory : WebApplicationFactory<Program>
{
    private const string Issuer = "ComponentD.Isolated.Tests";
    private const string Audience = "ComponentD.Test.Client";
    private const string Key = "component-d-local-test-signing-key-never-used-in-production";
    public const string CoordinatorId = "d0000000-0000-4000-8000-000000000099";
    private readonly string _database = Guid.NewGuid().ToString();
    public DeterministicGeminiClient Gemini { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("ComponentD:SeedDemoData", "false");
        builder.UseSetting("Jwt:Issuer", Issuer);
        builder.UseSetting("Jwt:Audience", Audience);
        builder.UseSetting("Jwt:Key", Key);
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureTestServices(services =>
        {
            ReplaceDatabase<AppDbContext>(services, _database + "-shared");
            ReplaceDatabase<ComponentDDbContext>(services, _database + "-d");
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IGeminiSafetyValidationClient>();
            services.AddSingleton<IGeminiSafetyValidationClient>(Gemini);
            // Fail closed if an unexpected service attempts any external HTTP request.
            services.ConfigureHttpClientDefaults(client =>
                client.ConfigurePrimaryHttpMessageHandler(() => new RejectExternalRequests()));
        });
    }

    private static void ReplaceDatabase<T>(IServiceCollection services, string name) where T : DbContext
    {
        services.RemoveAll<T>();
        services.RemoveAll<DbContextOptions<T>>();
        services.RemoveAll<IDbContextOptionsConfiguration<T>>();
        services.AddDbContext<T>(options => options.UseInMemoryDatabase(name));
    }

    public HttpClient Client(string? role = "EmergencyCoordinator")
    {
        var client = CreateClient();
        if (role is not null)
        {
            var token = new JwtSecurityToken(Issuer, Audience,
                [new Claim(ClaimTypes.NameIdentifier, CoordinatorId), new Claim(ClaimTypes.Role, role)],
                expires: DateTime.UtcNow.AddMinutes(10),
                signingCredentials: new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        }
        return client;
    }

    public async Task<T> InDb<T>(Func<ComponentDDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComponentDDbContext>();
        Assert.Equal("Microsoft.EntityFrameworkCore.InMemory", db.Database.ProviderName);
        return await action(db);
    }

    public async Task<Fixture> Seed(bool assignment = false, bool dispatch = false)
    {
        using var scope = Services.CreateScope();
        var shared = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var db = scope.ServiceProvider.GetRequiredService<ComponentDDbContext>();
        Assert.Equal("Microsoft.EntityFrameworkCore.InMemory", shared.Database.ProviderName);
        Assert.Equal("Microsoft.EntityFrameworkCore.InMemory", db.Database.ProviderName);
        var incident = new Incident { Title = "Isolated fixture", Description = "No external operations" };
        shared.Incidents.Add(incident);
        await shared.SaveChangesAsync();
        var team = new RescueTeam { Name = "Fixture Rescue", Status = TeamStatus.Available };
        team.Members.Add(new TeamMember { FullName = "Fixture Medic", Phone = "0712345678", Skill = SkillType.FirstAid });
        var vehicle = new Vehicle { RescueTeamId = team.Id, PlateNumber = "TEST-01", Type = VehicleType.Ambulance, Capacity = 4 };
        db.AddRange(team, vehicle);
        var proposal = new Assignment { IncidentId = incident.Id, RescueTeamId = team.Id, VehicleId = vehicle.Id,
            RequiredSkill = SkillType.FirstAid, RequiredCapacity = 1 };
        if (assignment || dispatch) db.Add(proposal);
        Dispatch? response = null;
        if (dispatch)
        {
            proposal.Status = AssignmentStatus.Approved;
            team.Status = TeamStatus.OnMission;
            vehicle.Status = VehicleStatus.InUse;
            response = new Dispatch { AssignmentId = proposal.Id, Status = DispatchStatus.Dispatched,
                ApprovalStatus = ApprovalStatus.Approved, ApprovedByUserId = CoordinatorId,
                ApprovedAt = DateTime.UtcNow, DispatchedAt = DateTime.UtcNow };
            db.Add(response);
        }
        await db.SaveChangesAsync();
        return new(team.Id, vehicle.Id, incident.Id, proposal.Id, response?.Id);
    }

    public sealed record Fixture(Guid TeamId, Guid VehicleId, Guid IncidentId, Guid AssignmentId, Guid? DispatchId)
    {
        public CreateAssignmentDto Proposal(int capacity = 1) =>
            new(IncidentId, null, TeamId, VehicleId, SkillType.FirstAid, capacity, null);
    }

    public sealed class DeterministicGeminiClient : IGeminiSafetyValidationClient
    {
        public int Calls { get; private set; }
        public Task<GeminiSafetyAgentResponse> GetNextResponseAsync(AssignmentValidationContextDto context,
            IReadOnlyList<object> priorToolResults, string? previousInteractionId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new GeminiSafetyAgentResponse([], SafetyValidationDecision.APPROVE,
                "Isolated provider recommendation; real mandatory checks still apply.", []));
        }
    }

    private sealed class RejectExternalRequests : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("External HTTP is prohibited in Component D API tests.");
    }
}
