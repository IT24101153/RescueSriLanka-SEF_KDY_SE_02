namespace RescueSriLanka.Api.Features.ComponentC.Agents.ResourceAllocationAgent;

public record ResourceAllocationRecommendation(
    string Decision,
    Guid? ResourceId,
    string? ResourceType,
    decimal Quantity,
    decimal Confidence,
    string Reason,
    IReadOnlyList<string> Warnings,
    bool RequiresApproval);

public record ResourceAllocationCandidate(
    Guid Id,
    string ResourceType,
    string Name,
    string Category,
    string Unit,
    decimal QuantityOnHand);

internal record ResourceAllocationPrompt(
    Guid RequestId,
    string NeedType,
    string Description,
    IReadOnlyList<ResourceAllocationCandidate> AvailableResources);