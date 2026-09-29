namespace RescueSriLanka.Api.Features.ComponentC.Models;

public class DonatedSupply
{
    public Guid Id { get; set; }

    public Guid DonationId { get; set; }

    public required string Name { get; set; }

    public required string DonorName { get; set; }

    public decimal QuantityOnHand { get; set; }

    public required string Unit { get; set; }

    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}