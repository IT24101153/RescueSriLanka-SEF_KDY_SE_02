using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentC.Models;

namespace RescueSriLanka.Api.Features.ComponentC.Data;

public static class ResourceDataSeeder
{
    public static async Task SeedAsync(AppDbContext dbContext, CancellationToken cancellationToken = default)
    {

        var shelterIds = new[]
        {
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Guid.Parse("10000000-0000-0000-0000-000000000002"),
            Guid.Parse("10000000-0000-0000-0000-000000000003")
        };
        if (!await dbContext.Shelters.AnyAsync(shelter => shelterIds.Contains(shelter.Id), cancellationToken))
        {
            dbContext.Shelters.AddRange(
                new Shelter { Id = shelterIds[0], Name = "Kandy Relief Centre", Address = "Peradeniya Road, Kandy", Latitude = 7.2906m, Longitude = 80.6337m, Capacity = 180, OccupiedCapacity = 72 },
                new Shelter { Id = shelterIds[1], Name = "Colombo Community Shelter", Address = "Narahenpita, Colombo 05", Latitude = 6.8941m, Longitude = 79.8760m, Capacity = 240, OccupiedCapacity = 118 },
                new Shelter { Id = shelterIds[2], Name = "Galle District Safe Centre", Address = "Wakwella Road, Galle", Latitude = 6.0329m, Longitude = 80.2168m, Capacity = 120, OccupiedCapacity = 34 });
        }

        var medicalIds = new[]
        {
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Guid.Parse("20000000-0000-0000-0000-000000000002"),
            Guid.Parse("20000000-0000-0000-0000-000000000003")
        };
        if (!await dbContext.MedicalSupplies.AnyAsync(supply => medicalIds.Contains(supply.Id), cancellationToken))
        {
            dbContext.MedicalSupplies.AddRange(
                new MedicalSupply { Id = medicalIds[0], Name = "First aid kits", Unit = "kits", QuantityOnHand = 48, LowStockThreshold = 20 },
                new MedicalSupply { Id = medicalIds[1], Name = "Bandages", Unit = "packs", QuantityOnHand = 120, LowStockThreshold = 40 },
                new MedicalSupply { Id = medicalIds[2], Name = "Essential medicines", Unit = "boxes", QuantityOnHand = 16, LowStockThreshold = 25 });
        }

        var stockIds = new[]
        {
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            Guid.Parse("30000000-0000-0000-0000-000000000002"),
            Guid.Parse("30000000-0000-0000-0000-000000000003")
        };
        if (!await dbContext.FoodWaterStocks.AnyAsync(stock => stockIds.Contains(stock.Id), cancellationToken))
        {
            dbContext.FoodWaterStocks.AddRange(
                new FoodWaterStock { Id = stockIds[0], ItemName = "Bottled water", Unit = "litres", QuantityOnHand = 850, LowStockThreshold = 300 },
                new FoodWaterStock { Id = stockIds[1], ItemName = "Rice and dry rations", Unit = "kg", QuantityOnHand = 420, LowStockThreshold = 150 },
                new FoodWaterStock { Id = stockIds[2], ItemName = "Ready-to-eat meals", Unit = "packs", QuantityOnHand = 95, LowStockThreshold = 120 });
        }

        var helpRequestId = Guid.Parse("40000000-0000-0000-0000-000000000001");
        if (!await dbContext.ResourceHelpRequests.AnyAsync(request => request.Id == helpRequestId, cancellationToken))
        {
            dbContext.ResourceHelpRequests.Add(new HelpRequest
            {
                Id = helpRequestId,
                RequesterName = "Nimal Perera",
                ContactNumber = "0712345678",
                NeedType = "Food and water",
                Description = "Family of four needs drinking water and dry food after flooding.",
                Latitude = 7.2906,
                Longitude = 80.6337
            });
        }

        var donationId = Guid.Parse("50000000-0000-0000-0000-000000000001");
        if (!await dbContext.Donations.AnyAsync(donation => donation.Id == donationId, cancellationToken))
        {
            dbContext.Donations.Add(new Donation
            {
                Id = donationId,
                DonorName = "Lanka Community Group",
                ContactNumber = "0812234567",
                DonationType = "Food and water",
                Quantity = 250,
                Unit = "litres",
                Notes = "Available for collection in Kandy."
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
