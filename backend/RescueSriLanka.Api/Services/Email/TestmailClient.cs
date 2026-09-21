using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace RescueSriLanka.Api.Services.Email;

/// <summary>A message as testmail.app recorded it.</summary>
public record TestmailMessage
{
    public required string Id { get; init; }
    public required string Subject { get; init; }
    public required string From { get; init; }
    public required string To { get; init; }
    public required DateTime ReceivedAt { get; init; }
    public string? Text { get; init; }
}

public interface ITestmailClient
{
    bool IsConfigured { get; }

    /// <summary>
    /// Reads what actually arrived, newest first. This is the half of the loop
    /// SMTP cannot give us: proof that a warning was delivered, readable by a
    /// test or by whoever is running the demo.
    /// </summary>
    Task<IReadOnlyList<TestmailMessage>> FetchAsync(
        string tag, int limit = 10, CancellationToken ct = default);
}

/// <summary>
/// Client for the testmail.app JSON API.
///
/// testmail.app only receives, so this never sends anything — it queries the
/// namespace that <see cref="TestmailRedirectingEmailSender"/> delivers into.
/// </summary>
public class TestmailClient(
    HttpClient http,
    IOptions<EmailOptions> options,
    ILogger<TestmailClient> logger) : ITestmailClient
{
    private const string Endpoint = "https://api.testmail.app/api/json";

    private readonly TestmailOptions _options = options.Value.Testmail;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.Namespace) &&
        !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<IReadOnlyList<TestmailMessage>> FetchAsync(
        string tag, int limit = 10, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            logger.LogWarning("testmail.app is not configured; cannot read the inbox.");
            return [];
        }

        var url = $"{Endpoint}" +
                  $"?apikey={Uri.EscapeDataString(_options.ApiKey!)}" +
                  $"&namespace={Uri.EscapeDataString(_options.Namespace!)}" +
                  $"&tag={Uri.EscapeDataString(tag)}" +
                  $"&limit={Math.Clamp(limit, 1, 100)}" +
                  "&pretty=false";

        try
        {
            using var response = await http.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "testmail.app returned HTTP {Status} for tag {Tag}",
                    (int)response.StatusCode, tag);
                return [];
            }

            var payload = await response.Content.ReadFromJsonAsync<TestmailResponse>(
                cancellationToken: ct);

            if (payload?.Result != "success")
            {
                logger.LogWarning(
                    "testmail.app rejected the query: {Message}", payload?.Message ?? "no reason given");
                return [];
            }

            return (payload.Emails ?? [])
                .Select(email => new TestmailMessage
                {
                    Id = email.Id ?? string.Empty,
                    Subject = email.Subject ?? "(no subject)",
                    From = email.From ?? string.Empty,
                    To = email.To ?? string.Empty,
                    // testmail timestamps are milliseconds since the epoch.
                    ReceivedAt = DateTimeOffset.FromUnixTimeMilliseconds(email.Timestamp).UtcDateTime,
                    Text = email.Text
                })
                .ToList();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            logger.LogError(ex, "Could not read the testmail.app inbox for tag {Tag}", tag);
            return [];
        }
    }

    // ---- wire shapes, kept private: nothing outside this file should care ----

    private record TestmailResponse
    {
        [JsonPropertyName("result")] public string? Result { get; init; }
        [JsonPropertyName("message")] public string? Message { get; init; }
        [JsonPropertyName("emails")] public List<TestmailEmail>? Emails { get; init; }
    }

    private record TestmailEmail
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
        [JsonPropertyName("subject")] public string? Subject { get; init; }
        [JsonPropertyName("from")] public string? From { get; init; }
        [JsonPropertyName("to")] public string? To { get; init; }
        [JsonPropertyName("timestamp")] public long Timestamp { get; init; }
        [JsonPropertyName("text")] public string? Text { get; init; }
    }
}
