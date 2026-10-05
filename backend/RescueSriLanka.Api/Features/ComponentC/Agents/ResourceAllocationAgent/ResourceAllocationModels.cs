namespace RescueSriLanka.Api.Features.ComponentC.Agents.ResourceAllocationAgent;

public record ResourceAllocationRecommendation(
    string Decision,
    Guid? ResourceId,
    string? ResourceType,
    decimal Quantity,
    decimal Confidence,
    string Reason,
    IReadOnlyList<string> Warnings,
    bool RequiresApproval,
    string? ResourceName = null,
    string? Unit = null,
    decimal? AvailableQuantity = null,
    // "AI" when the model decided, "Rules" when the built-in rules did because the AI was unavailable.
    string Source = "AI");

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

public record ResourceAllocationPlan(
    DateTime GeneratedAtUtc,
    string Summary,
    IReadOnlyList<ResourceAllocationPlanItem> Items);

public record ResourceAllocationPlanItem(
    Guid HelpRequestId,
    string NeedType,
    string RequesterName,
    int Priority,
    string Decision,
    Guid? ResourceId,
    string? ResourceType,
    string? ResourceName,
    string? Unit,
    decimal Quantity,
    decimal? AvailableQuantity,
    decimal Confidence,
    string Reason,
    IReadOnlyList<string> Warnings,
    bool RequiresApproval,
    string Source = "AI");

internal record PendingRequestInfo(
    Guid Id,
    string NeedType,
    string Description,
    DateTime CreatedAtUtc);

internal record ResourceAllocationBatchPrompt(
    IReadOnlyList<PendingRequestInfo> Requests,
    IReadOnlyList<ResourceAllocationCandidate> AvailableResources);

internal record PlanItemDraft(
    Guid HelpRequestId,
    int Priority,
    string Decision,
    Guid? ResourceId,
    string? ResourceType,
    decimal Quantity,
    decimal Confidence,
    string Reason,
    IReadOnlyList<string> Warnings,
    bool RequiresApproval);