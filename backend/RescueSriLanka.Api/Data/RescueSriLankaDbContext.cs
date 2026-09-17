using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Data;

public class RescueSriLankaDbContext(DbContextOptions<RescueSriLankaDbContext> options) : DbContext(options)
{
    public DbSet<Shelter> Shelters => Set<Shelter>();

    public DbSet<MedicalSupply> MedicalSupplies => Set<MedicalSupply>();

    public DbSet<FoodWaterStock> FoodWaterStocks => Set<FoodWaterStock>();

    public DbSet<ResourceAllocation> ResourceAllocations => Set<ResourceAllocation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Shelter>(entity =>
        {
            entity.Property(shelter => shelter.Name).HasMaxLength(200).IsRequired();
            entity.Property(shelter => shelter.Address).HasMaxLength(500).IsRequired();
            entity.Property(shelter => shelter.Latitude).HasPrecision(9, 6);
            entity.Property(shelter => shelter.Longitude).HasPrecision(9, 6);
            entity.Ignore(shelter => shelter.AvailableCapacity);
        });

        modelBuilder.Entity<MedicalSupply>(entity =>
        {
            entity.Property(supply => supply.Name).HasMaxLength(200).IsRequired();
            entity.Property(supply => supply.Unit).HasMaxLength(50).IsRequired();
            entity.Ignore(supply => supply.IsLowStock);
        });

        modelBuilder.Entity<FoodWaterStock>(entity =>
        {
            entity.Property(stock => stock.ItemName).HasMaxLength(200).IsRequired();
            entity.Property(stock => stock.Unit).HasMaxLength(50).IsRequired();
            entity.Property(stock => stock.QuantityOnHand).HasPrecision(12, 2);
            entity.Property(stock => stock.LowStockThreshold).HasPrecision(12, 2);
            entity.Ignore(stock => stock.IsLowStock);
        });

        modelBuilder.Entity<ResourceAllocation>(entity =>
        {
            entity.Property(allocation => allocation.ResourceType).HasMaxLength(50).IsRequired();
            entity.Property(allocation => allocation.Status).HasMaxLength(30).IsRequired();
            entity.HasIndex(allocation => new
            {
                allocation.ResourceType,
                allocation.ResourceId,
                allocation.Status
            });
        });
    }
}
