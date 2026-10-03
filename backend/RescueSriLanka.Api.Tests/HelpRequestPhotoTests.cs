using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace RescueSriLanka.Api.Tests;

/// <summary>
/// A help request's photo, uploaded through the API. The API hands back the
/// stored URL for the request to carry, and keeps no copy of its own. Shares the
/// photo test host, whose store is a fake that records what it was given.
/// </summary>
public class HelpRequestPhotoTests : IClassFixture<ReportWithPhotoTests.Factory>
{
    private readonly ReportWithPhotoTests.Factory _factory;

    public HelpRequestPhotoTests(ReportWithPhotoTests.Factory factory)
    {
        _factory = factory;
        _factory.Events.Clear();
        _factory.Store.Fail = false;
    }

    [Fact]
    public async Task ACitizensPhotoIsStoredAndItsUrlIsReturned()
    {
        var response = await PostAsync(Jpeg());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Body>();
        Assert.Equal("https://cdn.test/photo.jpg", body!.Url);
        Assert.Equal(["stored"], _factory.Events);
    }

    [Fact]
    public async Task AFileThatIsNotAnImageIsRefusedBeforeAnythingIsStored()
    {
        var response = await PostAsync(new ByteArrayContent("not an image"u8.ToArray())
        {
            Headers = { ContentType = new MediaTypeHeaderValue("image/jpeg") }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_factory.Events);
    }

    [Fact]
    public async Task AStoreFailureIsReportedRatherThanReturningABrokenUrl()
    {
        _factory.Store.Fail = true;

        var response = await PostAsync(Jpeg());

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Empty(_factory.Events);
    }

    private static ByteArrayContent Jpeg() =>
        new([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10])
        {
            Headers = { ContentType = new MediaTypeHeaderValue("image/jpeg") }
        };

    private async Task<HttpResponseMessage> PostAsync(HttpContent photo)
    {
        using var form = new MultipartFormDataContent { { photo, "photo", "photo.jpg" } };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/HelpRequests/photo")
        {
            Content = form
        };
        request.Headers.Authorization = new("Bearer", ReportWithPhotoTests.Factory.Token());

        return await _factory.CreateClient().SendAsync(request);
    }

    private record Body(string Url);
}
