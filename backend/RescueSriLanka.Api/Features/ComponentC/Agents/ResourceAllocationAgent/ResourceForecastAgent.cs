using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Services.Llm;

namespace RescueSriLanka.Api.Features.ComponentC.Agents.ResourceAllocationAgent;

public sealed class ResourceForecastAgent(
    AppDbContext dbContext,
    ILlmClient llm,
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

    private const string SummarySystemInstruction =
        "You summarize deterministic inventory forecasts for an emergency resource " +
        "manager in plain prose. Do not change or recalculate any quantities, risks, " +
        "or recommendations, and do not approve allocations.";

    private async Task<string> TrySummarizeAsync(ResourceStockForecast forecast, CancellationToken cancellationToken)
    {
        if (!llm.IsConfigured) return forecast.Summary;

        var prompt = $$"""
            Summarize this forecast for a resource manager in two concise sentences.
            Forecast data: {{JsonSerializer.Serialize(forecast)}}
            """;

        try
        {
            var summary = await llm.GenerateAsync(SummarySystemInstruction, prompt, ct: cancellationToken);
            return string.IsNullOrWhiteSpace(summary) ? forecast.Summary : summary.Trim();
        }
        catch (LlmUnavailableException exception)
        {
            logger.LogWarning(exception, "Gemini stock forecast summary failed; returning deterministic forecast.");
            return forecast.Summary;
        }
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