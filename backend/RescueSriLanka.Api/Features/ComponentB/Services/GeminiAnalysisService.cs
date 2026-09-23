using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace RescueSriLanka.Api.Features.ComponentB.Services
{
    public class AiAnalysisResult
    {
        public string Reasoning { get; set; } = string.Empty;
        public string CredibilitySignal { get; set; } = string.Empty; // e.g. "Looks genuine" / "Possibly vague or inconsistent"
        public string SuggestedAction { get; set; } = string.Empty;
    }

    public interface IAiAnalysisService
    {
        Task<AiAnalysisResult?> AnalyzeHelpRequestAsync(string type, string description, int urgencyScore);
    }

    // Calls Google Gemini's free-tier API to produce a human-readable severity
    // analysis and credibility signal for a citizen's help request description.
    // Source: leader's direction — Gemini free tier, replacing the originally
    // proposed Ollama for the no-cost LLM requirement.
    public class GeminiAnalysisService(HttpClient http, IConfiguration config) : IAiAnalysisService
    {
        private static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

        private readonly HttpClient _http = http;
        private readonly IConfiguration _config = config;

        public async Task<AiAnalysisResult?> AnalyzeHelpRequestAsync(string type, string description, int urgencyScore)
        {
            var apiKey = _config["GoogleAi:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                // No key configured — caller should treat this as "AI analysis unavailable"
                // rather than crash the whole request.
                return null;
            }

            var model = _config["GoogleAi:Model"] ?? "gemini-1.5-flash";
            var maxOutputTokens = _config.GetValue<int?>("GoogleAi:MaxOutputTokens") ?? 512;

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

            var prompt = $$"""
                You are assisting an emergency response coordinator in Sri Lanka.
                A citizen submitted this help request:

                Type: {{type}}
                Rule-based urgency score (0-100): {{urgencyScore}}
                Description: "{{description}}"

                Respond with ONLY a JSON object, no markdown, no extra text, in exactly this shape:
                {
                  "reasoning": "<one or two sentences explaining the likely severity of this situation>",
                  "credibilitySignal": "<one short phrase: e.g. 'Looks genuine' or 'Vague — recommend manual verification' or 'Possibly inconsistent details'>",
                  "suggestedAction": "<one short phrase recommending what kind of response is needed>"
                }
                """;

            var requestBody = new
            {
                contents = new[]
                {
                    new { parts = new[] { new { text = prompt } } }
                },
                generationConfig = new
                {
                    maxOutputTokens
                }
            };

            try
            {
                var response = await _http.PostAsJsonAsync(url, requestBody);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadFromJsonAsync<JsonElement>();
                var text = json
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();

                if (string.IsNullOrWhiteSpace(text)) return null;

                // Gemini sometimes wraps JSON in ```json fences despite instructions — strip if present.
                var cleaned = text.Trim().Trim('`').Replace("json", "", StringComparison.OrdinalIgnoreCase).Trim();

                var parsed = JsonSerializer.Deserialize<AiAnalysisResult>(cleaned, CaseInsensitive);

                return parsed;
            }
            catch
            {
                // Fail safe: AI analysis is a helpful add-on, not a hard dependency —
                // if Gemini is unreachable or returns something unexpected, the rest
                // of the workflow (urgency score, verification, dispatch) still works.
                return null;
            }
        }
    }
}