using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;

namespace RescueSriLanka.Api.Features.ComponentC.Agents.ResourceAllocationAgent;

public sealed class ResourceForecastAgent(
    AppDbContext dbContext,
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<ResourceForecastAgent> logger) : IResourceForecastAgent
{
    private const int ForecastWindowDays = 30;

    public async Task<ResourceStockForecast> ForecastAsync(CancellationToken cancellationToken = default)
    {
        var generatedAt = DateTime.UtcNow;
        var windowStart = generatedAt.AddDays(-ForecastWindowDays);
        var stocks = new List<StockRecord>();
        stocks.AddRange(await dbContext.MedicalSupplies.AsNoTracking()
            .Where(item => item.IsActive)
            .Select(item => new StockRecord(item.Id, "MedicalSupply", item.Name, item.Unit, item.QuantityOnHand, item.LowStockThreshold))
            .ToListAsync(cancellationToken));
        stocks.AddRange(await dbContext.FoodWaterStocks.AsNoTracking()
            .Where(item => item.IsActive)
            .Select(item => new StockRecord(item.Id, "FoodWaterStock", item.ItemName, item.Unit, item.QuantityOnHand, item.LowStockThreshold))
            .ToListAsync(cancellationToken));
        stocks.AddRange(await dbContext.ManagedSupplies.AsNoTracking()
            .Where(item => item.IsActive)
            .Select(item => new StockRecord(item.Id, "ManagedSupply", item.Name, item.Unit, item.QuantityOnHand, item.LowStockThreshold))
            .ToListAsync(cancellationToken));

        var allocations = await dbContext.ResourceAllocations.AsNoTracking()
            .Where(item => item.Status == "Active" && item.AllocatedAtUtc >= windowStart && item.AllocatedAtUtc <= generatedAt)
            .Select(item => new { item.ResourceType, item.ResourceId, item.Quantity })
            .ToListAsync(cancellationToken);
        var usageByStock = allocations
            .GroupBy(item => (ResourceType: NormalizeType(item.ResourceType), item.ResourceId))
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity));

        var items = stocks.Select(stock =>
        {
            var used = usageByStock.GetValueOrDefault((NormalizeType(stock.ResourceType), stock.ResourceId));
            var dailyUse = used / ForecastWindowDays;
            decimal? daysRemaining = dailyUse > 0
                ? decimal.Round(stock.QuantityOnHand / dailyUse, 1, MidpointRounding.AwayFromZero)
                : null;
            var atThreshold = stock.LowStockThreshold is decimal threshold && stock.QuantityOnHand <= threshold;
            var risk = atThreshold || stock.QuantityOnHand <= 0 || daysRemaining <= 7
                ? "Critical"
                : daysRemaining <= 30
                    ? "Watch"
                    : "Stable";
            var suggestedAction = risk switch
            {
                "Critical" => "Prioritize replenishment or reserve remaining stock for urgent requests.",
                "Watch" => "Monitor demand and plan a replenishment before stock runs low.",
                _ when dailyUse == 0 => "No recent allocations; continue monitoring demand.",
                _ => "Stock levels appear adequate at the recent usage rate."
            };

            return new ResourceStockForecastItem(
                stock.ResourceType,
                stock.ResourceId,
                stock.Name,
                stock.Unit,
                stock.QuantityOnHand,
                stock.LowStockThreshold,
                used,
                decimal.Round(dailyUse, 2, MidpointRounding.AwayFromZero),
                daysRemaining,
                risk,
                suggestedAction);
        }).ToList();

        var criticalCount = items.Count(item => item.RiskLevel == "Critical");
        var watchCount = items.Count(item => item.RiskLevel == "Watch");
        var summary = $"{criticalCount} stock item(s) at critical risk; {watchCount} need monitoring.";
        var forecast = new ResourceStockForecast(ForecastWindowDays, generatedAt, summary, items);
        var aiSummary = await TrySummarizeAsync(forecast, cancellationToken);
        return forecast with { Summary = aiSummary };
    }

    private async Task<string> TrySummarizeAsync(ResourceStockForecast forecast, CancellationToken cancellationToken)
    {
        var apiKey = configuration["Gemini:ApiKey"] ?? configuration["GoogleAi:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey)) return forecast.Summary;

        var configuredModel = configuration["Gemini:Model"] ?? configuration["GoogleAi:Model"] ?? "gemini-3-flash-preview";
        var models = new[] { configuredModel, "gemini-2.5-flash", "gemini-1.5-flash" }
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var prompt = $$"""
            Summarize these deterministic inventory forecasts for a resource manager in two concise sentences.
            Do not change or recalculate any quantities, risks, or recommendations. Do not approve allocations.
            Forecast data: {{JsonSerializer.Serialize(forecast)}}
            """;

        try
        {
            httpClient.BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/");
            httpClient.Timeout = TimeSpan.FromSeconds(30);
            foreach (var model in models)
            {
                using var response = await httpClient.PostAsJsonAsync(
                    $"models/{model}:generateContent?key={Uri.EscapeDataString(apiKey)}",
                    new { contents = new[] { new { parts = new[] { new { text = prompt } } } } },
                    cancellationToken);
                if ((int)response.StatusCode is 404 or 429 or >= 500) continue;
                if (!response.IsSuccessStatusCode) return forecast.Summary;

                using var document = await JsonDocument.ParseAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    cancellationToken: cancellationToken);
                var summary = document.RootElement.GetProperty("candidates")[0]
                    .GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
                return string.IsNullOrWhiteSpace(summary) ? forecast.Summary : summary.Trim();
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Gemini stock forecast summary failed; returning deterministic forecast.");
        }

        return forecast.Summary;
    }

    private static string NormalizeType(string resourceType) => resourceType.Trim().ToLowerInvariant();

    private sealed record StockRecord(
        Guid ResourceId,
        string ResourceType,
        string Name,
        string Unit,
        decimal QuantityOnHand,
        decimal? LowStockThreshold);
}