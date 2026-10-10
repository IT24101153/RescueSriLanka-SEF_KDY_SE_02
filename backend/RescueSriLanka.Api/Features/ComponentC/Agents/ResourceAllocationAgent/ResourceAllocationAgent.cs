using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentC.Models;
using RescueSriLanka.Api.Services.Llm;

namespace RescueSriLanka.Api.Features.ComponentC.Agents.ResourceAllocationAgent;

public sealed class ResourceAllocationAgent(
    AppDbContext dbContext,
    ILlmClient llm,
    ILogger<ResourceAllocationAgent> logger) : IResourceAllocationAgent
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string RecommendSystemInstruction =
        """
        You are a resource allocation assistant for an emergency response manager.
        Recommend an available resource for the request. Never invent an ID, resource,
        quantity, or fact — only use candidates from the list you are given. The
        manager must approve your recommendation before anything is allocated.
        Return only the JSON object described by the schema.
        """;

    private const string PlanSystemInstruction =
        """
        You are a resource allocation assistant for an emergency response manager.
        Rank the pending requests by urgency (1 = most urgent) and recommend an
        available resource for each one. Never invent an ID, resource, quantity, or
        fact — only use candidates from the list you are given — and never recommend
        more of a resource across all requests combined than the total available
        quantity. The manager must approve every recommendation before anything is
        allocated. Return only the JSON array described by the schema, with exactly
        one entry per request.
        """;

    private static object RecommendResponseSchema => new
    {
        type = "object",
        properties = new
        {
            decision = new { type = "string", @enum = new[] { "Recommend", "NoMatch" } },
            resourceId = new { type = "string", nullable = true },
            resourceType = new { type = "string", nullable = true },
            quantity = new { type = "number" },
            confidence = new { type = "number" },
            reason = new { type = "string" },
            warnings = new { type = "array", items = new { type = "string" } },
            requiresApproval = new { type = "boolean" }
        },
        required = new[] { "decision", "quantity", "confidence", "reason", "warnings", "requiresApproval" }
    };

    private static object PlanResponseSchema => new
    {
        type = "array",
        items = new
        {
            type = "object",
            properties = new
            {
                helpRequestId = new { type = "string" },
                priority = new { type = "integer" },
                decision = new { type = "string", @enum = new[] { "Recommend", "NoMatch" } },
                resourceId = new { type = "string", nullable = true },
                resourceType = new { type = "string", nullable = true },
                quantity = new { type = "number" },
                confidence = new { type = "number" },
                reason = new { type = "string" },
                warnings = new { type = "array", items = new { type = "string" } },
                requiresApproval = new { type = "boolean" }
            },
            required = new[]
            {
                "helpRequestId", "priority", "decision", "quantity",
                "confidence", "reason", "warnings", "requiresApproval"
            }
        }
    };

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

        if (!llm.IsConfigured)
        {
            return RulesRecommendation(request, candidates);
        }

        var prompt = $$"""
            Request:
            {{JsonSerializer.Serialize(new ResourceAllocationPrompt(request.Id, request.NeedType, request.Description, candidates), JsonOptions)}}
            """;

        try
        {
            var raw = await llm.GenerateAsync(
                RecommendSystemInstruction, prompt, RecommendResponseSchema, ct: cancellationToken);
            var recommendation = JsonSerializer.Deserialize<ResourceAllocationRecommendation>(raw, JsonOptions);
            var validated = ValidateRecommendation(recommendation, candidates);
            if (validated.Decision == "Recommend")
            {
                return validated;
            }

            // The model answered, but with nothing usable — an invented resource
            // id, an amount above stock, or no match at all. That is the rule
            // engine's cue just as much as an unreachable model is: the rules can
            // read the amount off the request and cap it at what is on hand.
            // Without this a perfectly servable request is simply declined, and
            // nothing is ever deducted for it.
            logger.LogInformation(
                "Gemini gave no usable allocation for request {RequestId} ({Reason}); using the built-in rules.",
                helpRequestId, validated.Reason);
            return RulesRecommendation(request, candidates);
        }
        catch (Exception exception) when (exception is LlmUnavailableException or JsonException)
        {
            logger.LogWarning(exception, "Gemini allocation recommendation failed for request {RequestId}.", helpRequestId);
            return RulesRecommendation(request, candidates);
        }
    }

    public async Task<ResourceAllocationPlan> PlanAsync(CancellationToken cancellationToken = default)
    {
        var generatedAt = DateTime.UtcNow;
        var pendingRequests = await dbContext.ResourceHelpRequests
            .AsNoTracking()
            .Where(request => request.Status == "Pending")
            .OrderBy(request => request.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        if (pendingRequests.Count == 0)
        {
            return new ResourceAllocationPlan(generatedAt, "There are no pending help requests to plan for.", []);
        }

        var candidates = await GetCandidatesAsync(cancellationToken);
        if (candidates.Count == 0)
        {
            return FallbackPlan(
                generatedAt, pendingRequests,
                "There are no active resources with available stock.",
                "No active stock is available.");
        }

        if (!llm.IsConfigured)
        {
            return RulesPlan(generatedAt, pendingRequests, candidates);
        }

        var pendingInfos = pendingRequests
            .Select(request => new PendingRequestInfo(request.Id, request.NeedType, request.Description, request.CreatedAtUtc))
            .ToList();
        var prompt = $$"""
            Requests and available resources:
            {{JsonSerializer.Serialize(new ResourceAllocationBatchPrompt(pendingInfos, candidates), JsonOptions)}}
            """;

        try
        {
            var raw = await llm.GenerateAsync(
                PlanSystemInstruction, prompt, PlanResponseSchema, ct: cancellationToken);
            var drafts = JsonSerializer.Deserialize<List<PlanItemDraft>>(raw, JsonOptions);
            return BuildPlan(generatedAt, drafts, pendingRequests, candidates);
        }
        catch (Exception exception) when (exception is LlmUnavailableException or JsonException)
        {
            logger.LogWarning(exception, "Gemini allocation plan failed.");
            return RulesPlan(generatedAt, pendingRequests, candidates);
        }
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
            Warnings = recommendation.Warnings ?? [],
            Source = "AI"
        };
    }

    // The AI could not answer, so the built-in rules do. Why the AI failed is in the server log; the manager sees the result.
    private static ResourceAllocationRecommendation RulesRecommendation(
        HelpRequest request, IReadOnlyList<ResourceAllocationCandidate> candidates)
    {
        var result = ResourceAllocationRules.Recommend(request, candidates);
        List<string> warnings = [.. result.Warnings];
        if (result.Decision != "Recommend" && StockHint(request, candidates) is { } hint) warnings.Add(hint);
        return result with { Warnings = warnings };
    }

    // Same rules across every pending request, in a fixed order, taking stock off as it is claimed.
    private static ResourceAllocationPlan RulesPlan(
        DateTime generatedAtUtc,
        IReadOnlyList<HelpRequest> pendingRequests,
        IReadOnlyList<ResourceAllocationCandidate> candidates)
    {
        var remaining = candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.QuantityOnHand);
        var items = new List<ResourceAllocationPlanItem>();

        foreach (var request in pendingRequests
            .OrderByDescending(ResourceAllocationRules.Urgency)
            .ThenBy(request => request.CreatedAtUtc))
        {
            items.Add(RulesPlanItem(request, items.Count + 1, candidates, remaining));
        }

        var matched = items.Count(item => item.Decision == "Recommend");
        return new ResourceAllocationPlan(
            generatedAtUtc,
            $"{matched} of {items.Count} pending request(s) matched to available stock.",
            items);
    }

    /// <summary>
    /// One plan item decided by the deterministic rules, against the stock still
    /// unclaimed by the requests above it. Claims what it recommends from
    /// <paramref name="remaining"/>, so a plan never promises the same stock twice.
    /// </summary>
    /// <param name="extraWarnings">Why the model's own answer was dropped, when it had one.</param>
    private static ResourceAllocationPlanItem RulesPlanItem(
        HelpRequest request,
        int priority,
        IReadOnlyList<ResourceAllocationCandidate> candidates,
        Dictionary<Guid, decimal> remaining,
        IReadOnlyList<string>? extraWarnings = null)
    {
        var result = ResourceAllocationRules.Recommend(request, candidates, remaining);
        List<string> warnings = [.. extraWarnings ?? [], .. result.Warnings];

        if (result.Decision == "Recommend")
        {
            remaining[result.ResourceId!.Value] -= result.Quantity;
            return new ResourceAllocationPlanItem(
                request.Id, request.NeedType, request.RequesterName, priority, "Recommend",
                result.ResourceId, result.ResourceType, result.ResourceName, result.Unit,
                result.Quantity, result.AvailableQuantity, result.Confidence,
                result.Reason, warnings, true, ResourceAllocationRules.Source);
        }

        if (StockHint(request, candidates) is { } hint) warnings.Add(hint);
        return NoMatchPlanItem(request, priority, result.Reason, warnings, ResourceAllocationRules.Source);
    }

    private static ResourceAllocationRecommendation NoMatch(string reason, string warning, string? stockHint = null) =>
        new("NoMatch", null, null, 0, 0, reason, stockHint is null ? [warning] : [warning, stockHint], true);

    // With no AI there is no safe way to pick an amount: a request does not state one, so any number
    // would be invented. What can be said without it is which stock looks relevant, for the manager to judge.
    private static string? StockHint(HelpRequest request, IReadOnlyList<ResourceAllocationCandidate> candidates)
    {
        var words = $"{request.NeedType} {request.Description}"
            .Split([' ', ',', '.', ';', ':', '/', '-', '(', ')', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.ToLowerInvariant())
            .Where(word => word.Length >= 4)
            .Distinct()
            .ToList();

        var matches = candidates
            .Select(candidate =>
            {
                var text = $"{candidate.Name} {candidate.Category} {candidate.ResourceType}".ToLowerInvariant();
                return (Candidate: candidate, Score: words.Count(word => text.Contains(word)));
            })
            .Where(match => match.Score > 0)
            .OrderByDescending(match => match.Score)
            .ThenByDescending(match => match.Candidate.QuantityOnHand)
            .Take(3)
            .Select(match => $"{match.Candidate.Name} ({match.Candidate.QuantityOnHand:0.##} {match.Candidate.Unit})")
            .ToList();

        return matches.Count == 0
            ? null
            : $"Stock that may fit, with no quantity suggested without the AI: {string.Join("; ", matches)}.";
    }

    // The model can rank requests and propose a resource per request, but it
    // cannot be trusted to correctly subtract shared stock across multiple
    // recommendations in one response. This walks the model's own priority
    // order and enforces stock limits deterministically, downgrading any
    // request that would over-claim a resource a higher-priority request
    // already took.
    private static ResourceAllocationPlan BuildPlan(
        DateTime generatedAtUtc,
        IReadOnlyList<PlanItemDraft>? drafts,
        IReadOnlyList<HelpRequest> pendingRequests,
        IReadOnlyList<ResourceAllocationCandidate> candidates)
    {
        var draftsByRequest = (drafts ?? [])
            .Where(draft => pendingRequests.Any(request => request.Id == draft.HelpRequestId))
            .GroupBy(draft => draft.HelpRequestId)
            .ToDictionary(group => group.Key, group => group.First());

        var ordered = pendingRequests
            .Select((request, index) => (
                request,
                draft: draftsByRequest.GetValueOrDefault(request.Id),
                fallbackPriority: index + 1))
            .OrderBy(entry => entry.draft?.Priority ?? entry.fallbackPriority)
            .ThenBy(entry => entry.request.CreatedAtUtc)
            .ToList();

        var remaining = candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.QuantityOnHand);
        var items = new List<ResourceAllocationPlanItem>();

        foreach (var (request, draft, _) in ordered)
        {
            var priority = items.Count + 1;
            var candidate = draft is null ? null : candidates.SingleOrDefault(item => item.Id == draft.ResourceId);
            var usable =
                draft is not null &&
                string.Equals(draft.Decision, "Recommend", StringComparison.OrdinalIgnoreCase) &&
                candidate is not null &&
                draft.Quantity > 0 &&
                draft.Quantity <= remaining.GetValueOrDefault(candidate.Id, 0);

            if (!usable)
            {
                // Nothing usable from the model for this request, so the rules
                // decide it instead — against the stock left after the requests
                // above it, so one plan never promises the same stock twice.
                // Why the model's answer was dropped is worth keeping: "already
                // claimed" tells the manager something the rules cannot.
                var overClaimed =
                    candidate is not null &&
                    draft!.Quantity > 0 &&
                    draft.Quantity > remaining.GetValueOrDefault(candidate.Id, 0);

                items.Add(RulesPlanItem(
                    request, priority, candidates, remaining,
                    overClaimed
                        ? ["Insufficient remaining stock — already claimed by a higher-priority request."]
                        : null));
                continue;
            }

            remaining[candidate!.Id] = remaining.GetValueOrDefault(candidate.Id, 0) - draft!.Quantity;
            items.Add(new ResourceAllocationPlanItem(
                request.Id,
                request.NeedType,
                request.RequesterName,
                priority,
                "Recommend",
                candidate.Id,
                candidate.ResourceType,
                candidate.Name,
                candidate.Unit,
                draft.Quantity,
                candidate.QuantityOnHand,
                Math.Clamp(draft.Confidence, 0, 1),
                draft.Reason,
                draft.Warnings ?? [],
                true));
        }

        var recommended = items.Count(item => item.Decision == "Recommend");
        var summary = $"{recommended} of {items.Count} pending request(s) matched to available stock.";
        return new ResourceAllocationPlan(generatedAtUtc, summary, items);
    }

    private static ResourceAllocationPlanItem NoMatchPlanItem(
        HelpRequest request, int priority, string reason, IReadOnlyList<string> warnings, string source = "AI") =>
        new(request.Id, request.NeedType, request.RequesterName, priority, "NoMatch",
            null, null, null, null, 0, null, 0, reason, warnings, true, source);

    private static ResourceAllocationPlan FallbackPlan(
        DateTime generatedAtUtc, IReadOnlyList<HelpRequest> pendingRequests, string reason, string warning,
        IReadOnlyList<ResourceAllocationCandidate>? candidates = null) =>
        new(generatedAtUtc, reason, [.. pendingRequests
            .Select((request, index) =>
            {
                var hint = candidates is null ? null : StockHint(request, candidates);
                return NoMatchPlanItem(request, index + 1, reason, hint is null ? [warning] : [warning, hint]);
            })]);
}
