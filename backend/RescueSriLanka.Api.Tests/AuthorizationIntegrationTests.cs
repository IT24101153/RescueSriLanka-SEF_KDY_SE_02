using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace RescueSriLanka.Api.Tests
{
    public class AuthorizationIntegrationTests : IClassFixture<AuthorizationIntegrationTests.RescueApiFactory>
    {
        private readonly HttpClient _client;

        public AuthorizationIntegrationTests(RescueApiFactory factory)
        {
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task ProtectedEndpointReturns401WithoutAToken()
        {
            var response = await _client.GetAsync("/api/dispatches");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task CoordinatorEndpointReturns403ForAuthenticatedUserWithoutCoordinatorRole()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/dispatches")
            {
                Content = JsonContent.Create(new { assignmentId = Guid.NewGuid(), notes = (string?)null })
            };
            request.Headers.Authorization = new("Bearer", CreateToken("Responder"));

            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        private static string CreateToken(string role)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(RescueApiFactory.SigningKey));
            var token = new JwtSecurityToken(
                issuer: RescueApiFactory.Issuer,
                audience: RescueApiFactory.Audience,
                claims: new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, "test-user"),
                    new Claim(ClaimTypes.Role, role)
                },
                expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public sealed class RescueApiFactory : WebApplicationFactory<Program>
        {
            public const string Issuer = "RescueSriLanka.Test";
            public const string Audience = "RescueSriLanka.TestClients";
            public const string SigningKey = "test-signing-key-that-is-long-enough-for-hmac-sha256";

            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("Jwt:Issuer", Issuer);
                builder.UseSetting("Jwt:Audience", Audience);
                builder.UseSetting("Jwt:Key", SigningKey);
            }
        }
    }
}
