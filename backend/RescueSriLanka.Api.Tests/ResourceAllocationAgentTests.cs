using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentC.Agents.ResourceAllocationAgent;
using RescueSriLanka.Api.Features.ComponentC.Models;
using RescueSriLanka.Api.Services.Llm;

namespace RescueSriLanka.Api.Tests;

public class ResourceAllocationAgentTests
{
    [Fact]
    public async Task ForecastAsync_UsesThirtyDayAllocationsAndReturnsDeterministicRisk()
    {
        await using var context = CreateContext();
        var stockId = Guid.NewGuid();
        context.FoodWaterStocks.Add(new FoodWaterStock
        {
            Id = stockId,
            ItemName = "Drinking water",
            Unit = "litres",
            QuantityOnHand = 30,
            LowStockThreshold = 5
        });
        context.FoodWaterStocks.Add(new FoodWaterStock
        {
            Id = Guid.NewGuid(),
            ItemName = "Inactive water",
            Unit = "litres",
            QuantityOnHand = 100,
            LowStockThreshold = 10,
            IsActive = false
        });
        context.ResourceAllocations.AddRange(
            Allocation(stockId, 30, DateTime.UtcNow.AddDays(-20)),
            Allocation(stockId, 60, DateTime.UtcNow.AddDays(-10)),
            Allocation(stockId, 500, DateTime.UtcNow.AddDays(-31)));
        await context.SaveChangesAsync();

        var agent = new ResourceForecastAgent(context, new UnconfiguredLlmClient(), NullLogger<ResourceForecastAgent>.Instance);
        var result = await agent.ForecastAsync();

        var item = Assert.Single(result.Items);
        Assert.Equal(30, result.WindowDays);
        Assert.Equal("Drinking water", item.Name);
        Assert.Equal(90, item.UsedInLast30Days);
        Assert.Equal(3, item.AverageDailyUse);
        Assert.Equal(10, item.EstimatedDaysRemaining);
        Assert.Equal("Watch", item.RiskLevel);
        Assert.Contains("replenishment", item.SuggestedAction);
    }

    [Fact]
    public async Task RecommendAsync_ReturnsValidatedRecommendationFromLlm()
    {
        await using var context = CreateContext();
        var requestId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        context.ResourceHelpRequests.Add(new HelpRequest
        {
            Id = requestId,
            RequesterName = "Test manager",
            ContactNumber = "000",
            NeedType = "Medical",
            Description = "Bandages"
        });
        context.MedicalSupplies.Add(new MedicalSupply
        {
            Id = stockId,
            Name = "Bandages",
            Unit = "packs",
            QuantityOnHand = 10,
            LowStockThreshold = 2
        });
        await context.SaveChangesAsync();

        var recommendationJson = JsonSerializer.Serialize(new
        {
            decision = "Recommend",
            resourceId = stockId,
            resourceType = "MedicalSupply",
            quantity = 2,
            confidence = 0.9,
            reason = "Matches the request",
            warnings = Array.Empty<string>(),
            requiresApproval = true
        });
        var agent = new ResourceAllocationAgent(context, new FakeLlmClient(recommendationJson), NullLogger<ResourceAllocationAgent>.Instance);

        var result = await agent.RecommendAsync(requestId);

        Assert.Equal("Recommend", result.Decision);
        Assert.Equal("Bandages", result.ResourceName);
        Assert.Equal(10, result.AvailableQuantity);
        Assert.True(result.RequiresApproval);
    }

    [Fact]
    public async Task RecommendAsync_FallsBackToNoMatchWhenLlmUnavailable()
    {
        await using var context = CreateContext();
        var requestId = Guid.NewGuid();
        context.ResourceHelpRequests.Add(new HelpRequest
        {
            Id = requestId,
            RequesterName = "Test manager",
            ContactNumber = "000",
            NeedType = "Medical",
            Description = "Bandages"
        });
        context.MedicalSupplies.Add(new MedicalSupply
        {
            Id = Guid.NewGuid(),
            Name = "Bandages",
            Unit = "packs",
            QuantityOnHand = 10,
            LowStockThreshold = 2
        });
        await context.SaveChangesAsync();

        var agent = new ResourceAllocationAgent(
            context,
            new FaultyLlmClient(new LlmUnavailableException("Google AI returned HTTP 503 after 3 attempt(s).")),
            NullLogger<ResourceAllocationAgent>.Instance);

        var result = await agent.RecommendAsync(requestId);

        Assert.Equal("NoMatch", result.Decision);
        Assert.True(result.RequiresApproval);
    }

    [Fact]
    public async Task PlanAsync_RanksAndRecommendsAcrossPendingRequests()
    {
        await using var context = CreateContext();
        var urgentRequestId = Guid.NewGuid();
        var otherRequestId = Guid.NewGuid();
        var bandagesId = Guid.NewGuid();
        var waterId = Guid.NewGuid();
        context.ResourceHelpRequests.AddRange(
            new HelpRequest
            {
                Id = urgentRequestId,
                RequesterName = "Urgent requester",
                ContactNumber = "111",
                NeedType = "Medical",
                Description = "Deep cut, needs bandages",
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5)
            },
            new HelpRequest
            {
                Id = otherRequestId,
                RequesterName = "Other requester",
                ContactNumber = "222",
                NeedType = "Water",
                Description = "Running low on drinking water",
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-10)
            });
        context.MedicalSupplies.Add(new MedicalSupply { Id = bandagesId, Name = "Bandages", Unit = "packs", QuantityOnHand = 10, LowStockThreshold = 2 });
        context.FoodWaterStocks.Add(new FoodWaterStock { Id = waterId, ItemName = "Drinking water", Unit = "litres", QuantityOnHand = 40, LowStockThreshold = 5 });
        await context.SaveChangesAsync();

        var planJson = JsonSerializer.Serialize(new object[]
        {
            new { helpRequestId = urgentRequestId, priority = 1, decision = "Recommend", resourceId = bandagesId, resourceType = "MedicalSupply", quantity = 2, confidence = 0.9, reason = "Medical need is most urgent", warnings = Array.Empty<string>(), requiresApproval = true },
            new { helpRequestId = otherRequestId, priority = 2, decision = "Recommend", resourceId = waterId, resourceType = "FoodWaterStock", quantity = 5, confidence = 0.7, reason = "Water need can wait slightly", warnings = Array.Empty<string>(), requiresApproval = true }
        });
        var agent = new ResourceAllocationAgent(context, new FakeLlmClient(planJson), NullLogger<ResourceAllocationAgent>.Instance);

        var plan = await agent.PlanAsync();

        Assert.Equal(2, plan.Items.Count);
        Assert.Equal(urgentRequestId, plan.Items[0].HelpRequestId);
        Assert.Equal(1, plan.Items[0].Priority);
        Assert.Equal("Recommend", plan.Items[0].Decision);
        Assert.Equal("Bandages", plan.Items[0].ResourceName);
        Assert.Equal(otherRequestId, plan.Items[1].HelpRequestId);
        Assert.Equal(2, plan.Items[1].Priority);
        Assert.Equal("Recommend", plan.Items[1].Decision);
        Assert.Equal("Drinking water", plan.Items[1].ResourceName);
    }

    [Fact]
    public async Task PlanAsync_DowngradesLowerPriorityRequestWhenStockIsExhausted()
    {
        await using var context = CreateContext();
        var firstRequestId = Guid.NewGuid();
        var secondRequestId = Guid.NewGuid();
        var bandagesId = Guid.NewGuid();
        context.ResourceHelpRequests.AddRange(
            new HelpRequest
            {
                Id = firstRequestId,
                RequesterName = "First requester",
                ContactNumber = "111",
                NeedType = "Medical",
                Description = "Severe wound",
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-10)
            },
            new HelpRequest
            {
                Id = secondRequestId,
                RequesterName = "Second requester",
                ContactNumber = "222",
                NeedType = "Medical",
                Description = "Minor cut",
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5)
            });
        context.MedicalSupplies.Add(new MedicalSupply { Id = bandagesId, Name = "Bandages", Unit = "packs", QuantityOnHand = 5, LowStockThreshold = 1 });
        await context.SaveChangesAsync();

        // The model (incorrectly) recommends the full 5 packs to both requests,
        // as if stock were unlimited per request — exactly the failure mode
        // deterministic post-validation must catch.
        var planJson = JsonSerializer.Serialize(new object[]
        {
            new { helpRequestId = firstRequestId, priority = 1, decision = "Recommend", resourceId = bandagesId, resourceType = "MedicalSupply", quantity = 5, confidence = 0.9, reason = "Severe wound takes priority", warnings = Array.Empty<string>(), requiresApproval = true },
            new { helpRequestId = secondRequestId, priority = 2, decision = "Recommend", resourceId = bandagesId, resourceType = "MedicalSupply", quantity = 5, confidence = 0.6, reason = "Also needs bandages", warnings = Array.Empty<string>(), requiresApproval = true }
        });
        var agent = new ResourceAllocationAgent(context, new FakeLlmClient(planJson), NullLogger<ResourceAllocationAgent>.Instance);

        var plan = await agent.PlanAsync();

        Assert.Equal(2, plan.Items.Count);
        var first = plan.Items.Single(item => item.HelpRequestId == firstRequestId);
        var second = plan.Items.Single(item => item.HelpRequestId == secondRequestId);
        Assert.Equal("Recommend", first.Decision);
        Assert.Equal(5, first.Quantity);
        Assert.Equal("NoMatch", second.Decision);
        Assert.Contains(second.Warnings, warning => warning.Contains("higher-priority", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PlanAsync_FallsBackToNoMatchPlanWhenLlmUnavailable()
    {
        await using var context = CreateContext();
        var requestId = Guid.NewGuid();
        context.ResourceHelpRequests.Add(new HelpRequest
        {
            Id = requestId,
            RequesterName = "Requester",
            ContactNumber = "111",
            NeedType = "Medical",
            Description = "Needs bandages"
        });
        context.MedicalSupplies.Add(new MedicalSupply { Id = Guid.NewGuid(), Name = "Bandages", Unit = "packs", QuantityOnHand = 5, LowStockThreshold = 1 });
        await context.SaveChangesAsync();

        var agent = new ResourceAllocationAgent(
            context,
            new FaultyLlmClient(new LlmUnavailableException("Google AI did not return a usable response.")),
            NullLogger<ResourceAllocationAgent>.Instance);

        var plan = await agent.PlanAsync();

        var item = Assert.Single(plan.Items);
        Assert.Equal(requestId, item.HelpRequestId);
        Assert.Equal("NoMatch", item.Decision);
        Assert.True(item.RequiresApproval);
    }

    private static ResourceAllocation Allocation(Guid resourceId, decimal quantity, DateTime allocatedAtUtc) => new()
    {
        ResourceType = "FoodWaterStock",
        ResourceId = resourceId,
        Quantity = quantity,
        AllocatedAtUtc = allocatedAtUtc
    };

    private static AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private sealed class FakeLlmClient(string response) : ILlmClient
    {
        public bool IsConfigured => true;
        public string ModelName => "fake-model";

        public Task<string> GenerateAsync(
            string systemInstruction, string prompt, object? jsonSchema = null,
            IReadOnlyList<LlmImage>? images = null, CancellationToken ct = default) =>
            Task.FromResult(response);
    }

    private sealed class FaultyLlmClient(Exception exception) : ILlmClient
    {
        public bool IsConfigured => true;
        public string ModelName => "fake-model";

        public Task<string> GenerateAsync(
            string systemInstruction, string prompt, object? jsonSchema = null,
            IReadOnlyList<LlmImage>? images = null, CancellationToken ct = default) =>
            throw exception;
    }

    private sealed class UnconfiguredLlmClient : ILlmClient
    {
        public bool IsConfigured => false;
        public string ModelName => "unconfigured";

        public Task<string> GenerateAsync(
            string systemInstruction, string prompt, object? jsonSchema = null,
            IReadOnlyList<LlmImage>? images = null, CancellationToken ct = default) =>
            throw new LlmUnavailableException("Not configured.");
    }
}
