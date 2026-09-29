using System.Net;
using System.Net.Http.Json;

namespace RescueSriLanka.Api.Tests;

public class AuthorizationIntegrationTests
{
    [Fact]
    public async Task ProtectedEndpointReturns401WithoutAToken()
    {
        using var factory = new ComponentDApiFactory();
        using var client = factory.Client(role: null);
        var response = await client.GetAsync("/api/dispatches");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CoordinatorEndpointReturns403ForAuthenticatedUserWithoutCoordinatorRole()
    {
        using var factory = new ComponentDApiFactory();
        using var client = factory.Client("Responder");
        var response = await client.PostAsJsonAsync("/api/dispatches",
            new { assignmentId = Guid.NewGuid(), notes = (string?)null });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
