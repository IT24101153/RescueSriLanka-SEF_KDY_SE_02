using System.Net;
using System.Text.Json;

namespace RescueSriLanka.Api.Services.Llm;

/// <summary>
/// What every Gemini caller in the API shares. They all use one key, and so one
/// quota: the incident agents, the help-request triage, the rescue explanations
/// and the resource recommendations. This keeps them from bursting through it
/// together, and turns a 429 into a decision instead of a blind retry.
/// </summary>
public static class GeminiThrottle
{
    // The free tier limits requests per minute as well as per day. A few calls in flight at
    // once is plenty for this app, and a burst from background triage is what trips the limit.
    private static readonly SemaphoreSlim Gate = new(2, 2);

    /// <summary>Longest a caller will wait out a 429 before telling the user instead.</summary>
    public static readonly TimeSpan DefaultMaxWait = TimeSpan.FromSeconds(15);

    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient http, Func<HttpRequestMessage> createRequest, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            using var request = createRequest();
            return await http.SendAsync(request, ct);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static TimeSpan MaxWait(IConfiguration configuration) =>
        double.TryParse(configuration["GoogleAi:MaxRetryWaitSeconds"], out var seconds)
            ? TimeSpan.FromSeconds(Math.Clamp(seconds, 0, 120))
            : DefaultMaxWait;

    /// <param name="Retry">True to wait <paramref name="Delay"/> and ask again.</param>
    /// <param name="Message">When not retrying: what to tell the person, free of provider jargon.</param>
    public sealed record Decision(bool Retry, TimeSpan Delay, string Message);

    public static Decision Decide(
        HttpStatusCode status, string body, TimeSpan? retryAfterHeader,
        int attempt, int attempts, TimeSpan maxWait)
    {
        var code = (int)status;
        var transient = code is 429 or 500 or 502 or 503 or 504;
        var failure = $"Google AI returned HTTP {code} after {attempt} attempt(s).";
        if (!transient) return new Decision(false, TimeSpan.Zero, failure);

        var backoff = TimeSpan.FromMilliseconds(600 * Math.Pow(2, attempt - 1));
        if (code != 429)
        {
            return attempt >= attempts
                ? new Decision(false, TimeSpan.Zero, failure)
                : new Decision(true, backoff, string.Empty);
        }

        // A daily quota does not come back in seconds, so waiting only holds the user up.
        if (IsDailyQuota(body))
        {
            return new Decision(false, TimeSpan.Zero,
                "Google AI's daily quota for this key is used up, so AI recommendations are paused until it resets. " +
                "Review requests manually, or use a key with more quota.");
        }

        // Google says how long to wait; an answer far longer than a person will sit through is reported, not slept on.
        var delay = ParseRetryDelay(body) ?? retryAfterHeader ?? backoff;
        if (delay > maxWait || attempt >= attempts)
        {
            var seconds = (int)Math.Ceiling(Math.Max(delay.TotalSeconds, 1));
            return new Decision(false, TimeSpan.Zero,
                $"Google AI is rate limiting requests right now (HTTP 429). Try again in about {seconds} seconds.");
        }

        return new Decision(true, delay, string.Empty);
    }

    // The 429 body lists which quota ran out; per-day ones carry "PerDay" in their id.
    public static bool IsDailyQuota(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            foreach (var detail in Details(document.RootElement))
            {
                if (!detail.TryGetProperty("violations", out var violations)) continue;
                foreach (var violation in violations.EnumerateArray())
                {
                    if (violation.TryGetProperty("quotaId", out var id) &&
                        id.GetString()?.Contains("PerDay", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        return true;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON: treat it as an ordinary rate limit.
        }

        return false;
    }

    // "retryDelay": "37s" (or "0.5s") in the RetryInfo detail.
    public static TimeSpan? ParseRetryDelay(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            foreach (var detail in Details(document.RootElement))
            {
                if (detail.TryGetProperty("retryDelay", out var value) &&
                    value.GetString() is { } text &&
                    text.EndsWith('s') &&
                    double.TryParse(text[..^1], System.Globalization.CultureInfo.InvariantCulture, out var seconds))
                {
                    return TimeSpan.FromSeconds(seconds);
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON: no hint to read.
        }

        return null;
    }

    private static IEnumerable<JsonElement> Details(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("error", out var error) ||
            error.ValueKind != JsonValueKind.Object ||
            !error.TryGetProperty("details", out var details) ||
            details.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return details.EnumerateArray();
    }
}
