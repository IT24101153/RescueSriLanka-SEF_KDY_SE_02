// Gemini 3 Flash Preview backed version of the Incident Analysis Agent —
// no tool-calling loop needed here (severity classification is a single
// structured-output call), so this is simpler than the recommendation
// agent. Swap in for OllamaIncidentAnalysisAgent in Program.cs.
//
// See the header comment in GeminiDispatchRecommendationAgent.cs for
// the same caveats about this being a young API and a cloud/paid
// dependency — they apply here too.

using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace RescueSriLanka.Api.Features.ComponentD.Agents.Orchestration
{
    public class GeminiIncidentAnalysisAgent : IIncidentAnalysisAgent
    {
        private const string ApiRevision = "2026-05-20";

        private readonly HttpClient _httpClient;
        private readonly ILogger<GeminiIncidentAnalysisAgent> _logger;
        private readonly string _model;
        private readonly string _apiKey;

        public GeminiIncidentAnalysisAgent(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<GeminiIncidentAnalysisAgent> logger)
        {
            _httpClient = httpClient;
            _logger = logger;

            _httpClient.BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);

            _model = configuration["Gemini:Model"] ?? "gemini-3-flash-preview";
            _apiKey = configuration["Gemini:ApiKey"]
                ?? throw new InvalidOperationException(
                    "Gemini:ApiKey is not configured. Set it in appsettings.Development.json.");
        }

        public async Task<IncidentAnalysisResult> AnalyzeAsync(Guid objectiveId)
        {
            // NOTE: Student A's real version should pass the incident's
            // actual text/image/location data here instead of just the
            // id — this stays minimal since Component D has no access to
            // Incident data.
            var requestBody = new
            {
                model = _model,
                input = $"Classify the severity of incident {objectiveId} for a disaster-response system.",
                response_format = new
                {
                    type = "text",
                    mime_type = "application/json",
                    schema = new
                    {
                        type = "object",
                        properties = new
                        {
                            severity = new { type = "string", @enum = new[] { "Low", "Moderate", "High", "Critical" } },
                            notes = new { type = "string" }
                        },
                        required = new[] { "severity", "notes" }
                    }
                }
            };

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "interactions")
                {
                    Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
                };
                request.Headers.Add("x-goog-api-key", _apiKey);
                request.Headers.Add("Api-Revision", ApiRevision);

                var httpResponse = await _httpClient.SendAsync(request);
                httpResponse.EnsureSuccessStatusCode();

                var raw = await httpResponse.Content.ReadAsStringAsync();
                var parsed = JsonSerializer.Deserialize<GeminiInteractionResponse>(raw,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                var text = parsed?.Steps?
                    .LastOrDefault(s => s.Type == "model_output")?
                    .Content?.FirstOrDefault(c => c.Type == "text")?.Text;

                if (string.IsNullOrWhiteSpace(text))
                {
                    _logger.LogWarning("Gemini returned no text output for objective {ObjectiveId}", objectiveId);
                    return Degraded("Gemini returned an empty response.");
                }

                var result = JsonSerializer.Deserialize<SeverityJson>(text,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result is null || string.IsNullOrWhiteSpace(result.Severity))
                {
                    _logger.LogWarning("Could not parse Gemini's severity JSON for objective {ObjectiveId}: {Raw}", objectiveId, text);
                    return Degraded("Could not parse the model's severity output.");
                }

                return new IncidentAnalysisResult(result.Severity, result.Notes ?? "");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Gemini API unreachable for objective {ObjectiveId}", objectiveId);
                return Degraded("Gemini API unreachable — check network and API key.");
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning("Gemini call timed out for objective {ObjectiveId}", objectiveId);
                return Degraded("Gemini call timed out.");
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse Gemini response for objective {ObjectiveId}", objectiveId);
                return Degraded("Failed to parse Gemini's response.");
            }
        }

        private static IncidentAnalysisResult Degraded(string reason) =>
            new("Unclassified", $"Degraded result — {reason}");

        private class GeminiInteractionResponse { public List<GeminiStep>? Steps { get; set; } }
        private class GeminiStep { public string? Type { get; set; } public List<GeminiContentBlock>? Content { get; set; } }
        private class GeminiContentBlock { public string? Type { get; set; } public string? Text { get; set; } }
        private class SeverityJson { public string? Severity { get; set; } public string? Notes { get; set; } }
    }
}
