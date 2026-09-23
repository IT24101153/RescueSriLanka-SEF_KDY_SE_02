namespace RescueSriLanka.Api.Features.ComponentC.Models;

public class FoodWaterStock
{
    public Guid Id { get; set; }

    public required string ItemName { get; set; }

    public required string Unit { get; set; }

    public decimal QuantityOnHand { get; set; }

    public decimal LowStockThreshold { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public bool IsLowStock => QuantityOnHand <= LowStockThreshold;
}
