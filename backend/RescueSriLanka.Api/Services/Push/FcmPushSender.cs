using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace RescueSriLanka.Api.Services.Push;

/// <summary>
/// Sends through Firebase Cloud Messaging's HTTP v1 API, one request per device.
/// </summary>
public sealed class FcmPushSender(
    IHttpClientFactory httpClientFactory,
    IFcmAccessTokens accessTokens,
    IOptions<PushOptions> options,
    ILogger<FcmPushSender> logger) : IPushSender
{
    public string Name => "Firebase Cloud Messaging";

    public async Task<PushOutcome> SendAsync(string token, PushMessage message, CancellationToken ct = default)
    {
        var projectId = options.Value.Fcm.ProjectId;

        try
        {
            var bearer = await accessTokens.GetAsync(ct);

            using var request = new HttpRequestMessage(
                HttpMethod.Post, $"https://fcm.googleapis.com/v1/projects/{projectId}/messages:send")
            {
                Headers = { Authorization = new AuthenticationHeaderValue("Bearer", bearer) },
                Content = JsonContent.Create(new
                {
                    message = new
                    {
                        token,
                        notification = new { title = message.Title, body = message.Body },
                        android = new { priority = "high" }
                    }
                })
            };

            using var response = await httpClientFactory
                .CreateClient(nameof(FcmPushSender))
                .SendAsync(request, ct);

            if (response.IsSuccessStatusCode) return PushOutcome.Sent;

            var body = await response.Content.ReadAsStringAsync(ct);
            if (IsDeadToken(response.StatusCode, body)) return PushOutcome.TokenInvalid;

            logger.LogWarning("Firebase refused a push with {Status}: {Body}", (int)response.StatusCode, body);
            return PushOutcome.Failed;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Push to Firebase failed.");
            return PushOutcome.Failed;
        }
    }

    /// <summary>
    /// Firebase answers 404 UNREGISTERED when the app was uninstalled or the
    /// token has expired, and 400 when the token itself is malformed. Anything
    /// else is treated as a fault in our request, and no device is dropped for it.
    /// </summary>
    public static bool IsDeadToken(HttpStatusCode status, string body) =>
        (status == HttpStatusCode.NotFound && body.Contains("UNREGISTERED", StringComparison.Ordinal)) ||
        (status == HttpStatusCode.BadRequest && body.Contains("registration token", StringComparison.OrdinalIgnoreCase));
}
