namespace RescueSriLanka.Api.Features.ComponentC.Agents.ResourceAllocationAgent;

public record ResourceStockForecast(
    int WindowDays,
    DateTime GeneratedAtUtc,
    string Summary,
    IReadOnlyList<ResourceStockForecastItem> Items);

public record ResourceStockForecastItem(
    string ResourceType,
    Guid ResourceId,
    string Name,
    string Unit,
    decimal QuantityOnHand,
    decimal? LowStockThreshold,
    decimal UsedInLast30Days,
    decimal AverageDailyUse,
    decimal? EstimatedDaysRemaining,
    string RiskLevel,
    string SuggestedAction);