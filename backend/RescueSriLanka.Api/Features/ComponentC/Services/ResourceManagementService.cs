using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Features.ComponentC.DTOs;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentC.Models;

namespace RescueSriLanka.Api.Features.ComponentC.Services;

public interface IResourceManagementService
{
    Task<IReadOnlyList<Shelter>> GetSheltersAsync(CancellationToken cancellationToken);

    Task<Shelter> CreateShelterAsync(CreateShelterRequest request, CancellationToken cancellationToken);

    Task<Shelter?> UpdateShelterAsync(Guid id, UpdateShelterRequest request, CancellationToken cancellationToken);

    Task<bool> DeleteShelterAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<MedicalSupply>> GetMedicalSuppliesAsync(CancellationToken cancellationToken);

    Task<MedicalSupply> CreateMedicalSupplyAsync(CreateMedicalSupplyRequest request, CancellationToken cancellationToken);

    Task<MedicalSupply?> UpdateMedicalSupplyAsync(Guid id, UpdateMedicalSupplyRequest request, CancellationToken cancellationToken);

    Task<bool> DeleteMedicalSupplyAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<FoodWaterStock>> GetFoodWaterStockAsync(CancellationToken cancellationToken);

    Task<FoodWaterStock> CreateFoodWaterStockAsync(CreateFoodWaterStockRequest request, CancellationToken cancellationToken);

    Task<FoodWaterStock?> UpdateFoodWaterStockAsync(Guid id, UpdateFoodWaterStockRequest request, CancellationToken cancellationToken);

    Task<bool> DeleteFoodWaterStockAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ResourceAlertResponse>> GetLowStockAlertsAsync(CancellationToken cancellationToken);

    Task<ResourceAllocationResponse> AllocateAsync(AllocateResourceRequest request, CancellationToken cancellationToken);

    Task<ResourceAllocationResponse> MatchAndAllocateAsync(
        MatchResourceRequest request,
        CancellationToken cancellationToken);

    Task<ResourceAllocationResponse?> ReleaseAsync(Guid allocationId, CancellationToken cancellationToken);

    Task<HelpRequestResponse> CreateHelpRequestAsync(CreateHelpRequestRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<HelpRequestResponse>> GetHelpRequestsAsync(CancellationToken cancellationToken);

    Task<DonationResponse> CreateDonationAsync(CreateDonationRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<DonationResponse>> GetDonationsAsync(CancellationToken cancellationToken);
}

public class ResourceManagementService(AppDbContext dbContext) : IResourceManagementService
{
    public async Task<IReadOnlyList<Shelter>> GetSheltersAsync(CancellationToken cancellationToken) =>
        await dbContext.Shelters
            .AsNoTracking()
            .Where(shelter => shelter.IsActive)
            .OrderBy(shelter => shelter.Name)
            .ToListAsync(cancellationToken);

    public async Task<Shelter> CreateShelterAsync(CreateShelterRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Address))
        {
            throw new ArgumentException("Shelter name and address are required.");
        }

        if (request.Capacity < 0)
        {
            throw new ArgumentException("Shelter capacity cannot be negative.");
        }

        var shelter = new Shelter
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Address = request.Address.Trim(),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            Capacity = request.Capacity
        };

        dbContext.Shelters.Add(shelter);
        await dbContext.SaveChangesAsync(cancellationToken);
        return shelter;
    }

    public async Task<Shelter?> UpdateShelterAsync(Guid id, UpdateShelterRequest request, CancellationToken cancellationToken)
    {
        ValidateShelter(request.Name, request.Address, request.Capacity);
        var shelter = await dbContext.Shelters.SingleOrDefaultAsync(item => item.Id == id && item.IsActive, cancellationToken);
        if (shelter is null) return null;
        shelter.Name = request.Name.Trim();
        shelter.Address = request.Address.Trim();
        shelter.Latitude = request.Latitude;
        shelter.Longitude = request.Longitude;
        shelter.Capacity = request.Capacity;
        await dbContext.SaveChangesAsync(cancellationToken);
        return shelter;
    }

    public async Task<bool> DeleteShelterAsync(Guid id, CancellationToken cancellationToken) =>
        await SoftDeleteAsync(dbContext.Shelters, id, cancellationToken);

    public async Task<IReadOnlyList<MedicalSupply>> GetMedicalSuppliesAsync(CancellationToken cancellationToken) =>
        await dbContext.MedicalSupplies
            .AsNoTracking()
            .Where(supply => supply.IsActive)
            .OrderBy(supply => supply.Name)
            .ToListAsync(cancellationToken);

    public async Task<MedicalSupply> CreateMedicalSupplyAsync(
        CreateMedicalSupplyRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Unit))
        {
            throw new ArgumentException("Medical supply name and unit are required.");
        }

        if (request.QuantityOnHand < 0 || request.LowStockThreshold < 0)
        {
            throw new ArgumentException("Supply quantities cannot be negative.");
        }

        var supply = new MedicalSupply
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Unit = request.Unit.Trim(),
            QuantityOnHand = request.QuantityOnHand,
            LowStockThreshold = request.LowStockThreshold
        };

        dbContext.MedicalSupplies.Add(supply);
        await dbContext.SaveChangesAsync(cancellationToken);
        return supply;
    }

    public async Task<MedicalSupply?> UpdateMedicalSupplyAsync(Guid id, UpdateMedicalSupplyRequest request, CancellationToken cancellationToken)
    {
        ValidateSupply(request.Name, request.Unit, request.QuantityOnHand, request.LowStockThreshold);
        var supply = await dbContext.MedicalSupplies.SingleOrDefaultAsync(item => item.Id == id && item.IsActive, cancellationToken);
        if (supply is null) return null;
        supply.Name = request.Name.Trim();
        supply.Unit = request.Unit.Trim();
        supply.QuantityOnHand = request.QuantityOnHand;
        supply.LowStockThreshold = request.LowStockThreshold;
        supply.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return supply;
    }

    public async Task<bool> DeleteMedicalSupplyAsync(Guid id, CancellationToken cancellationToken) =>
        await SoftDeleteAsync(dbContext.MedicalSupplies, id, cancellationToken);

    public async Task<IReadOnlyList<FoodWaterStock>> GetFoodWaterStockAsync(CancellationToken cancellationToken) =>
        await dbContext.FoodWaterStocks
            .AsNoTracking()
            .Where(stock => stock.IsActive)
            .OrderBy(stock => stock.ItemName)
            .ToListAsync(cancellationToken);

    public async Task<FoodWaterStock> CreateFoodWaterStockAsync(
        CreateFoodWaterStockRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ItemName) || string.IsNullOrWhiteSpace(request.Unit))
        {
            throw new ArgumentException("Food/water item name and unit are required.");
        }

        if (request.QuantityOnHand < 0 || request.LowStockThreshold < 0)
        {
            throw new ArgumentException("Stock quantities cannot be negative.");
        }

        var stock = new FoodWaterStock
        {
            Id = Guid.NewGuid(),
            ItemName = request.ItemName.Trim(),
            Unit = request.Unit.Trim(),
            QuantityOnHand = request.QuantityOnHand,
            LowStockThreshold = request.LowStockThreshold
        };

        dbContext.FoodWaterStocks.Add(stock);
        await dbContext.SaveChangesAsync(cancellationToken);
        return stock;
    }

    public async Task<FoodWaterStock?> UpdateFoodWaterStockAsync(Guid id, UpdateFoodWaterStockRequest request, CancellationToken cancellationToken)
    {
        ValidateSupply(request.ItemName, request.Unit, request.QuantityOnHand, request.LowStockThreshold);
        var stock = await dbContext.FoodWaterStocks.SingleOrDefaultAsync(item => item.Id == id && item.IsActive, cancellationToken);
        if (stock is null) return null;
        stock.ItemName = request.ItemName.Trim();
        stock.Unit = request.Unit.Trim();
        stock.QuantityOnHand = request.QuantityOnHand;
        stock.LowStockThreshold = request.LowStockThreshold;
        stock.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return stock;
    }

    public async Task<bool> DeleteFoodWaterStockAsync(Guid id, CancellationToken cancellationToken) =>
        await SoftDeleteAsync(dbContext.FoodWaterStocks, id, cancellationToken);

    private static void ValidateShelter(string name, string address, int capacity)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("Shelter name and address are required.");
        if (capacity < 0) throw new ArgumentException("Shelter capacity cannot be negative.");
    }

    private static void ValidateSupply(string name, string unit, decimal quantity, decimal threshold)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(unit))
            throw new ArgumentException("Resource name and unit are required.");
        if (quantity < 0 || threshold < 0) throw new ArgumentException("Resource quantities cannot be negative.");
    }

    private async Task<bool> SoftDeleteAsync<TEntity>(DbSet<TEntity> resources, Guid id, CancellationToken cancellationToken)
        where TEntity : class
    {
        var resource = await resources.FindAsync([id], cancellationToken);
        if (resource is null) return false;
        var activeProperty = typeof(TEntity).GetProperty(nameof(Shelter.IsActive));
        if (activeProperty is null) return false;
        activeProperty.SetValue(resource, false);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<ResourceAlertResponse>> GetLowStockAlertsAsync(CancellationToken cancellationToken)
    {
        var supplyAlerts = await dbContext.MedicalSupplies
            .AsNoTracking()
            .Where(supply => supply.IsActive && supply.QuantityOnHand <= supply.LowStockThreshold)
            .Select(supply => new ResourceAlertResponse(
                "MedicalSupply",
                supply.Id,
                supply.Name,
                supply.QuantityOnHand,
                supply.LowStockThreshold,
                supply.Unit))
            .ToListAsync(cancellationToken);

        var stockAlerts = await dbContext.FoodWaterStocks
            .AsNoTracking()
            .Where(stock => stock.IsActive && stock.QuantityOnHand <= stock.LowStockThreshold)
            .Select(stock => new ResourceAlertResponse(
                "FoodWaterStock",
                stock.Id,
                stock.ItemName,
                stock.QuantityOnHand,
                stock.LowStockThreshold,
                stock.Unit))
            .ToListAsync(cancellationToken);

        return supplyAlerts.Concat(stockAlerts).ToList();
    }

    public async Task<HelpRequestResponse> CreateHelpRequestAsync(
        CreateHelpRequestRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RequesterName) ||
            string.IsNullOrWhiteSpace(request.ContactNumber) ||
            string.IsNullOrWhiteSpace(request.NeedType) ||
            string.IsNullOrWhiteSpace(request.Description))
        {
            throw new ArgumentException("Name, contact number, need type, and description are required.");
        }

        var helpRequest = new HelpRequest
        {
            Id = Guid.NewGuid(),
            RequesterName = request.RequesterName.Trim(),
            ContactNumber = request.ContactNumber.Trim(),
            NeedType = request.NeedType.Trim(),
            Description = request.Description.Trim(),
            Latitude = request.Latitude is null ? null : (double?)request.Latitude,
            Longitude = request.Longitude is null ? null : (double?)request.Longitude
        };

        dbContext.ResourceHelpRequests.Add(helpRequest);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(helpRequest);
    }

    public async Task<IReadOnlyList<HelpRequestResponse>> GetHelpRequestsAsync(CancellationToken cancellationToken) =>
        (await dbContext.ResourceHelpRequests
            .AsNoTracking()
            .OrderByDescending(request => request.CreatedAtUtc)
            .ToListAsync(cancellationToken))
        .Select(ToResponse)
        .ToList();

    public async Task<DonationResponse> CreateDonationAsync(
        CreateDonationRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DonorName) ||
            string.IsNullOrWhiteSpace(request.ContactNumber) ||
            string.IsNullOrWhiteSpace(request.DonationType) ||
            string.IsNullOrWhiteSpace(request.Unit) ||
            request.Quantity <= 0)
        {
            throw new ArgumentException("Name, contact number, donation type, unit, and a positive quantity are required.");
        }

        var donation = new Donation
        {
            Id = Guid.NewGuid(),
            DonorName = request.DonorName.Trim(),
            ContactNumber = request.ContactNumber.Trim(),
            DonationType = request.DonationType.Trim(),
            Quantity = request.Quantity,
            Unit = request.Unit.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()
        };

        dbContext.Donations.Add(donation);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(donation);
    }

    public async Task<IReadOnlyList<DonationResponse>> GetDonationsAsync(CancellationToken cancellationToken) =>
        (await dbContext.Donations
            .AsNoTracking()
            .OrderByDescending(donation => donation.CreatedAtUtc)
            .ToListAsync(cancellationToken))
        .Select(ToResponse)
        .ToList();

    private static HelpRequestResponse ToResponse(HelpRequest request) => new(
        request.Id,
        request.RequesterName,
        request.ContactNumber,
        request.NeedType,
        request.Description,
        request.Latitude is null ? null : (decimal?)request.Latitude,
        request.Longitude is null ? null : (decimal?)request.Longitude,
        request.Status,
        request.CreatedAtUtc);

    private static DonationResponse ToResponse(Donation donation) => new(
        donation.Id,
        donation.DonorName,
        donation.ContactNumber,
        donation.DonationType,
        donation.Quantity,
        donation.Unit,
        donation.Notes,
        donation.Status,
        donation.CreatedAtUtc);

    public async Task<ResourceAllocationResponse> AllocateAsync(
        AllocateResourceRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Quantity <= 0)
        {
            throw new ArgumentException("Allocation quantity must be greater than zero.");
        }

        var resourceType = request.ResourceType.Trim();
        var allocation = new ResourceAllocation
        {
            Id = Guid.NewGuid(),
            ResourceType = resourceType,
            ResourceId = request.ResourceId,
            Quantity = request.Quantity,
            HelpRequestId = request.HelpRequestId,
            IncidentId = request.IncidentId
        };

        switch (resourceType.ToLowerInvariant())
        {
            case "shelter":
                await AllocateShelterAsync(request, cancellationToken);
                break;
            case "medicalsupply":
                await AllocateMedicalSupplyAsync(request, cancellationToken);
                break;
            case "foodwaterstock":
                await AllocateFoodWaterStockAsync(request, cancellationToken);
                break;
            default:
                throw new ArgumentException("Resource type must be Shelter, MedicalSupply, or FoodWaterStock.");
        }

        dbContext.ResourceAllocations.Add(allocation);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(allocation);
    }

    public async Task<ResourceAllocationResponse> MatchAndAllocateAsync(
        MatchResourceRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Quantity <= 0)
        {
            throw new ArgumentException("Allocation quantity must be greater than zero.");
        }

        var resourceType = request.ResourceType.Trim();
        Guid? resourceId = resourceType.ToLowerInvariant() switch
        {
            "shelter" => await dbContext.Shelters
                .Where(shelter => shelter.IsActive && shelter.Capacity - shelter.OccupiedCapacity >= request.Quantity)
                .OrderByDescending(shelter => shelter.Capacity - shelter.OccupiedCapacity)
                .Select(shelter => (Guid?)shelter.Id)
                .FirstOrDefaultAsync(cancellationToken),
            "medicalsupply" => await dbContext.MedicalSupplies
                .Where(supply => supply.IsActive && supply.QuantityOnHand >= request.Quantity)
                .OrderByDescending(supply => supply.QuantityOnHand)
                .Select(supply => (Guid?)supply.Id)
                .FirstOrDefaultAsync(cancellationToken),
            "foodwaterstock" => await dbContext.FoodWaterStocks
                .Where(stock => stock.IsActive && stock.QuantityOnHand >= request.Quantity)
                .OrderByDescending(stock => stock.QuantityOnHand)
                .Select(stock => (Guid?)stock.Id)
                .FirstOrDefaultAsync(cancellationToken),
            _ => throw new ArgumentException("Resource type must be Shelter, MedicalSupply, or FoodWaterStock.")
        };

        if (resourceId is null)
        {
            throw new InvalidOperationException("No active resource has enough availability.");
        }

        return await AllocateAsync(
            new AllocateResourceRequest(
                resourceType,
                resourceId.Value,
                request.Quantity,
                request.HelpRequestId,
                request.IncidentId),
            cancellationToken);
    }

    public async Task<ResourceAllocationResponse?> ReleaseAsync(
        Guid allocationId,
        CancellationToken cancellationToken)
    {
        var allocation = await dbContext.ResourceAllocations
            .SingleOrDefaultAsync(item => item.Id == allocationId, cancellationToken);

        if (allocation is null || !string.Equals(allocation.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        switch (allocation.ResourceType.ToLowerInvariant())
        {
            case "shelter":
                var shelter = await dbContext.Shelters.SingleAsync(item => item.Id == allocation.ResourceId, cancellationToken);
                shelter.OccupiedCapacity = Math.Max(0, shelter.OccupiedCapacity - (int)allocation.Quantity);
                break;
            case "medicalsupply":
                var supply = await dbContext.MedicalSupplies.SingleAsync(item => item.Id == allocation.ResourceId, cancellationToken);
                supply.QuantityOnHand += (int)allocation.Quantity;
                supply.UpdatedAtUtc = DateTime.UtcNow;
                break;
            case "foodwaterstock":
                var stock = await dbContext.FoodWaterStocks.SingleAsync(item => item.Id == allocation.ResourceId, cancellationToken);
                stock.QuantityOnHand += allocation.Quantity;
                stock.UpdatedAtUtc = DateTime.UtcNow;
                break;
        }

        allocation.Status = "Released";
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(allocation);
    }

    private async Task AllocateShelterAsync(AllocateResourceRequest request, CancellationToken cancellationToken)
    {
        if (request.Quantity != decimal.Truncate(request.Quantity))
        {
            throw new ArgumentException("Shelter allocation quantity must be a whole number.");
        }

        var shelter = await dbContext.Shelters.SingleOrDefaultAsync(
            item => item.Id == request.ResourceId && item.IsActive,
            cancellationToken);

        if (shelter is null)
        {
            throw new KeyNotFoundException("Shelter was not found.");
        }

        if (shelter.AvailableCapacity < request.Quantity)
        {
            throw new InvalidOperationException("Shelter does not have enough available capacity.");
        }

        shelter.OccupiedCapacity += (int)request.Quantity;
    }

    private async Task AllocateMedicalSupplyAsync(AllocateResourceRequest request, CancellationToken cancellationToken)
    {
        if (request.Quantity != decimal.Truncate(request.Quantity))
        {
            throw new ArgumentException("Medical supply allocation quantity must be a whole number.");
        }

        var supply = await dbContext.MedicalSupplies.SingleOrDefaultAsync(
            item => item.Id == request.ResourceId && item.IsActive,
            cancellationToken);

        if (supply is null)
        {
            throw new KeyNotFoundException("Medical supply was not found.");
        }

        if (supply.QuantityOnHand < request.Quantity)
        {
            throw new InvalidOperationException("Medical supply stock is insufficient.");
        }

        supply.QuantityOnHand -= (int)request.Quantity;
        supply.UpdatedAtUtc = DateTime.UtcNow;
    }

    private async Task AllocateFoodWaterStockAsync(AllocateResourceRequest request, CancellationToken cancellationToken)
    {
        var stock = await dbContext.FoodWaterStocks.SingleOrDefaultAsync(
            item => item.Id == request.ResourceId && item.IsActive,
            cancellationToken);

        if (stock is null)
        {
            throw new KeyNotFoundException("Food/water stock item was not found.");
        }

        if (stock.QuantityOnHand < request.Quantity)
        {
            throw new InvalidOperationException("Food/water stock is insufficient.");
        }

        stock.QuantityOnHand -= request.Quantity;
        stock.UpdatedAtUtc = DateTime.UtcNow;
    }

    private static ResourceAllocationResponse ToResponse(ResourceAllocation allocation) =>
        new(
            allocation.Id,
            allocation.ResourceType,
            allocation.ResourceId,
            allocation.Quantity,
            allocation.Status,
            allocation.HelpRequestId,
            allocation.IncidentId,
            allocation.AllocatedAtUtc);
}
