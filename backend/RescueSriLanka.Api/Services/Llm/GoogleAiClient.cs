using System.Net.Http.Json;
using System.Text.Json;

namespace RescueSriLanka.Api.Services.Llm;

/// <summary>
/// Google AI Studio (Gemini) client.
///
/// Configuration — never hard-code the key:
///   "GoogleAi": { "ApiKey": "...", "Model": "gemini-2.0-flash" }
/// In deployment supply it as the environment variable GoogleAi__ApiKey.
/// </summary>
public class GoogleAiClient(
    HttpClient http,
    IConfiguration configuration,
    ILogger<GoogleAiClient> logger) : ILlmClient
{
    private const string DefaultModel = "gemini-3-flash-preview";

    private string? ApiKey => configuration["GoogleAi:ApiKey"];

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    public string ModelName => configuration["GoogleAi:Model"] ?? DefaultModel;

    public async Task<string> GenerateAsync(
        string systemInstruction,
        string prompt,
        object? jsonSchema = null,
        IReadOnlyList<LlmImage>? images = null,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            throw new LlmUnavailableException(
                "GoogleAi:ApiKey is not configured. Add it to appsettings.Development.json.");
        }

        // Free-tier tuning. The output is a small JSON object with a one or two
        // sentence rationale, so a tight token cap costs nothing and keeps well
        // inside quota. Thinking is held to a small budget: this is a bounded
        // classification task, and a large budget mostly buys latency.
        // Thinking tokens count against maxOutputTokens — a trivial prompt can
        // burn 300+ before a single output token — so this needs real headroom
        // or the JSON comes back truncated.
        var maxTokens = int.TryParse(configuration["GoogleAi:MaxOutputTokens"], out var tokens)
            ? Math.Clamp(tokens, 512, 8192)
            : 2048;

        // gemini-3.6-flash REJECTS a budget of 0 with a bare INVALID_ARGUMENT —
        // thinking cannot be switched off on this model. Negative means "let the
        // model decide"; anything else is floored at the smallest budget it accepts.
        var requestedBudget = int.TryParse(configuration["GoogleAi:ThinkingBudget"], out var budget)
            ? budget
            : 128;
        var thinkingBudget = requestedBudget < 0 ? -1 : Math.Max(requestedBudget, 128);

        object generationConfig = jsonSchema is null
            ? new
            {
                temperature = 0.2,
                maxOutputTokens = maxTokens,
                thinkingConfig = new { thinkingBudget }
            }
            : new
            {
                temperature = 0.2,
                maxOutputTokens = maxTokens,
                thinkingConfig = new { thinkingBudget },
                responseMimeType = "application/json",
                responseSchema = jsonSchema
            };

        // Text first, then any photos as inline base64 — Gemini is multimodal,
        // so the same call does the vision step.
        var parts = new List<object> { new { text = prompt } };

        foreach (var image in images ?? [])
        {
            parts.Add(new
            {
                inlineData = new
                {
                    mimeType = image.MimeType,
                    data = Convert.ToBase64String(image.Data)
                }
            });
        }

        var body = new
        {
            systemInstruction = new { parts = new[] { new { text = systemInstruction } } },
            contents = new[] { new { role = "user", parts = parts.ToArray() } },
            generationConfig
        };

        var url =
            $"https://generativelanguage.googleapis.com/v1beta/models/{ModelName}:generateContent";

        // Free-tier Gemini returns 503/429 under load. These are transient, so
        // retry with backoff before falling back to the rule engine — a spike
        // in demand should not change an incident's severity assessment.
        var attempts = int.TryParse(configuration["GoogleAi:MaxAttempts"], out var configured)
            ? Math.Clamp(configured, 1, 5)
            : 3;

        HttpResponseMessage? response = null;
        string? lastDetail = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(body)
            };
            // Header rather than a query string, so the key never lands in logs.
            request.Headers.Add("x-goog-api-key", ApiKey);

            try
            {
                response = await http.SendAsync(request, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new LlmUnavailableException("Could not reach Google AI Studio.", ex);
            }

            if (response.IsSuccessStatusCode) break;

            lastDetail = await response.Content.ReadAsStringAsync(ct);
            var transient = (int)response.StatusCode is 429 or 500 or 502 or 503 or 504;

            if (!transient || attempt == attempts)
            {
                logger.LogWarning(
                    "Google AI returned {Status}: {Detail}", (int)response.StatusCode, lastDetail);
                throw new LlmUnavailableException(
                    $"Google AI returned HTTP {(int)response.StatusCode} after {attempt} attempt(s).");
            }

            var delay = TimeSpan.FromMilliseconds(600 * Math.Pow(2, attempt - 1));
            logger.LogInformation(
                "Google AI {Status} (attempt {Attempt}/{Total}); retrying in {Delay}ms.",
                (int)response.StatusCode, attempt, attempts, delay.TotalMilliseconds);
            await Task.Delay(delay, ct);
        }

        if (response is null || !response.IsSuccessStatusCode)
        {
            throw new LlmUnavailableException("Google AI did not return a usable response.");
        }

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        if (!payload.TryGetProperty("candidates", out var candidates) ||
            candidates.GetArrayLength() == 0)
        {
            throw new LlmUnavailableException("Google AI returned no candidates.");
        }

        var candidate = candidates[0];

        // Anything but STOP means the answer is incomplete; fail loudly so the
        // rule engine takes over instead of parsing half a JSON object.
        if (candidate.TryGetProperty("finishReason", out var finish) &&
            finish.GetString() is string reason &&
            !string.Equals(reason, "STOP", StringComparison.OrdinalIgnoreCase))
        {
            throw new LlmUnavailableException($"Google AI stopped early: {reason}.");
        }

        var text = candidate
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        return text ?? throw new LlmUnavailableException("Google AI returned an empty response.");
    }
}
