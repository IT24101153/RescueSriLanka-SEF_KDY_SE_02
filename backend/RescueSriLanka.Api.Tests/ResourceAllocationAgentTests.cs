using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentC.Agents.ResourceAllocationAgent;
using RescueSriLanka.Api.Features.ComponentC.Models;

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

        var agent = new ResourceForecastAgent(context, new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<ResourceForecastAgent>.Instance);
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
    public async Task RecommendAsync_ContinuesToNextModelAfterModelNotFound()
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
        var responseJson = JsonSerializer.Serialize(new
        {
            candidates = new[] { new { content = new { parts = new[] { new { text = recommendationJson } } } } }
        });
        var handler = new SequenceHandler(
            new HttpResponseMessage(HttpStatusCode.NotFound),
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(responseJson, Encoding.UTF8, "application/json") });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gemini:ApiKey"] = "test-key",
            ["Gemini:Model"] = "gemini-2.5-flash"
        }).Build();
        var agent = new ResourceAllocationAgent(context, new HttpClient(handler), configuration, NullLogger<ResourceAllocationAgent>.Instance);

        var result = await agent.RecommendAsync(requestId);

        Assert.Equal(2, handler.CallCount);
        Assert.Equal("Recommend", result.Decision);
        Assert.Equal("Bandages", result.ResourceName);
        Assert.Equal(10, result.AvailableQuantity);
        Assert.True(result.RequiresApproval);
    }

    [Fact]
    public async Task RecommendAsync_DiscoversAccessibleGenerationModelAfterFallbacksFail()
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
        var recommendationResponse = JsonSerializer.Serialize(new
        {
            candidates = new[] { new { content = new { parts = new[] { new { text = recommendationJson } } } } }
        });
        var modelsResponse = JsonSerializer.Serialize(new
        {
            models = new[]
            {
                new { name = "models/gemini-2.0-flash", supportedGenerationMethods = new[] { "generateContent" } },
                new { name = "models/gemini-embedding-001", supportedGenerationMethods = new[] { "embedContent" } }
            }
        });
        var handler = new SequenceHandler(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.NotFound),
            new HttpResponseMessage(HttpStatusCode.NotFound),
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(modelsResponse, Encoding.UTF8, "application/json") },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(recommendationResponse, Encoding.UTF8, "application/json") });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gemini:ApiKey"] = "test-key",
            ["Gemini:Model"] = "gemini-preview-unavailable"
        }).Build();
        var agent = new ResourceAllocationAgent(context, new HttpClient(handler), configuration, NullLogger<ResourceAllocationAgent>.Instance);

        var result = await agent.RecommendAsync(requestId);

        Assert.Equal(5, handler.CallCount);
        Assert.Equal("Recommend", result.Decision);
        Assert.Equal("Bandages", result.ResourceName);
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

    private sealed class SequenceHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responses[CallCount++]);
    }
}