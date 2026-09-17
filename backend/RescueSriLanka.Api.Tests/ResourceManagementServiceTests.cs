using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Contracts;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Services;
using Xunit;

namespace RescueSriLanka.Api.Tests;

public class ResourceManagementServiceTests
{
    [Fact]
    public async Task MatchAndAllocateAsync_UsesShelterWithEnoughCapacity()
    {
        await using var context = CreateContext();
        var shelter = new Shelter
        {
            Id = Guid.NewGuid(),
            Name = "Kandy Relief Centre",
            Address = "Kandy",
            Capacity = 100,
            OccupiedCapacity = 80
        };
        context.Shelters.Add(shelter);
        await context.SaveChangesAsync();

        var service = new ResourceManagementService(context);
        var result = await service.MatchAndAllocateAsync(
            new MatchResourceRequest("Shelter", 10, null, null),
            CancellationToken.None);

        Assert.Equal(shelter.Id, result.ResourceId);
        Assert.Equal(90, (await context.Shelters.SingleAsync()).OccupiedCapacity);
    }

    [Fact]
    public async Task GetLowStockAlertsAsync_ReturnsMedicalSupplyBelowThreshold()
    {
        await using var context = CreateContext();
        context.MedicalSupplies.Add(new MedicalSupply
        {
            Id = Guid.NewGuid(),
            Name = "First aid kits",
            Unit = "kits",
            QuantityOnHand = 4,
            LowStockThreshold = 5
        });
        await context.SaveChangesAsync();

        var service = new ResourceManagementService(context);
        var alerts = await service.GetLowStockAlertsAsync(CancellationToken.None);

        var alert = Assert.Single(alerts);
        Assert.Equal("MedicalSupply", alert.ResourceType);
        Assert.Equal(4, alert.QuantityOnHand);
    }

    private static RescueSriLankaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<RescueSriLankaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
