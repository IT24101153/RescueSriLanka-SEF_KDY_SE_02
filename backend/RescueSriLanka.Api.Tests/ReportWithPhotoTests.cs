using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentA.Services;
using RescueSriLanka.Api.Services.Storage;

namespace RescueSriLanka.Api.Tests;

/// <summary>
/// A report and its photo, filed in one request, over real HTTP.
///
/// The bug this guards against was one of ordering: the report was created,
/// the analysis agent was queued and started at once, and only then did the
/// photo arrive — so the agent graded every report without its picture. The
/// events list below records what happened in which order, so the test fails
/// if analysis is ever queued before the photo is stored again.
/// </summary>
public class ReportWithPhotoTests : IClassFixture<ReportWithPhotoTests.Factory>
{
    private readonly Factory _factory;

    public ReportWithPhotoTests(Factory factory)
    {
        _factory = factory;
        _factory.Events.Clear();
        _factory.Store.Fail = false;
    }

    [Fact]
    public async Task ThePhotoIsStoredBeforeTheAgentIsAskedToLook()
    {
        var response = await PostAsync(Jpeg());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(["stored", "analyse"], _factory.Events);

        var body = await response.Content.ReadFromJsonAsync<Response>();
        Assert.Null(body!.PhotoError);
        Assert.Equal("Flood", body.Incident.Type);
        Assert.Equal(6.9271, body.Incident.Latitude);
        Assert.Equal("https://cdn.test/photo.jpg", Assert.Single(body.Incident.Images).Url);
    }

    [Fact]
    public async Task AnUnacceptableFileIsRefusedBeforeAnyReportExists()
    {
        var response = await PostAsync(
            new ByteArrayContent("not an image"u8.ToArray())
            {
                Headers = { ContentType = new MediaTypeHeaderValue("text/plain") }
            },
            fileName: "notes.txt");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_factory.Events);
    }

    [Fact]
    public async Task AStorageFailureKeepsTheReportAndSaysSo()
    {
        _factory.Store.Fail = true;

        var response = await PostAsync(Jpeg());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Response>();
        Assert.Contains("Cloudinary upload failed", body!.PhotoError);
        Assert.Empty(body.Incident.Images);
        // The report is still analysed — from its text alone.
        Assert.Equal(["analyse"], _factory.Events);
    }

    // ------------------------------------------------------------ plumbing

    private static ByteArrayContent Jpeg() =>
        new([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10])
        {
            Headers = { ContentType = new MediaTypeHeaderValue("image/jpeg") }
        };

    /// <summary>The form exactly as the Flutter report screen builds it.</summary>
    private async Task<HttpResponseMessage> PostAsync(
        HttpContent photo, string fileName = "photo.jpg")
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent("Flash flooding on Galle Road"), "title" },
            { new StringContent("Water over the carriageway."), "description" },
            { new StringContent("Flood"), "type" },
            { new StringContent("6.9271"), "latitude" },
            { new StringContent("79.8612"), "longitude" },
            { new StringContent("1000"), "affectedRadiusMeters" },
            { new StringContent("Colombo"), "district" },
            { photo, "photo", fileName }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/incidents/with-photo")
        {
            Content = form
        };
        request.Headers.Authorization = new("Bearer", Factory.Token());

        return await _factory.CreateClient().SendAsync(request);
    }

    private record Response(IncidentBody Incident, string? PhotoError);

    private record IncidentBody(string Type, double Latitude, List<ImageBody> Images);

    private record ImageBody(string Url);

    /// <summary>Stands in for Cloudinary, and can be told to fail.</summary>
    public sealed class FakeStore(List<string> events) : IImageStore
    {
        public bool Fail { get; set; }

        public string Name => "fake";

        public Task<StoredImage> SaveAsync(
            Guid ownerId, string category, IFormFile file, CancellationToken ct = default)
        {
            if (Fail)
            {
                throw new InvalidOperationException("Cloudinary upload failed: test");
            }

            events.Add("stored");
            return Task.FromResult(new StoredImage("https://cdn.test/photo.jpg", "photo"));
        }

        public Task<byte[]?> ReadAsync(string location, CancellationToken ct = default) =>
            Task.FromResult<byte[]?>(null);
    }

    public sealed class RecordingAnalysisQueue(List<string> events) : IIncidentAnalysisQueue
    {
        public void Enqueue(Guid incidentId) => events.Add("analyse");
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        private const string Issuer = "RescueSriLanka.Test";
        private const string Audience = "RescueSriLanka.TestClients";
        private const string SigningKey = "test-signing-key-that-is-long-enough-for-hmac-sha256";

        private readonly string _database = $"report-photo-{Guid.NewGuid()}";

        public List<string> Events { get; } = [];

        public FakeStore Store { get; }

        public Factory() => Store = new FakeStore(Events);

        public static string Token()
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
            var token = new JwtSecurityToken(
                issuer: Issuer,
                audience: Audience,
                claims: [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                         new Claim(ClaimTypes.Role, "Citizen")],
                expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Jwt:Issuer", Issuer);
            builder.UseSetting("Jwt:Audience", Audience);
            builder.UseSetting("Jwt:Key", SigningKey);

            builder.ConfigureTestServices(services =>
            {
                // Postgres out, in-memory in — the options configuration has
                // to go too, or EF sees two providers.
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
                services.AddDbContext<AppDbContext>(options =>
                    options.UseInMemoryDatabase(_database));

                services.RemoveAll<IImageStore>();
                services.AddSingleton<IImageStore>(Store);

                services.RemoveAll<IIncidentAnalysisQueue>();
                services.AddSingleton<IIncidentAnalysisQueue>(new RecordingAnalysisQueue(Events));
            });
        }
    }
}
