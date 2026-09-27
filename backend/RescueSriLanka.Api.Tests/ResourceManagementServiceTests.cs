using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Features.ComponentC.DTOs;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentC.Models;
using RescueSriLanka.Api.Features.ComponentC.Services;
using RescueSriLanka.Api.Models;
using Xunit;

namespace RescueSriLanka.Api.Tests;

public class ResourceManagementServiceTests
{
    [Fact]
    public async Task CreateResourceSubmissions_UseRegisteredProfileDetails()
    {
        await using var context = CreateContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Nimal Perera",
            Email = "nimal@example.com",
            PasswordHash = "test-hash",
            PhoneNumber = "0712345678",
            District = "Kandy"
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = new ResourceManagementService(context);
        var helpRequest = await service.CreateHelpRequestAsync(
            new CreateHelpRequestRequest("Typed Name", "0000000000", "Food and water", "Need water", null, null),
            user.Id,
            CancellationToken.None);
        var donation = await service.CreateDonationAsync(
            new CreateDonationRequest("Typed Name", "0000000000", "Clothing", 2, "bags", null),
            user.Id,
            CancellationToken.None);

        Assert.Equal(user.FullName, helpRequest.RequesterName);
        Assert.Equal(user.PhoneNumber, helpRequest.ContactNumber);
        Assert.Equal(user.District, helpRequest.District);
        Assert.Equal(user.FullName, donation.DonorName);
        Assert.Equal(user.PhoneNumber, donation.ContactNumber);
        Assert.Equal(user.District, donation.District);
    }

    [Fact]
    public async Task UpdateHelpRequestStatusAsync_AcceptsRequestAndAllocationFulfillsIt()
    {
        await using var context = CreateContext();
        var requestId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        context.ResourceHelpRequests.Add(new HelpRequest
        {
            Id = requestId,
            RequesterName = "Nimal Perera",
            ContactNumber = "0712345678",
            NeedType = "Food and water",
            Description = "Drinking water"
        });
        context.FoodWaterStocks.Add(new FoodWaterStock
        {
            Id = stockId,
            ItemName = "Bottled water",
            Unit = "litres",
            QuantityOnHand = 100,
            LowStockThreshold = 20
        });
        await context.SaveChangesAsync();

        var service = new ResourceManagementService(context);
        var accepted = await service.UpdateHelpRequestStatusAsync(
            requestId,
            new UpdateHelpRequestStatusRequest("Accepted"),
            CancellationToken.None);
        var allocation = await service.AllocateAsync(
            new AllocateResourceRequest("FoodWaterStock", stockId, 20, requestId, null),
            CancellationToken.None);

        Assert.Equal("Accepted", accepted!.Status);
        Assert.Equal("Fulfilled", (await context.ResourceHelpRequests.FindAsync(requestId))!.Status);
        Assert.Equal(80, (await context.FoodWaterStocks.FindAsync(stockId))!.QuantityOnHand);
        Assert.Equal(requestId, allocation.HelpRequestId);
    }

    [Fact]
    public async Task UpdateDonationStatusAsync_AcceptsDonation()
    {
        await using var context = CreateContext();
        var donationId = Guid.NewGuid();
        context.Donations.Add(new Donation
        {
            Id = donationId,
            DonorName = "Lanka Community Group",
            ContactNumber = "0812234567",
            DonationType = "Food and water",
            Quantity = 50,
            Unit = "packs"
        });
        await context.SaveChangesAsync();

        var service = new ResourceManagementService(context);
        var result = await service.UpdateDonationStatusAsync(
            donationId,
            new UpdateHelpRequestStatusRequest("Accepted"),
            CancellationToken.None);

        Assert.Equal("Accepted", result!.Status);
        Assert.Equal("Accepted", (await context.Donations.FindAsync(donationId))!.Status);
        var supply = await context.DonatedSupplies.SingleAsync();
        Assert.Equal(donationId, supply.DonationId);
        Assert.Equal("Food and water", supply.Name);
        Assert.Equal(50, supply.QuantityOnHand);
        Assert.Equal("packs", supply.Unit);
    }

    [Fact]
    public async Task AllocateAsync_DeductsSelectedDonatedSupply()
    {
        await using var context = CreateContext();
        var supplyId = Guid.NewGuid();
        context.DonatedSupplies.Add(new DonatedSupply
        {
            Id = supplyId,
            DonationId = Guid.NewGuid(),
            Name = "Clothing",
            DonorName = "Community group",
            QuantityOnHand = 12,
            Unit = "bags"
        });
        await context.SaveChangesAsync();

        var service = new ResourceManagementService(context);
        var allocation = await service.AllocateAsync(
            new AllocateResourceRequest("DonatedSupply", supplyId, 5, null, null),
            CancellationToken.None);

        Assert.Equal(supplyId, allocation.ResourceId);
        Assert.Equal(7, (await context.DonatedSupplies.FindAsync(supplyId))!.QuantityOnHand);
    }

    [Fact]
    public async Task CreateManagedSupplyAsync_PreservesCategoryAndStockDetails()
    {
        await using var context = CreateContext();
        var service = new ResourceManagementService(context);

        var result = await service.CreateManagedSupplyAsync(
            new CreateManagedSupplyRequest("Hygiene items", "Toothbrushes", "packs", 25, 5),
            CancellationToken.None);

        Assert.Equal("Hygiene items", result.Category);
        Assert.Equal("Toothbrushes", result.Name);
        Assert.Equal(25, result.QuantityOnHand);
        Assert.Contains(await service.GetManagedSuppliesAsync(CancellationToken.None), supply => supply.Id == result.Id);
    }

    [Fact]
    public async Task AllocateAsync_DeductsSelectedManagedSupply()
    {
        await using var context = CreateContext();
        var supplyId = Guid.NewGuid();
        context.ManagedSupplies.Add(new ManagedSupply
        {
            Id = supplyId,
            Category = "Sanitary products",
            Name = "Napkins",
            Unit = "packs",
            QuantityOnHand = 12,
            LowStockThreshold = 2
        });
        await context.SaveChangesAsync();

        var service = new ResourceManagementService(context);
        var allocation = await service.AllocateAsync(
            new AllocateResourceRequest("ManagedSupply", supplyId, 5, null, null),
            CancellationToken.None);

        Assert.Equal(supplyId, allocation.ResourceId);
        Assert.Equal(7, (await context.ManagedSupplies.FindAsync(supplyId))!.QuantityOnHand);
    }

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

    [Fact]
    public async Task AllocateAsync_RejectsMissingResourceType()
    {
        await using var context = CreateContext();
        var service = new ResourceManagementService(context);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AllocateAsync(
                new AllocateResourceRequest(string.Empty, Guid.NewGuid(), 1m, null, null),
                CancellationToken.None));

        Assert.Equal("Resource type is required.", ex.Message);
    }

    [Fact]
    public async Task AllocateAsync_WithClothes_FulfillsHelpRequestSuccessfully()
    {
        await using var context = CreateContext();
        var requestId = Guid.NewGuid();
        context.ResourceHelpRequests.Add(new HelpRequest
        {
            Id = requestId,
            RequesterName = "Kamal Gunaratne",
            ContactNumber = "0771234567",
            NeedType = "Clothes",
            Description = "Warm clothing"
        });
        await context.SaveChangesAsync();

        var service = new ResourceManagementService(context);
        var allocation = await service.AllocateAsync(
            new AllocateResourceRequest("Clothes", Guid.NewGuid(), 10, requestId, null),
            CancellationToken.None);

        Assert.Equal("Clothes", allocation.ResourceType);
        Assert.Equal(10, allocation.Quantity);
        Assert.Equal("Fulfilled", (await context.ResourceHelpRequests.FindAsync(requestId))!.Status);
    }

    [Fact]
    public async Task MatchAndAllocateAsync_WithOtherCustomType_FulfillsHelpRequestSuccessfully()
    {
        await using var context = CreateContext();
        var requestId = Guid.NewGuid();
        context.ResourceHelpRequests.Add(new HelpRequest
        {
            Id = requestId,
            RequesterName = "Sunil Perera",
            ContactNumber = "0751234567",
            NeedType = "Tents",
            Description = "Emergency shelter kits"
        });
        await context.SaveChangesAsync();

        var service = new ResourceManagementService(context);
        var allocation = await service.MatchAndAllocateAsync(
            new MatchResourceRequest("Tents and Blankets", 5, requestId, null),
            CancellationToken.None);

        Assert.Equal("Tents and Blankets", allocation.ResourceType);
        Assert.Equal(5, allocation.Quantity);
        Assert.Equal("Fulfilled", (await context.ResourceHelpRequests.FindAsync(requestId))!.Status);
    }

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
