using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Features.ComponentC.DTOs;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Features.ComponentC.Models;
using RescueSriLanka.Api.Features.ComponentA.Services.Notifications;
using RescueSriLanka.Api.Services.Email;
using Microsoft.Extensions.Options;

namespace RescueSriLanka.Api.Features.ComponentC.Services;

public interface IResourceManagementService
{
    Task<IReadOnlyList<MedicalSupply>> GetMedicalSuppliesAsync(CancellationToken cancellationToken);

    Task<MedicalSupply> CreateMedicalSupplyAsync(CreateMedicalSupplyRequest request, CancellationToken cancellationToken);

    Task<MedicalSupply?> UpdateMedicalSupplyAsync(Guid id, UpdateMedicalSupplyRequest request, CancellationToken cancellationToken);

    Task<bool> DeleteMedicalSupplyAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<FoodWaterStock>> GetFoodWaterStockAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ManagedSupply>> GetManagedSuppliesAsync(CancellationToken cancellationToken);

    Task<ManagedSupply> CreateManagedSupplyAsync(CreateManagedSupplyRequest request, CancellationToken cancellationToken);

    Task<ManagedSupply?> UpdateManagedSupplyAsync(Guid id, CreateManagedSupplyRequest request, CancellationToken cancellationToken);

    Task<bool> DeleteManagedSupplyAsync(Guid id, CancellationToken cancellationToken);

    Task<FoodWaterStock> CreateFoodWaterStockAsync(CreateFoodWaterStockRequest request, CancellationToken cancellationToken);

    Task<FoodWaterStock?> UpdateFoodWaterStockAsync(Guid id, UpdateFoodWaterStockRequest request, CancellationToken cancellationToken);

    Task<bool> DeleteFoodWaterStockAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ResourceAlertResponse>> GetLowStockAlertsAsync(CancellationToken cancellationToken);

    Task<ResourceAllocationResponse> AllocateAsync(AllocateResourceRequest request, CancellationToken cancellationToken);

    Task<ResourceAllocationResponse> MatchAndAllocateAsync(
        MatchResourceRequest request,
        CancellationToken cancellationToken);

    Task<ResourceAllocationResponse?> ReleaseAsync(Guid allocationId, CancellationToken cancellationToken);

    Task<HelpRequestResponse> CreateHelpRequestAsync(CreateHelpRequestRequest request, Guid? userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<HelpRequestResponse>> CreateHelpRequestsBatchAsync(CreateHelpRequestsBatchRequest request, Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<HelpRequestResponse>> GetHelpRequestsAsync(Guid userId, bool includeAll, CancellationToken cancellationToken);

    Task<HelpRequestResponse?> UpdateHelpRequestStatusAsync(
        Guid id,
        UpdateHelpRequestStatusRequest request,
        CancellationToken cancellationToken);

    Task<DonationResponse> CreateDonationAsync(CreateDonationRequest request, Guid? userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<DonationResponse>> CreateDonationsBatchAsync(CreateDonationsBatchRequest request, Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<DonationResponse>> GetDonationsAsync(Guid userId, bool includeAll, CancellationToken cancellationToken);

    Task<IReadOnlyList<DonatedSupplyResponse>> GetDonatedSuppliesAsync(CancellationToken cancellationToken);

    Task<DonationResponse?> UpdateDonationStatusAsync(
        Guid id,
        UpdateHelpRequestStatusRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DonationResponse>?> UpdateDonationBatchStatusAsync(
        Guid submissionId,
        UpdateHelpRequestStatusRequest request,
        CancellationToken cancellationToken);

}

public class ResourceManagementService(
    AppDbContext dbContext,
    IResourceEmailQueue? emailQueue = null,
    IOptions<EmailOptions>? emailOptions = null,
    ILogger<ResourceManagementService>? logger = null) : IResourceManagementService
{
    private static readonly string[] ResourceCategories =
        ["Food", "Water", "Medical", "Sanitary products", "Hygiene items", "Other"];

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

    public async Task<IReadOnlyList<ManagedSupply>> GetManagedSuppliesAsync(CancellationToken cancellationToken) =>
        await dbContext.ManagedSupplies
            .AsNoTracking()
            .Where(supply => supply.IsActive)
            .OrderBy(supply => supply.Category)
            .ThenBy(supply => supply.Name)
            .ToListAsync(cancellationToken);

    public async Task<ManagedSupply> CreateManagedSupplyAsync(
        CreateManagedSupplyRequest request,
        CancellationToken cancellationToken)
    {
        var allowedCategories = new[] { "Food", "Water", "Medical", "Sanitary products", "Hygiene items", "Other" };
        var category = allowedCategories.FirstOrDefault(value =>
            string.Equals(value, request.Category.Trim(), StringComparison.OrdinalIgnoreCase));
        if (category is null)
        {
            throw new ArgumentException("Select a valid supply category.");
        }

        ValidateSupply(request.Name, request.Unit, request.QuantityOnHand, request.LowStockThreshold);
        var supply = new ManagedSupply
        {
            Id = Guid.NewGuid(),
            Category = category,
            Name = request.Name.Trim(),
            Unit = request.Unit.Trim(),
            QuantityOnHand = request.QuantityOnHand,
            LowStockThreshold = request.LowStockThreshold
        };

        dbContext.ManagedSupplies.Add(supply);
        await dbContext.SaveChangesAsync(cancellationToken);
        return supply;
    }

    public async Task<ManagedSupply?> UpdateManagedSupplyAsync(
        Guid id,
        CreateManagedSupplyRequest request,
        CancellationToken cancellationToken)
    {
        var allowedCategories = new[] { "Food", "Water", "Medical", "Sanitary products", "Hygiene items", "Other" };
        var category = allowedCategories.FirstOrDefault(value =>
            string.Equals(value, request.Category.Trim(), StringComparison.OrdinalIgnoreCase));
        if (category is null) throw new ArgumentException("Select a valid supply category.");

        ValidateSupply(request.Name, request.Unit, request.QuantityOnHand, request.LowStockThreshold);
        var supply = await dbContext.ManagedSupplies.SingleOrDefaultAsync(
            item => item.Id == id && item.IsActive, cancellationToken);
        if (supply is null) return null;

        supply.Category = category;
        supply.Name = request.Name.Trim();
        supply.Unit = request.Unit.Trim();
        supply.QuantityOnHand = request.QuantityOnHand;
        supply.LowStockThreshold = request.LowStockThreshold;
        supply.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return supply;
    }

    public Task<bool> DeleteManagedSupplyAsync(Guid id, CancellationToken cancellationToken) =>
        SoftDeleteAsync(dbContext.ManagedSupplies, id, cancellationToken);

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
        var activeProperty = typeof(TEntity).GetProperty(nameof(MedicalSupply.IsActive));
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

        var managedSupplyAlerts = await dbContext.ManagedSupplies
            .AsNoTracking()
            .Where(supply => supply.IsActive && supply.QuantityOnHand <= supply.LowStockThreshold)
            .Select(supply => new ResourceAlertResponse(
                "ManagedSupply",
                supply.Id,
                $"{supply.Category}: {supply.Name}",
                supply.QuantityOnHand,
                supply.LowStockThreshold,
                supply.Unit))
            .ToListAsync(cancellationToken);

        return [.. supplyAlerts.Concat(stockAlerts).Concat(managedSupplyAlerts)];
    }

    public async Task<HelpRequestResponse> CreateHelpRequestAsync(
        CreateHelpRequestRequest request,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        var requesterName = request.RequesterName.Trim();
        var contactNumber = request.ContactNumber.Trim();
        var district = (string?)null;
        if (userId is Guid linkedUserId)
        {
            var user = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(
                candidate => candidate.Id == linkedUserId && candidate.IsActive,
                cancellationToken);
            if (user is not null)
            {
                requesterName = user.FullName;
                contactNumber = user.PhoneNumber?.Trim() ?? string.Empty;
                district = user.District;
            }
        }

        if (string.IsNullOrWhiteSpace(requesterName) ||
            string.IsNullOrWhiteSpace(contactNumber) ||
            string.IsNullOrWhiteSpace(request.NeedType) ||
            string.IsNullOrWhiteSpace(request.Description))
        {
            throw new ArgumentException("Name, contact number, need type, and description are required.");
        }

        var helpRequest = new HelpRequest
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RequesterName = requesterName,
            ContactNumber = contactNumber,
            NeedType = request.NeedType.Trim(),
            Description = request.Description.Trim(),
            Latitude = request.Latitude is null ? null : (double?)request.Latitude,
            Longitude = request.Longitude is null ? null : (double?)request.Longitude
        };

        dbContext.ResourceHelpRequests.Add(helpRequest);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(helpRequest, district);
    }

    public async Task<IReadOnlyList<HelpRequestResponse>> CreateHelpRequestsBatchAsync(
        CreateHelpRequestsBatchRequest request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        ValidateSubmissionItems(request.Items);
        var user = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.Id == userId && candidate.IsActive,
            cancellationToken)
            ?? throw new ArgumentException("An active account is required to submit a resource request.");
        if (string.IsNullOrWhiteSpace(user.PhoneNumber))
        {
            throw new ArgumentException("Add a phone number to your profile before submitting a request.");
        }

        var requests = request.Items.Select(item => new HelpRequest
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            RequesterName = user.FullName,
            ContactNumber = user.PhoneNumber.Trim(),
            NeedType = NormalizeResourceCategory(item.Category),
            Description = $"{item.ItemName.Trim()} - {item.Quantity} {item.Unit.Trim()}"
        }).ToArray();

        dbContext.ResourceHelpRequests.AddRange(requests);
        await dbContext.SaveChangesAsync(cancellationToken);
        return [.. requests.Select(resourceRequest => ToResponse(resourceRequest, user.District))];
    }

    public async Task<IReadOnlyList<HelpRequestResponse>> GetHelpRequestsAsync(
        Guid userId,
        bool includeAll,
        CancellationToken cancellationToken)
    {
        var query = dbContext.ResourceHelpRequests
            .AsNoTracking()
            .AsQueryable();
        if (!includeAll) query = query.Where(request => request.UserId == userId);
        var requests = await query
            .OrderByDescending(request => request.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        var userIds = requests.Where(request => request.UserId is not null).Select(request => request.UserId!.Value).ToArray();
        var districts = await dbContext.Users.AsNoTracking().Where(user => userIds.Contains(user.Id)).ToDictionaryAsync(user => user.Id, user => user.District, cancellationToken);
        return [.. requests.Select(request => ToResponse(request, request.UserId is Guid id && districts.TryGetValue(id, out var district) ? district : null))];
    }

    public async Task<HelpRequestResponse?> UpdateHelpRequestStatusAsync(
        Guid id,
        UpdateHelpRequestStatusRequest request,
        CancellationToken cancellationToken)
    {
        var status = request.Status.Trim();
        if (status is not ("Accepted" or "Rejected"))
        {
            throw new ArgumentException("Request status must be Accepted or Rejected.");
        }

        var helpRequest = await dbContext.ResourceHelpRequests
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (helpRequest is null) return null;
        if (helpRequest.Status == "Fulfilled")
        {
            throw new InvalidOperationException("A fulfilled request cannot be changed.");
        }

        helpRequest.Status = status;
        await dbContext.SaveChangesAsync(cancellationToken);
        if (status == "Accepted")
        {
            var user = await FindUserForHelpRequestAsync(helpRequest, cancellationToken);
            if (user is not null)
            {
                QueueAcceptedEmail(user, "help request", helpRequest.NeedType, helpRequest.Description);
            }
        }
        var district = helpRequest.UserId is Guid userId
            ? await dbContext.Users.AsNoTracking().Where(user => user.Id == userId).Select(user => user.District).SingleOrDefaultAsync(cancellationToken)
            : null;
        return ToResponse(helpRequest, district);
    }

    public async Task<DonationResponse> CreateDonationAsync(
        CreateDonationRequest request,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        var donorName = request.DonorName.Trim();
        var contactNumber = request.ContactNumber.Trim();
        var district = (string?)null;
        if (userId is Guid linkedUserId)
        {
            var user = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(
                candidate => candidate.Id == linkedUserId && candidate.IsActive,
                cancellationToken);
            if (user is not null)
            {
                donorName = user.FullName;
                contactNumber = user.PhoneNumber?.Trim() ?? string.Empty;
                district = user.District;
            }
        }

        if (string.IsNullOrWhiteSpace(donorName) ||
            string.IsNullOrWhiteSpace(contactNumber) ||
            string.IsNullOrWhiteSpace(request.DonationType) ||
            string.IsNullOrWhiteSpace(request.Unit) ||
            request.Quantity <= 0)
        {
            throw new ArgumentException("Name, contact number, donation type, unit, and a positive quantity are required.");
        }

        var donation = new Donation
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DonorName = donorName,
            ContactNumber = contactNumber,
            DonationType = request.DonationType.Trim(),
            Quantity = request.Quantity,
            Unit = request.Unit.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()
        };

        dbContext.Donations.Add(donation);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(donation, district);
    }

    public async Task<IReadOnlyList<DonationResponse>> CreateDonationsBatchAsync(
        CreateDonationsBatchRequest request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        ValidateSubmissionItems(request.Items);
        var user = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.Id == userId && candidate.IsActive,
            cancellationToken)
            ?? throw new ArgumentException("An active account is required to submit donations.");
        if (string.IsNullOrWhiteSpace(user.PhoneNumber))
        {
            throw new ArgumentException("Add a phone number to your profile before donating.");
        }

        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        var submissionId = Guid.NewGuid();
        var donations = request.Items.Select(item => new Donation
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            SubmissionId = submissionId,
            DonorName = user.FullName,
            ContactNumber = user.PhoneNumber.Trim(),
            DonationType = $"{NormalizeResourceCategory(item.Category)}: {item.ItemName.Trim()}",
            Quantity = item.Quantity,
            Unit = item.Unit.Trim(),
            Notes = notes
        }).ToArray();

        dbContext.Donations.AddRange(donations);
        await dbContext.SaveChangesAsync(cancellationToken);
        return [.. donations.Select(donation => ToResponse(donation, user.District))];
    }

    private static void ValidateSubmissionItems(IReadOnlyCollection<ResourceSubmissionItem> items)
    {
        if (items.Count is < 1 or > 20)
        {
            throw new ArgumentException("Submit between 1 and 20 items at a time.");
        }

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Category) ||
                string.IsNullOrWhiteSpace(item.ItemName) ||
                string.IsNullOrWhiteSpace(item.Unit) ||
                item.ItemName.Trim().Length > 100 ||
                item.Unit.Trim().Length > 40 ||
                item.Quantity <= 0)
            {
                throw new ArgumentException("Each item needs a category, name, unit, and positive quantity.");
            }

            _ = NormalizeResourceCategory(item.Category);
        }
    }

    private static string NormalizeResourceCategory(string category) =>
        ResourceCategories.FirstOrDefault(value =>
            string.Equals(value, category.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException("Select a valid resource category.");

    public async Task<IReadOnlyList<DonationResponse>> GetDonationsAsync(
        Guid userId,
        bool includeAll,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Donations
            .AsNoTracking()
            .AsQueryable();
        if (!includeAll) query = query.Where(donation => donation.UserId == userId);
        var donations = await query
            .OrderByDescending(donation => donation.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        var userIds = donations.Where(donation => donation.UserId is not null).Select(donation => donation.UserId!.Value).ToArray();
        var districts = await dbContext.Users.AsNoTracking().Where(user => userIds.Contains(user.Id)).ToDictionaryAsync(user => user.Id, user => user.District, cancellationToken);
        return [.. donations.Select(donation => ToResponse(donation, donation.UserId is Guid id && districts.TryGetValue(id, out var district) ? district : null))];
    }

    public async Task<IReadOnlyList<DonatedSupplyResponse>> GetDonatedSuppliesAsync(CancellationToken cancellationToken) =>
        [.. (await dbContext.DonatedSupplies
            .AsNoTracking()
            .Where(supply => supply.IsActive && supply.QuantityOnHand > 0)
            .OrderBy(supply => supply.Name)
            .ThenBy(supply => supply.UpdatedAtUtc)
            .ToListAsync(cancellationToken))
        .Select(supply => new DonatedSupplyResponse(
            supply.Id,
            supply.DonationId,
            supply.Name,
            supply.DonorName,
            supply.QuantityOnHand,
            supply.Unit,
            supply.Notes,
            supply.UpdatedAtUtc))];

    public async Task<DonationResponse?> UpdateDonationStatusAsync(
        Guid id,
        UpdateHelpRequestStatusRequest request,
        CancellationToken cancellationToken)
    {
        var status = request.Status.Trim();
        if (status is not ("Accepted" or "Rejected"))
        {
            throw new ArgumentException("Donation status must be Accepted or Rejected.");
        }

        var donation = await dbContext.Donations
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (donation is null) return null;
        if (donation.Status == "Accepted")
        {
            throw new InvalidOperationException("An accepted donation cannot be changed.");
        }

        donation.Status = status;
        if (status == "Accepted")
        {
            await AddDonationToManagedStockAsync(donation, cancellationToken);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        if (status == "Accepted")
        {
            var user = await FindUserForDonationAsync(donation, cancellationToken);
            if (user is not null)
            {
                QueueAcceptedEmail(user, "donation", donation.DonationType, $"{donation.Quantity} {donation.Unit}");
            }
        }
        var district = donation.UserId is Guid userId
            ? await dbContext.Users.AsNoTracking().Where(user => user.Id == userId).Select(user => user.District).SingleOrDefaultAsync(cancellationToken)
            : null;
        return ToResponse(donation, district);
    }

    public async Task<IReadOnlyList<DonationResponse>?> UpdateDonationBatchStatusAsync(
        Guid submissionId,
        UpdateHelpRequestStatusRequest request,
        CancellationToken cancellationToken)
    {
        var status = request.Status.Trim();
        if (status is not ("Accepted" or "Rejected"))
        {
            throw new ArgumentException("Donation status must be Accepted or Rejected.");
        }

        var donations = await dbContext.Donations
            .Where(item => item.SubmissionId == submissionId)
            .ToListAsync(cancellationToken);
        if (donations.Count == 0) return null;
        if (donations.Any(item => item.Status == "Accepted"))
        {
            throw new InvalidOperationException("An accepted donation submission cannot be changed.");
        }

        foreach (var donation in donations)
        {
            donation.Status = status;
            if (status == "Accepted")
            {
                await AddDonationToManagedStockAsync(donation, cancellationToken);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        if (status == "Accepted")
        {
            var user = await FindUserForDonationAsync(donations[0], cancellationToken);
            if (user is not null)
            {
                QueueAcceptedEmail(
                    user,
                    "donation",
                    string.Join(", ", donations.Select(item => item.DonationType)),
                    string.Join("; ", donations.Select(item => $"{item.Quantity} {item.Unit}")));
            }
        }

        var userId = donations[0].UserId;
        var district = userId is Guid id
            ? await dbContext.Users.AsNoTracking().Where(user => user.Id == id).Select(user => user.District).SingleOrDefaultAsync(cancellationToken)
            : null;
        return [.. donations.Select(donation => ToResponse(donation, district))];
    }

    private async Task AddDonationToManagedStockAsync(
        Donation donation,
        CancellationToken cancellationToken)
    {
        var (category, name) = DonationCategoryAndName(donation.DonationType);
        var unit = donation.Unit.Trim();
        var existing = await dbContext.ManagedSupplies.FirstOrDefaultAsync(supply =>
            supply.IsActive &&
            supply.Category == category &&
            supply.Name.ToLower() == name.ToLower() &&
            supply.Unit.ToLower() == unit.ToLower(),
            cancellationToken);

        if (existing is null)
        {
            dbContext.ManagedSupplies.Add(new ManagedSupply
            {
                Id = Guid.NewGuid(),
                Category = category,
                Name = name,
                Unit = unit,
                QuantityOnHand = donation.Quantity,
                LowStockThreshold = 0
            });
            return;
        }

        existing.QuantityOnHand += donation.Quantity;
        existing.UpdatedAtUtc = DateTime.UtcNow;
    }

    private static (string Category, string Name) DonationCategoryAndName(string donationType)
    {
        var separator = donationType.IndexOf(':');
        if (separator > 0)
        {
            var categoryText = donationType[..separator].Trim();
            var category = ResourceCategories.FirstOrDefault(value =>
                string.Equals(value, categoryText, StringComparison.OrdinalIgnoreCase));
            if (category is not null)
            {
                return (category, donationType[(separator + 1)..].Trim());
            }
        }

        return ("Other", donationType.Trim());
    }

    private static HelpRequestResponse ToResponse(HelpRequest request, string? district) => new(
        request.Id,
        request.UserId,
        request.RequesterName,
        request.ContactNumber,
        request.NeedType,
        request.Description,
        request.Latitude is null ? null : (decimal?)request.Latitude,
        request.Longitude is null ? null : (decimal?)request.Longitude,
        request.Status,
        request.CreatedAtUtc,
        district);

    private static DonationResponse ToResponse(Donation donation, string? district) => new(
        donation.Id,
        donation.UserId,
        donation.SubmissionId,
        donation.DonorName,
        donation.ContactNumber,
        donation.DonationType,
        donation.Quantity,
        donation.Unit,
        donation.Notes,
        donation.Status,
        donation.CreatedAtUtc,
        district);

    private async Task<User?> FindUserForHelpRequestAsync(HelpRequest helpRequest, CancellationToken cancellationToken)
    {
        if (helpRequest.UserId is Guid id)
        {
            var user = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(candidate =>
                candidate.Id == id && candidate.IsActive,
                cancellationToken);
            if (user is not null) return user;
        }

        if (!string.IsNullOrWhiteSpace(helpRequest.ContactNumber))
        {
            var phone = helpRequest.ContactNumber.Trim();
            var user = await dbContext.Users.AsNoTracking().FirstOrDefaultAsync(candidate =>
                candidate.PhoneNumber == phone && candidate.IsActive,
                cancellationToken);
            if (user is not null) return user;
        }

        if (!string.IsNullOrWhiteSpace(helpRequest.RequesterName))
        {
            var name = helpRequest.RequesterName.Trim().ToLower();
            var user = await dbContext.Users.AsNoTracking().FirstOrDefaultAsync(candidate =>
                candidate.FullName.ToLower() == name && candidate.IsActive,
                cancellationToken);
            if (user is not null) return user;
        }

        return null;
    }

    private async Task<User?> FindUserForDonationAsync(Donation donation, CancellationToken cancellationToken)
    {
        if (donation.UserId is Guid id)
        {
            var user = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(candidate =>
                candidate.Id == id && candidate.IsActive,
                cancellationToken);
            if (user is not null) return user;
        }

        if (!string.IsNullOrWhiteSpace(donation.ContactNumber))
        {
            var phone = donation.ContactNumber.Trim();
            var user = await dbContext.Users.AsNoTracking().FirstOrDefaultAsync(candidate =>
                candidate.PhoneNumber == phone && candidate.IsActive,
                cancellationToken);
            if (user is not null) return user;
        }

        if (!string.IsNullOrWhiteSpace(donation.DonorName))
        {
            var name = donation.DonorName.Trim().ToLower();
            var user = await dbContext.Users.AsNoTracking().FirstOrDefaultAsync(candidate =>
                candidate.FullName.ToLower() == name && candidate.IsActive,
                cancellationToken);
            if (user is not null) return user;
        }

        return null;
    }

    private void QueueAcceptedEmail(
        User user,
        string itemType,
        string itemName,
        string details)
    {
        if (emailQueue is null || emailOptions?.Value.Enabled != true) return;
        if (!emailQueue.TryQueue(EmailTemplates.ResourceAccepted(
                user, itemType, itemName, details, user.District)))
        {
            logger?.LogWarning("Could not queue accepted {ItemType} email to {Email}.", itemType, user.Email);
        }
    }

    private void QueueDispatchedEmail(
        User user,
        string resourceType,
        decimal quantity)
    {
        if (emailQueue is null || emailOptions?.Value.Enabled != true) return;
        if (!emailQueue.TryQueue(EmailTemplates.ResourcesDispatched(
                user, resourceType, quantity, user.District)))
        {
            logger?.LogWarning("Could not queue dispatched-resources email to {Email}.", user.Email);
        }
    }

    public async Task<ResourceAllocationResponse> AllocateAsync(
        AllocateResourceRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Quantity <= 0)
        {
            throw new ArgumentException("Allocation quantity must be greater than zero.");
        }

        var resourceType = NormalizeResourceType(request.ResourceType);
        var allocation = new ResourceAllocation
        {
            Id = Guid.NewGuid(),
            ResourceType = resourceType,
            ResourceId = request.ResourceId != Guid.Empty ? request.ResourceId : Guid.NewGuid(),
            Quantity = request.Quantity,
            HelpRequestId = request.HelpRequestId,
            IncidentId = request.IncidentId
        };

        switch (resourceType.ToLowerInvariant())
        {
            case "medicalsupply":
                await AllocateMedicalSupplyAsync(request, cancellationToken);
                break;
            case "foodwaterstock":
                await AllocateFoodWaterStockAsync(request, cancellationToken);
                break;
            case "donatedsupply":
                await AllocateDonatedSupplyAsync(request, cancellationToken);
                break;
            case "managedsupply":
                await AllocateManagedSupplyAsync(request, cancellationToken);
                break;
            default:
                // Clothes, Other, or custom resources do not require stock deduction.
                break;
        }

        dbContext.ResourceAllocations.Add(allocation);
        User? dispatchRecipient = null;
        if (request.HelpRequestId is Guid helpRequestId)
        {
            var helpRequest = await dbContext.ResourceHelpRequests
                .SingleOrDefaultAsync(item => item.Id == helpRequestId, cancellationToken)
                ?? throw new KeyNotFoundException("Help request was not found.");
            helpRequest.Status = "Fulfilled";

            dispatchRecipient = await FindUserForHelpRequestAsync(helpRequest, cancellationToken);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        if (dispatchRecipient is not null)
        {
            QueueDispatchedEmail(dispatchRecipient, resourceType, request.Quantity);
        }
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

        var resourceType = NormalizeResourceType(request.ResourceType);
        Guid resourceId = resourceType.ToLowerInvariant() switch
        {
            "medicalsupply" => await dbContext.MedicalSupplies
                .Where(supply => supply.IsActive && supply.QuantityOnHand >= request.Quantity)
                .OrderByDescending(supply => supply.QuantityOnHand)
                .Select(supply => (Guid?)supply.Id)
                .FirstOrDefaultAsync(cancellationToken) ?? throw new InvalidOperationException("No active medical supply has enough availability."),
            "foodwaterstock" => await dbContext.FoodWaterStocks
                .Where(stock => stock.IsActive && stock.QuantityOnHand >= request.Quantity)
                .OrderByDescending(stock => stock.QuantityOnHand)
                .Select(stock => (Guid?)stock.Id)
                .FirstOrDefaultAsync(cancellationToken) ?? throw new InvalidOperationException("No active food/water stock has enough availability."),
            "donatedsupply" => await dbContext.DonatedSupplies
                .Where(supply => supply.IsActive && supply.QuantityOnHand >= request.Quantity)
                .OrderByDescending(supply => supply.QuantityOnHand)
                .Select(supply => (Guid?)supply.Id)
                .FirstOrDefaultAsync(cancellationToken) ?? throw new InvalidOperationException("No donated supply has enough availability."),
            "managedsupply" => await dbContext.ManagedSupplies
                .Where(supply => supply.IsActive && supply.QuantityOnHand >= request.Quantity)
                .OrderByDescending(supply => supply.QuantityOnHand)
                .Select(supply => (Guid?)supply.Id)
                .FirstOrDefaultAsync(cancellationToken) ?? throw new InvalidOperationException("No manager-added supply has enough availability."),
            _ => Guid.NewGuid() // Clothes or custom items
        };

        return await AllocateAsync(
            new AllocateResourceRequest(
                resourceType,
                resourceId,
                request.Quantity,
                request.HelpRequestId,
                request.IncidentId),
            cancellationToken);
    }

    private static string NormalizeResourceType(string? resourceType)
    {
        if (string.IsNullOrWhiteSpace(resourceType))
        {
            throw new ArgumentException("Resource type is required.");
        }

        var trimmed = resourceType.Trim();
        return trimmed.ToLowerInvariant() switch
        {
            "medicalsupply" or "medical" => "MedicalSupply",
            "foodwaterstock" or "food" => "FoodWaterStock",
            "donatedsupply" => "DonatedSupply",
            "managedsupply" => "ManagedSupply",
            "clothes" or "clothing" => "Clothes",
            _ => trimmed
        };
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
            case "medicalsupply":
                var supply = await dbContext.MedicalSupplies.SingleOrDefaultAsync(item => item.Id == allocation.ResourceId, cancellationToken);
                if (supply is not null)
                {
                    supply.QuantityOnHand += (int)allocation.Quantity;
                    supply.UpdatedAtUtc = DateTime.UtcNow;
                }
                break;
            case "foodwaterstock":
                var stock = await dbContext.FoodWaterStocks.SingleOrDefaultAsync(item => item.Id == allocation.ResourceId, cancellationToken);
                if (stock is not null)
                {
                    stock.QuantityOnHand += allocation.Quantity;
                    stock.UpdatedAtUtc = DateTime.UtcNow;
                }
                break;
        }

        allocation.Status = "Released";
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(allocation);
    }

    private async Task AllocateMedicalSupplyAsync(AllocateResourceRequest request, CancellationToken cancellationToken)
    {
        if (request.Quantity != decimal.Truncate(request.Quantity))
        {
            throw new ArgumentException("Medical supply allocation quantity must be a whole number.");
        }

        var supply = await dbContext.MedicalSupplies.SingleOrDefaultAsync(
            item => item.Id == request.ResourceId && item.IsActive,
            cancellationToken)
            ?? throw new KeyNotFoundException("Medical supply was not found.");

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
            cancellationToken)
            ?? throw new KeyNotFoundException("Food/water stock item was not found.");

        if (stock.QuantityOnHand < request.Quantity)
        {
            throw new InvalidOperationException("Food/water stock is insufficient.");
        }

        stock.QuantityOnHand -= request.Quantity;
        stock.UpdatedAtUtc = DateTime.UtcNow;
    }

    private async Task AllocateDonatedSupplyAsync(AllocateResourceRequest request, CancellationToken cancellationToken)
    {
        var supply = await dbContext.DonatedSupplies.SingleOrDefaultAsync(
            item => item.Id == request.ResourceId && item.IsActive,
            cancellationToken)
            ?? throw new KeyNotFoundException("Donated supply was not found.");

        if (supply.QuantityOnHand < request.Quantity)
        {
            throw new InvalidOperationException("Donated supply stock is insufficient.");
        }

        supply.QuantityOnHand -= request.Quantity;
        supply.UpdatedAtUtc = DateTime.UtcNow;
        if (supply.QuantityOnHand == 0)
        {
            supply.IsActive = false;
        }
    }

    private async Task AllocateManagedSupplyAsync(AllocateResourceRequest request, CancellationToken cancellationToken)
    {
        var supply = await dbContext.ManagedSupplies.SingleOrDefaultAsync(
            item => item.Id == request.ResourceId && item.IsActive,
            cancellationToken)
            ?? throw new KeyNotFoundException("Manager-added supply was not found.");

        if (supply.QuantityOnHand < request.Quantity)
        {
            throw new InvalidOperationException("Manager-added supply stock is insufficient.");
        }

        supply.QuantityOnHand -= request.Quantity;
        supply.UpdatedAtUtc = DateTime.UtcNow;
        if (supply.QuantityOnHand == 0)
        {
            supply.IsActive = false;
        }
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
