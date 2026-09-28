using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentC.Models;

namespace RescueSriLanka.Api.Features.ComponentC.Agents.ResourceAllocationAgent;

public sealed class ResourceAllocationAgent(
    AppDbContext dbContext,
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<ResourceAllocationAgent> logger) : IResourceAllocationAgent
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ResourceAllocationRecommendation> RecommendAsync(
        Guid helpRequestId,
        CancellationToken cancellationToken = default)
    {
        var request = await dbContext.ResourceHelpRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == helpRequestId, cancellationToken);
        if (request is null)
        {
            return NoMatch("The resource request was not found.", "Request was not found.");
        }

        var candidates = await GetCandidatesAsync(cancellationToken);
        if (candidates.Count == 0)
        {
            return NoMatch("There are no active resources with available stock.", "No active stock is available.");
        }

        var apiKey = configuration["Gemini:ApiKey"] ?? configuration["GoogleAi:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return NoMatch("Gemini is not configured, so manager review is required.", "Gemini API key is not configured.");
        }

        var configuredModel = configuration["Gemini:Model"] ?? configuration["GoogleAi:Model"] ?? "gemini-3-flash-preview";
        var models = new[] { configuredModel, "gemini-2.5-flash", "gemini-1.5-flash" }
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var attemptedModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? lastUnavailableResponse = null;
        var prompt = $$"""
            You are a resource allocation assistant for an emergency response manager.
            Recommend an available resource for the request. Never invent an ID, resource,
            quantity, or fact. The manager must approve your recommendation.

            Request:
            {{JsonSerializer.Serialize(new ResourceAllocationPrompt(request.Id, request.NeedType, request.Description, candidates), JsonOptions)}}

            Respond with only this JSON shape:
            {
              "decision": "Recommend" or "NoMatch",
              "resourceId": "exact candidate UUID or null",
              "resourceType": "exact candidate resource type or null",
              "quantity": 0,
              "confidence": 0.0,
              "reason": "short explanation",
              "warnings": ["short warning"],
              "requiresApproval": true
            }
            """;

        try
        {
            httpClient.BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/");
            httpClient.Timeout = TimeSpan.FromSeconds(30);
            var discoveredModels = new List<string>();
            foreach (var model in models)
            {
                attemptedModels.Add(model);
                var attempt = await TryModelAsync(model, apiKey, prompt, cancellationToken);
                if (attempt.IsUnavailable)
                {
                    lastUnavailableResponse = attempt.Error;
                    logger.LogWarning("Gemini allocation recommendation unavailable for {Model}: {ProviderError}", model, attempt.Error);
                    continue;
                }
                if (attempt.Error is not null)
                {
                    logger.LogWarning("Gemini allocation recommendation failed for {Model}: {ProviderError}", model, attempt.Error);
                    return NoMatch("Gemini rejected the recommendation request; review it manually.", attempt.Error);
                }

                return ValidateRecommendation(attempt.Recommendation, candidates);
            }

            discoveredModels = await DiscoverGenerationModelsAsync(apiKey, cancellationToken);
            foreach (var model in discoveredModels.Where(model => !attemptedModels.Contains(model)))
            {
                attemptedModels.Add(model);
                var attempt = await TryModelAsync(model, apiKey, prompt, cancellationToken);
                if (attempt.IsUnavailable)
                {
                    lastUnavailableResponse = attempt.Error;
                    logger.LogWarning("Gemini allocation recommendation unavailable for discovered model {Model}: {ProviderError}", model, attempt.Error);
                    continue;
                }
                if (attempt.Error is not null)
                {
                    logger.LogWarning("Gemini allocation recommendation failed for discovered model {Model}: {ProviderError}", model, attempt.Error);
                    return NoMatch("Gemini rejected the recommendation request; review it manually.", attempt.Error);
                }

                return ValidateRecommendation(attempt.Recommendation, candidates);
            }

            var warning = lastUnavailableResponse is null
                ? "No accessible Gemini model supports generateContent. Check the API key and enabled API."
                : $"Gemini models were unavailable ({lastUnavailableResponse}). Check API key model access and quota.";
            return NoMatch("Gemini could not reach an available model; review the request manually.", warning);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            logger.LogWarning(exception, "Gemini allocation recommendation failed for request {RequestId}.", helpRequestId);
            return NoMatch("The AI recommendation could not be completed; review the request manually.", "AI recommendation failed.");
        }
    }

    private async Task<ModelAttempt> TryModelAsync(
        string model,
        string apiKey,
        string prompt,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            $"models/{model}:generateContent?key={Uri.EscapeDataString(apiKey)}",
            new
            {
                contents = new[] { new { parts = new[] { new { text = prompt } } } },
                generationConfig = new { temperature = 0.1, maxOutputTokens = 512 }
            },
            cancellationToken);
        if ((int)response.StatusCode is 404 or 429 or >= 500)
        {
            var providerError = await ReadProviderErrorAsync(response, cancellationToken);
            return new ModelAttempt(null, true, providerError);
        }
        if (!response.IsSuccessStatusCode)
        {
            return new ModelAttempt(null, false, $"Gemini returned HTTP {(int)response.StatusCode} ({response.StatusCode}).");
        }

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        var text = document.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();
        var recommendation = JsonSerializer.Deserialize<ResourceAllocationRecommendation>(CleanJson(text), JsonOptions);
        return new ModelAttempt(recommendation, false, null);
    }

    private async Task<List<string>> DiscoverGenerationModelsAsync(string apiKey, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                $"models?key={Uri.EscapeDataString(apiKey)}",
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Gemini model discovery returned HTTP {StatusCode}.", response.StatusCode);
                return [];
            }

            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("models", out var modelsElement) || modelsElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return modelsElement.EnumerateArray()
                .Where(model => model.TryGetProperty("supportedGenerationMethods", out var methods) &&
                    methods.ValueKind == JsonValueKind.Array &&
                    methods.EnumerateArray().Any(method => method.GetString() == "generateContent"))
                .Select(model => model.TryGetProperty("name", out var name) ? name.GetString() : null)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!.StartsWith("models/", StringComparison.Ordinal) ? name["models/".Length..] : name)
                .Where(name => name.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(name => name.Contains("flash", StringComparison.OrdinalIgnoreCase))
                .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToList();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(exception, "Gemini model discovery failed.");
            return [];
        }
    }

    private static async Task<string> ReadProviderErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        string? message = null;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var messageElement))
            {
                message = messageElement.GetString();
            }
        }
        catch (JsonException)
        {
            // The HTTP status remains useful when the provider body is not JSON.
        }

        var detail = string.IsNullOrWhiteSpace(message) ? string.Empty : $": {message}";
        if (detail.Length > 220) detail = $"{detail[..220]}...";
        return $"HTTP {(int)response.StatusCode} ({response.StatusCode}){detail}";
    }

    private async Task<List<ResourceAllocationCandidate>> GetCandidatesAsync(CancellationToken cancellationToken)
    {
        var medical = await dbContext.MedicalSupplies.AsNoTracking()
            .Where(item => item.IsActive && item.QuantityOnHand > 0)
            .Select(item => new ResourceAllocationCandidate(item.Id, "MedicalSupply", item.Name, "Medical", item.Unit, item.QuantityOnHand))
            .ToListAsync(cancellationToken);
        var foodWater = await dbContext.FoodWaterStocks.AsNoTracking()
            .Where(item => item.IsActive && item.QuantityOnHand > 0)
            .Select(item => new ResourceAllocationCandidate(item.Id, "FoodWaterStock", item.ItemName, "Food or water", item.Unit, item.QuantityOnHand))
            .ToListAsync(cancellationToken);
        var managed = await dbContext.ManagedSupplies.AsNoTracking()
            .Where(item => item.IsActive && item.QuantityOnHand > 0)
            .Select(item => new ResourceAllocationCandidate(item.Id, "ManagedSupply", item.Name, item.Category, item.Unit, item.QuantityOnHand))
            .ToListAsync(cancellationToken);
        return [.. medical, .. foodWater, .. managed];
    }

    private static ResourceAllocationRecommendation ValidateRecommendation(
        ResourceAllocationRecommendation? recommendation,
        IReadOnlyList<ResourceAllocationCandidate> candidates)
    {
        if (recommendation is null || !string.Equals(recommendation.Decision, "Recommend", StringComparison.OrdinalIgnoreCase))
        {
            return NoMatch(recommendation?.Reason ?? "No suitable resource was found.", "No safe match was recommended.");
        }

        var candidate = candidates.SingleOrDefault(item => item.Id == recommendation.ResourceId);
        if (candidate is null || recommendation.Quantity <= 0 || recommendation.Quantity > candidate.QuantityOnHand)
        {
            return NoMatch("The recommendation failed deterministic stock validation.", "The AI returned an invalid resource or quantity.");
        }

        return recommendation with
        {
            ResourceType = candidate.ResourceType,
            ResourceName = candidate.Name,
            Unit = candidate.Unit,
            AvailableQuantity = candidate.QuantityOnHand,
            Confidence = Math.Clamp(recommendation.Confidence, 0, 1),
            RequiresApproval = true,
            Warnings = recommendation.Warnings ?? []
        };
    }

    private static ResourceAllocationRecommendation NoMatch(string reason, string warning) =>
        new("NoMatch", null, null, 0, 0, reason, [warning], true);

    private static string CleanJson(string? text)
    {
        var cleaned = (text ?? string.Empty).Trim();
        if (cleaned.StartsWith("```", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("```json", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("```", string.Empty, StringComparison.Ordinal)
                .Trim();
        }
        return cleaned;
    }

    private sealed record ModelAttempt(
        ResourceAllocationRecommendation? Recommendation,
        bool IsUnavailable,
        string? Error);
}