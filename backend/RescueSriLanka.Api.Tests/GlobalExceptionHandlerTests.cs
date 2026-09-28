using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using RescueSriLanka.Api.Services;

namespace RescueSriLanka.Api.Tests;

/// <summary>
/// The last-resort handler for any exception a controller did not already
/// turn into a response. The one thing that must always hold: whatever the
/// exception message says, it never reaches a caller outside Development —
/// that message can contain a connection string, a file path or other detail
/// about the server that section 5 of the spec asks not to leak.
/// </summary>
public class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_ReturnsA500_AndHidesTheExceptionMessage_OutsideDevelopment()
    {
        var handler = new GlobalExceptionHandler(
            NullLogger<GlobalExceptionHandler>.Instance,
            new FakeHostEnvironment(Environments.Production));

        var context = NewContext(out var body);

        var handled = await handler.TryHandleAsync(
            context, new InvalidOperationException("db connection string leaked here"), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);

        var json = await ReadBodyAsync(body);
        Assert.Contains("An unexpected error occurred.", json);
        Assert.DoesNotContain("db connection string leaked here", json);
    }

    [Fact]
    public async Task TryHandleAsync_IncludesTheExceptionMessage_InDevelopmentOnly()
    {
        var handler = new GlobalExceptionHandler(
            NullLogger<GlobalExceptionHandler>.Instance,
            new FakeHostEnvironment(Environments.Development));

        var context = NewContext(out var body);

        await handler.TryHandleAsync(
            context, new InvalidOperationException("boom"), CancellationToken.None);

        var json = await ReadBodyAsync(body);
        Assert.Contains("boom", json);
    }

    [Fact]
    public async Task TryHandleAsync_NeverThrows_EvenForAnExceptionWithNoMessage()
    {
        var handler = new GlobalExceptionHandler(
            NullLogger<GlobalExceptionHandler>.Instance,
            new FakeHostEnvironment(Environments.Production));

        var context = NewContext(out _);

        // A safe failure must itself be safe: this must not throw.
        var handled = await handler.TryHandleAsync(
            context, new NullReferenceException(), CancellationToken.None);

        Assert.True(handled);
    }

    private static HttpContext NewContext(out MemoryStream body)
    {
        body = new MemoryStream();
        var context = new DefaultHttpContext
        {
            Response = { Body = body }
        };
        context.Request.Method = "GET";
        context.Request.Path = "/api/incidents";
        return context;
    }

    private static async Task<string> ReadBodyAsync(MemoryStream body)
    {
        body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(body);
        return await reader.ReadToEndAsync();
    }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "RescueSriLanka.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
