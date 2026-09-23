// Gemini 3 Flash Preview backed version of the Dispatch Recommendation
// Agent, using Google's Interactions API — the same bounded think/act/
// observe loop as the Ollama version, just talking to a different
// provider's wire format. Swap this in for OllamaDispatchRecommendationAgent
// in Program.cs's DI registration; AgentOrchestrator needs no changes
// since both implement IDispatchRecommendationAgent.
//
// IMPORTANT — read before relying on this in a demo:
// - This is a cloud API requiring GEMINI_API_KEY and network access —
//   NOT "self-hosted" / "no-cost" as the original proposal specified.
//   Confirm with your group/lecturer this substitution is sanctioned,
//   and update the ADR to reflect and justify it.
// - Verified against Gemini's documented Interactions API as of this
//   writing (multiple independent doc sources agreed on the shape) —
//   but this is a young, actively-changing API ("breaking changes"
//   migration guides already exist for it), so test against the real
//   endpoint before trusting this in a live demo.
// - Uses the documented "Api-Revision" header to pin behavior, per
//   Google's own recommendation, reducing the risk of a silent
//   breaking change affecting you mid-project.

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace RescueSriLanka.Api.Features.ComponentD.Agents.Orchestration
{
    public class GeminiDispatchRecommendationAgent : IDispatchRecommendationAgent
    {
        private const int MaxSteps = 6;
        private static readonly TimeSpan OverallTimeBudget = TimeSpan.FromSeconds(45);
        private const string ApiRevision = "2026-05-20"; // pin to avoid unannounced breaking changes

        private readonly HttpClient _httpClient;
        private readonly DispatchAgentTools _tools;
        private readonly ILogger<GeminiDispatchRecommendationAgent> _logger;
        private readonly string _model;
        private readonly string _apiKey;

        public GeminiDispatchRecommendationAgent(
            HttpClient httpClient,
            DispatchAgentTools tools,
            IConfiguration configuration,
            ILogger<GeminiDispatchRecommendationAgent> logger)
        {
            _httpClient = httpClient;
            _tools = tools;
            _logger = logger;

            _httpClient.BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);

            _model = configuration["Gemini:Model"] ?? configuration["GoogleAi:Model"] ?? "gemini-3-flash-preview";
            _apiKey = configuration["Gemini:ApiKey"]
            ?? configuration["GoogleAi:ApiKey"]
                ?? throw new InvalidOperationException(
                    "Gemini:ApiKey is not configured. Set it in appsettings.Development.json " +
                    "(gitignored — never commit a real key).");
        }

        public async Task<DispatchRecommendationResult> RecommendAsync(string requiredSkill)
        {
            var log = new List<string>();
            var stopwatch = Stopwatch.StartNew();

            const string systemInstruction =
                "You recommend which rescue team to dispatch. Use the search_teams and " +
                "get_team_details tools to investigate candidates. When you have decided, " +
                "respond with ONLY this JSON and no other text, and call no more tools: " +
                "{\"rescueTeamId\": \"<guid>\", \"reasoning\": \"<one short sentence>\"}. " +
                "If no suitable team exists, respond with " +
                "{\"rescueTeamId\": null, \"reasoning\": \"<why>\"}.";

            object input = $"Find the best rescue team for required skill: {requiredSkill}";
            string? previousInteractionId = null;

            for (int step = 1; step <= MaxSteps; step++)
            {
                if (stopwatch.Elapsed > OverallTimeBudget)
                {
                    log.Add($"Step {step}: time budget exceeded ({OverallTimeBudget.TotalSeconds}s) — failing loudly.");
                    return new DispatchRecommendationResult(false, null, "Agent exceeded its time budget.", log);
                }

                GeminiInteractionResponse? response;
                try
                {
                    response = await CallGeminiWithOneRetryAsync(
                        input, previousInteractionId, systemInstruction, log, step);
                }
                catch (Exception ex)
                {
                    log.Add($"Step {step}: Gemini call failed after retry — {ex.Message}");
                    return new DispatchRecommendationResult(false, null, "Gemini API unreachable or timed out.", log);
                }

                if (response is null)
                {
                    log.Add($"Step {step}: empty response from Gemini — failing loudly.");
                    return new DispatchRecommendationResult(false, null, "Gemini returned an empty response.", log);
                }

                previousInteractionId = response.Id;

                var functionCalls = response.Steps?.Where(s => s.Type == "function_call").ToList() ?? new();
                var modelOutput = response.Steps?.LastOrDefault(s => s.Type == "model_output");

                if (functionCalls.Count == 0)
                {
                    var finalText = modelOutput?.Content?.FirstOrDefault(c => c.Type == "text")?.Text;
                    log.Add($"Step {step}: THINK — model produced a final answer, no further tool calls.");
                    return ParseFinalAnswer(finalText, log);
                }

                log.Add($"Step {step}: ACT — model requested {functionCalls.Count} tool call(s).");

                var functionResults = new List<object>();
                foreach (var call in functionCalls)
                {
                    var toolName = call.Name ?? "";
                    log.Add($"Step {step}: ACT — calling tool '{toolName}'.");

                    string? toolResult;
                    try
                    {
                        toolResult = await _tools.ExecuteAsync(toolName, call.Arguments ?? default);
                    }
                    catch (Exception ex)
                    {
                        toolResult = JsonSerializer.Serialize(new { error = ex.Message });
                    }

                    if (toolResult is null)
                    {
                        log.Add($"Step {step}: '{toolName}' is not an allow-listed tool — refusing.");
                        toolResult = JsonSerializer.Serialize(new { error = "Tool not allowed." });
                    }
                    else
                    {
                        log.Add($"Step {step}: OBSERVE — tool result received ({toolResult.Length} chars).");
                    }

                    functionResults.Add(new
                    {
                        type = "function_result",
                        name = toolName,
                        call_id = call.Id,
                        result = new[] { new { type = "text", text = toolResult } }
                    });
                }

                // Next turn's input is the batch of function results;
                // previous_interaction_id carries the conversation state
                // server-side, so we don't resend history ourselves.
                input = functionResults;
            }

            log.Add($"Step cap ({MaxSteps}) reached without a final answer — failing loudly.");
            return new DispatchRecommendationResult(false, null, $"Exceeded the {MaxSteps}-step limit without a decision.", log);
        }

        private async Task<GeminiInteractionResponse?> CallGeminiWithOneRetryAsync(
            object input, string? previousInteractionId, string systemInstruction, List<string> log, int step)
        {
            var requestBody = new Dictionary<string, object?>
            {
                ["model"] = _model,
                ["input"] = input,
                ["tools"] = _tools.GetGeminiToolSchemas(),
                ["system_instruction"] = systemInstruction,
            };
            if (previousInteractionId is not null)
                requestBody["previous_interaction_id"] = previousInteractionId;

            var json = JsonSerializer.Serialize(requestBody);

            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, "interactions")
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json")
                    };
                    request.Headers.Add("x-goog-api-key", _apiKey);
                    request.Headers.Add("Api-Revision", ApiRevision);

                    var httpResponse = await _httpClient.SendAsync(request);
                    httpResponse.EnsureSuccessStatusCode();

                    var raw = await httpResponse.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<GeminiInteractionResponse>(raw,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch (Exception ex) when (attempt == 1)
                {
                    log.Add($"Step {step}: attempt 1 failed ({ex.Message}), retrying once...");
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
            }

            using var finalRequest = new HttpRequestMessage(HttpMethod.Post, "interactions")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            finalRequest.Headers.Add("x-goog-api-key", _apiKey);
            finalRequest.Headers.Add("Api-Revision", ApiRevision);

            var finalResponse = await _httpClient.SendAsync(finalRequest);
            finalResponse.EnsureSuccessStatusCode();
            var finalRaw = await finalResponse.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<GeminiInteractionResponse>(finalRaw,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        private static DispatchRecommendationResult ParseFinalAnswer(string? content, List<string> log)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                log.Add("Final answer was empty — failing loudly.");
                return new DispatchRecommendationResult(false, null, "Model gave an empty final answer.", log);
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<FinalAnswerJson>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (parsed is null)
                {
                    log.Add("Could not parse final answer JSON — failing loudly.");
                    return new DispatchRecommendationResult(false, null, "Could not parse the model's final answer.", log);
                }

                if (parsed.RescueTeamId is null || !Guid.TryParse(parsed.RescueTeamId, out var teamId))
                {
                    return new DispatchRecommendationResult(false, null, parsed.Reasoning ?? "No suitable team found.", log);
                }

                return new DispatchRecommendationResult(true, teamId, parsed.Reasoning ?? "", log);
            }
            catch (JsonException ex)
            {
                log.Add($"Final answer was not valid JSON — {ex.Message}. Failing loudly.");
                return new DispatchRecommendationResult(false, null, "Model's final answer was not valid JSON.", log);
            }
        }

        // --- Gemini Interactions API wire shapes (flat steps schema) ---
        // VERIFY against a real call — this is a young, evolving API.
        private class GeminiInteractionResponse
        {
            public string? Id { get; set; }
            public List<GeminiStep>? Steps { get; set; }
        }

        private class GeminiStep
        {
            public string? Type { get; set; } // "function_call" | "model_output" | ...
            public string? Id { get; set; }    // call id, used as call_id in the result
            public string? Name { get; set; }  // present on function_call steps
            public JsonElement? Arguments { get; set; } // present on function_call steps
            public List<GeminiContentBlock>? Content { get; set; } // present on model_output steps
        }

        private class GeminiContentBlock
        {
            public string? Type { get; set; }
            public string? Text { get; set; }
        }

        private class FinalAnswerJson
        {
            public string? RescueTeamId { get; set; }
            public string? Reasoning { get; set; }
        }
    }
}
