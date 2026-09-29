namespace RescueSriLanka.Api.Features.ComponentC.Models;

public class ManagedSupply
{
    public Guid Id { get; set; }

    public required string Category { get; set; }

    public required string Name { get; set; }

    public required string Unit { get; set; }

    public decimal QuantityOnHand { get; set; }

    public decimal LowStockThreshold { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}