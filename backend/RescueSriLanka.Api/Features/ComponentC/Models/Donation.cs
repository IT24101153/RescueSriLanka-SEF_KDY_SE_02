namespace RescueSriLanka.Api.Features.ComponentC.Models;

public class Donation
{
    public Guid Id { get; set; }

    public required string DonorName { get; set; }

    public required string ContactNumber { get; set; }

    public required string DonationType { get; set; }

    public decimal Quantity { get; set; }

    public required string Unit { get; set; }

    public string? Notes { get; set; }

    public string Status { get; set; } = "PendingReview";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
