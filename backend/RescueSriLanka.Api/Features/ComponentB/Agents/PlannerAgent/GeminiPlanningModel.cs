using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using RescueSriLanka.Api.Services.Llm;

namespace RescueSriLanka.Api.Features.ComponentB.Agents.PlannerAgent;

/// <summary>
/// Google Gemini with function calling. It reads the same "GoogleAi" settings as
/// Component A's client: model, output limit, thinking budget and retry count. The
/// key goes in a header, never in the URL, so it stays out of request logs.
/// </summary>
public sealed class GeminiPlanningModel(HttpClient http, IConfiguration config) : IPlanningModel
{
    private const string DefaultModel = "gemini-3-flash-preview";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(config["GoogleAi:ApiKey"]);

    public async Task<PlanningTurn> NextTurnAsync(
        IReadOnlyList<object> contents, string instruction, CancellationToken ct = default)
    {
        var apiKey = config["GoogleAi:ApiKey"]
            ?? throw new InvalidOperationException("GoogleAi:ApiKey is not configured.");
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{ModelName}:generateContent";

        var body = new
        {
            systemInstruction = new { parts = new[] { new { text = instruction } } },
            contents,
            tools = new[] { new { functionDeclarations = AssessmentTools.Declarations } },
            generationConfig = new
            {
                temperature = 0.2,
                maxOutputTokens = MaxOutputTokens,
                thinkingConfig = new { thinkingBudget = ThinkingBudget }
            }
        };

        var attempts = Attempts;
        var maxWait = GeminiThrottle.MaxWait(config);
        for (var attempt = 1; ; attempt++)
        {
            // The key is shared with every other AI feature, so calls share their throttle too.
            using var response = await GeminiThrottle.SendAsync(http, () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
                request.Headers.Add("x-goog-api-key", apiKey);
                return request;
            }, ct);
            if (response.IsSuccessStatusCode)
            {
                return ParseTurn(await response.Content.ReadAsStringAsync(ct));
            }

            var decision = GeminiThrottle.Decide(
                response.StatusCode, await response.Content.ReadAsStringAsync(ct),
                response.Headers.RetryAfter?.Delta, attempt, attempts, maxWait);
            if (!decision.Retry)
            {
                throw new HttpRequestException(decision.Message);
            }

            await Task.Delay(decision.Delay, ct);
        }
    }

    private string ModelName => config["GoogleAi:Model"] ?? DefaultModel;

    // Thinking tokens count against this limit, so a small one can cut the JSON verdict off.
    private int MaxOutputTokens => int.TryParse(config["GoogleAi:MaxOutputTokens"], out var tokens)
        ? Math.Clamp(tokens, 512, 8192)
        : 2048;

    // This model rejects a budget of 0. Negative lets the model decide; anything else is floored at 128.
    private int ThinkingBudget
    {
        get
        {
            var requested = int.TryParse(config["GoogleAi:ThinkingBudget"], out var budget) ? budget : 128;
            return requested < 0 ? -1 : Math.Max(requested, 128);
        }
    }

    private int Attempts => int.TryParse(config["GoogleAi:MaxAttempts"], out var attempts)
        ? Math.Clamp(attempts, 1, 5)
        : 3;

    private static PlanningTurn ParseTurn(string json)
    {
        using var document = JsonDocument.Parse(json);
        var content = document.RootElement.GetProperty("candidates")[0].GetProperty("content");

        var calls = new List<PlannedToolCall>();
        var text = new StringBuilder();
        foreach (var part in content.GetProperty("parts").EnumerateArray())
        {
            if (part.TryGetProperty("functionCall", out var call))
            {
                var name = call.GetProperty("name").GetString() ?? string.Empty;
                var args = call.TryGetProperty("args", out var values)
                    ? values.Clone()
                    : JsonSerializer.SerializeToElement(new { });
                calls.Add(new PlannedToolCall(name, args));
            }
            else if (part.TryGetProperty("text", out var textPart))
            {
                text.Append(textPart.GetString());
            }
        }

        return new PlanningTurn(calls, text.Length == 0 ? null : text.ToString(), content.Clone());
    }
}
