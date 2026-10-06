using System.ComponentModel.DataAnnotations;
using RescueSriLanka.Api.DTOs;

namespace RescueSriLanka.Api.Features.ComponentC.DTOs;

public record CreateMedicalSupplyRequest(
    string Name,
    string Unit,
    [Range(0, 1000000000)] int QuantityOnHand,
    int LowStockThreshold);

public record CreateFoodWaterStockRequest(
    string ItemName,
    string Unit,
    [Range(0, 1000000000)] decimal QuantityOnHand,
    decimal LowStockThreshold);

public record UpdateMedicalSupplyRequest(
    string Name,
    string Unit,
    [Range(0, 1000000000)] int QuantityOnHand,
    int LowStockThreshold);

public record UpdateFoodWaterStockRequest(
    string ItemName,
    string Unit,
    [Range(0, 1000000000)] decimal QuantityOnHand,
    decimal LowStockThreshold);

public record AllocateResourceRequest(
    string ResourceType,
    Guid ResourceId,
    [Range(0.01, 1000000000)] decimal Quantity,
    Guid? HelpRequestId,
    Guid? IncidentId);

public record MatchResourceRequest(
    string ResourceType,
    [Range(0.01, 1000000000)] decimal Quantity,
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
    [Required, PersonName] string RequesterName,
    [Required, SriLankaPhone] string ContactNumber,
    string NeedType,
    string Description,
    [Range(-90, 90)] decimal? Latitude,
    [Range(-180, 180)] decimal? Longitude);

public record HelpRequestResponse(
    Guid Id,
    Guid? UserId,
    string RequesterName,
    string ContactNumber,
    string NeedType,
    string Description,
    decimal? Latitude,
    decimal? Longitude,
    string Status,
    DateTime CreatedAtUtc,
    string? District);

public record UpdateHelpRequestStatusRequest(string Status);

public record CreateDonationRequest(
    [Required, PersonName] string DonorName,
    [Required, SriLankaPhone] string ContactNumber,
    string DonationType,
    [Range(0.01, 1000000000)] decimal Quantity,
    string Unit,
    string? Notes);

public record ResourceSubmissionItem(
    string Category,
    string ItemName,
    [Range(0.01, 1000000000)] decimal Quantity,
    string Unit);

public record CreateHelpRequestsBatchRequest(List<ResourceSubmissionItem> Items);

public record CreateDonationsBatchRequest(
    List<ResourceSubmissionItem> Items,
    string? Notes);

public record DonationResponse(
    Guid Id,
    Guid? UserId,
    Guid? SubmissionId,
    string DonorName,
    string ContactNumber,
    string DonationType,
    decimal Quantity,
    string Unit,
    string? Notes,
    string Status,
    DateTime CreatedAtUtc,
    string? District);

public record DonatedSupplyResponse(
    Guid Id,
    Guid DonationId,
    string Name,
    string DonorName,
    decimal QuantityOnHand,
    string Unit,
    string? Notes,
    DateTime UpdatedAtUtc);

public record CreateManagedSupplyRequest(
    string Category,
    string Name,
    string Unit,
    [Range(0, 1000000000)] decimal QuantityOnHand,
    decimal LowStockThreshold);
