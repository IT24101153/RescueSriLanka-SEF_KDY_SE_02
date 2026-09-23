namespace RescueSriLanka.Api.Features.ComponentC.Models;

public class MedicalSupply
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public required string Unit { get; set; }

    public int QuantityOnHand { get; set; }

    public int LowStockThreshold { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public bool IsLowStock => QuantityOnHand <= LowStockThreshold;
}
