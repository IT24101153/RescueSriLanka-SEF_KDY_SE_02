namespace RescueSriLanka.Api.Contracts;

public record CreateShelterRequest(
    string Name,
    string Address,
    decimal Latitude,
    decimal Longitude,
    int Capacity);

public record CreateMedicalSupplyRequest(
    string Name,
    string Unit,
    int QuantityOnHand,
    int LowStockThreshold);

public record CreateFoodWaterStockRequest(
    string ItemName,
    string Unit,
    decimal QuantityOnHand,
    decimal LowStockThreshold);

public record UpdateShelterRequest(
    string Name,
    string Address,
    decimal Latitude,
    decimal Longitude,
    int Capacity);

public record UpdateMedicalSupplyRequest(
    string Name,
    string Unit,
    int QuantityOnHand,
    int LowStockThreshold);

public record UpdateFoodWaterStockRequest(
    string ItemName,
    string Unit,
    decimal QuantityOnHand,
    decimal LowStockThreshold);

public record AllocateResourceRequest(
    string ResourceType,
    Guid ResourceId,
    decimal Quantity,
    Guid? HelpRequestId,
    Guid? IncidentId);

public record MatchResourceRequest(
    string ResourceType,
    decimal Quantity,
    Guid? HelpRequestId,
    Guid? IncidentId);

public record ResourceAllocationResponse(
    Guid Id,
    string ResourceType,
    Guid ResourceId,
    decimal Quantity,
    string Status,
    Guid? HelpRequestId,
    Guid? IncidentId,
    DateTime AllocatedAtUtc);

public record ResourceAlertResponse(
    string ResourceType,
    Guid ResourceId,
    string Name,
    decimal QuantityOnHand,
    decimal LowStockThreshold,
    string Unit);

public record CreateHelpRequestRequest(
    string RequesterName,
    string ContactNumber,
    string NeedType,
    string Description,
    decimal? Latitude,
    decimal? Longitude);

public record HelpRequestResponse(
    Guid Id,
    string RequesterName,
    string ContactNumber,
    string NeedType,
    string Description,
    decimal? Latitude,
    decimal? Longitude,
    string Status,
    DateTime CreatedAtUtc);

public record CreateDonationRequest(
    string DonorName,
    string ContactNumber,
    string DonationType,
    decimal Quantity,
    string Unit,
    string? Notes);

public record DonationResponse(
    Guid Id,
    string DonorName,
    string ContactNumber,
    string DonationType,
    decimal Quantity,
    string Unit,
    string? Notes,
    string Status,
    DateTime CreatedAtUtc);
