using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentC.Models;

namespace RescueSriLanka.Api.Features.ComponentC.Data;

public static class ResourceDataSeeder
{
    public static async Task SeedAsync(RescueSriLankaDbContext dbContext, CancellationToken cancellationToken = default)
    {
        await dbContext.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "Donations" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "DonorName" varchar(160) NOT NULL,
                "ContactNumber" varchar(40) NOT NULL,
                "DonationType" varchar(80) NOT NULL,
                "Quantity" numeric(12,2) NOT NULL,
                "Unit" varchar(40) NOT NULL,
                "Notes" varchar(1000),
                "Status" varchar(30) NOT NULL DEFAULT 'PendingReview',
                "CreatedAtUtc" timestamptz NOT NULL DEFAULT now()
            );
            ALTER TABLE "HelpRequests"
                ADD COLUMN IF NOT EXISTS "RequesterName" varchar(160) NOT NULL DEFAULT '',
                ADD COLUMN IF NOT EXISTS "ContactNumber" varchar(40) NOT NULL DEFAULT '',
                ADD COLUMN IF NOT EXISTS "NeedType" varchar(50) NOT NULL DEFAULT 'Other',
                ADD COLUMN IF NOT EXISTS "Description" varchar(2000) NOT NULL DEFAULT '',
                ADD COLUMN IF NOT EXISTS "Latitude" numeric,
                ADD COLUMN IF NOT EXISTS "Longitude" numeric,
                ADD COLUMN IF NOT EXISTS "Status" varchar(30) NOT NULL DEFAULT 'Pending',
                ADD COLUMN IF NOT EXISTS "CreatedAtUtc" timestamptz NOT NULL DEFAULT now();
            DO $$
            BEGIN
                IF (SELECT data_type FROM information_schema.columns
                    WHERE table_name = 'HelpRequests' AND column_name = 'Status') = 'integer' THEN
                    ALTER TABLE "HelpRequests" ALTER COLUMN "Status" TYPE varchar(30) USING "Status"::text;
                END IF;
            END $$;
            ALTER TABLE "HelpRequests" ALTER COLUMN "CitizenId" DROP NOT NULL;
            ALTER TABLE "HelpRequests" ALTER COLUMN "Type" SET DEFAULT 0;
            ALTER TABLE "HelpRequests" ALTER COLUMN "UrgencyScore" SET DEFAULT 0;
            ALTER TABLE "HelpRequests" ALTER COLUMN "CreatedAt" SET DEFAULT now();
            ALTER TABLE "HelpRequests" ALTER COLUMN "UpdatedAt" SET DEFAULT now();
            ALTER TABLE "HelpRequests" ALTER COLUMN "Latitude" TYPE numeric USING "Latitude"::numeric;
            ALTER TABLE "HelpRequests" ALTER COLUMN "Longitude" TYPE numeric USING "Longitude"::numeric;
            """, cancellationToken);

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
        if (!await dbContext.HelpRequests.AnyAsync(request => request.Id == helpRequestId, cancellationToken))
        {
            dbContext.HelpRequests.Add(new HelpRequest
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
