namespace RescueSriLanka.Api.Features.ComponentC.Agents.ResourceAllocationAgent;

public interface IResourceAllocationAgent
{
    Task<ResourceAllocationRecommendation> RecommendAsync(
        Guid helpRequestId,
        CancellationToken cancellationToken = default);
}