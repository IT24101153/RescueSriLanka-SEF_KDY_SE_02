using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace RescueSriLanka.Api.Tests;

internal static class TestHostLogging
{
    public static IWebHostBuilder UseTestLogging(this IWebHostBuilder builder) =>
        builder.ConfigureLogging(logging =>
        {
            // Match the isolated Component D host's provider reset, retaining
            // console diagnostics here. Windows host defaults include EventLog,
            // which requires OS permissions these HTTP tests must not depend on.
            // Neither affected fixture asserts provider-specific log behavior.
            logging.ClearProviders();
            logging.AddConsole();
        });
}
