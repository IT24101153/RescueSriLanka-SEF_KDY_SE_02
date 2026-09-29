using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentC.Models;

namespace RescueSriLanka.Api.Features.ComponentC.Data;

public static class ResourceDataSeeder
{
    public static async Task SeedAsync(AppDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var medicalSupplies = new[]
        {
            new MedicalSupply { Id = Guid.Parse("20000000-0000-0000-0000-000000000001"), Name = "First aid kits", Unit = "kits", QuantityOnHand = 48, LowStockThreshold = 20 },
            new MedicalSupply { Id = Guid.Parse("20000000-0000-0000-0000-000000000002"), Name = "Bandages", Unit = "packs", QuantityOnHand = 120, LowStockThreshold = 40 },
            new MedicalSupply { Id = Guid.Parse("20000000-0000-0000-0000-000000000003"), Name = "Essential medicines", Unit = "boxes", QuantityOnHand = 16, LowStockThreshold = 25 }
        };
        foreach (var seededSupply in medicalSupplies)
        {
            var existingSupply = await dbContext.MedicalSupplies
                .SingleOrDefaultAsync(supply => supply.Id == seededSupply.Id, cancellationToken);
            if (existingSupply is null)
            {
                dbContext.MedicalSupplies.Add(seededSupply);
            }
            else if (!existingSupply.IsActive)
            {
                existingSupply.IsActive = true;
                existingSupply.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        var foodWaterStocks = new[]
        {
            new FoodWaterStock { Id = Guid.Parse("30000000-0000-0000-0000-000000000001"), ItemName = "Bottled water", Unit = "litres", QuantityOnHand = 850, LowStockThreshold = 300 },
            new FoodWaterStock { Id = Guid.Parse("30000000-0000-0000-0000-000000000002"), ItemName = "Rice and dry rations", Unit = "kg", QuantityOnHand = 420, LowStockThreshold = 150 },
            new FoodWaterStock { Id = Guid.Parse("30000000-0000-0000-0000-000000000003"), ItemName = "Ready-to-eat meals", Unit = "packs", QuantityOnHand = 95, LowStockThreshold = 120 }
        };
        foreach (var seededStock in foodWaterStocks)
        {
            var existingStock = await dbContext.FoodWaterStocks
                .SingleOrDefaultAsync(stock => stock.Id == seededStock.Id, cancellationToken);
            if (existingStock is null)
            {
                dbContext.FoodWaterStocks.Add(seededStock);
            }
            else if (!existingStock.IsActive)
            {
                existingStock.IsActive = true;
                existingStock.UpdatedAtUtc = DateTime.UtcNow;
            }
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
