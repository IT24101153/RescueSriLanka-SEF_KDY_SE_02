using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace RescueSriLanka.Api.Services;

/// <summary>
/// The last line of defence for any exception a controller did not already
/// turn into a proper response (most do — see the try/catch blocks in
/// IncidentsController for the expected failure cases). Logs the full
/// exception server-side and returns a safe, uniform ProblemDetails body:
/// callers never see a stack trace or an internal exception message, which
/// could leak details of the database or file system.
/// </summary>
public class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IHostEnvironment environment) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        logger.LogError(
            exception,
            "Unhandled exception on {Method} {Path}",
            httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            // The exception's own message only goes to the client in
            // Development, where whoever is debugging it needs it and no
            // member of the public can be reading the response.
            Detail = environment.IsDevelopment() ? exception.Message : null,
            Instance = httpContext.Request.Path
        }, ct);

        return true;
    }
}
