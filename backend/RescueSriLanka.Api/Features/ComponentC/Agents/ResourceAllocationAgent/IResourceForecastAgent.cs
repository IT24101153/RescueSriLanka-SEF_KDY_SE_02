namespace RescueSriLanka.Api.Features.ComponentC.Agents.ResourceAllocationAgent;

public interface IResourceForecastAgent
{
    Task<ResourceStockForecast> ForecastAsync(CancellationToken cancellationToken = default);
}