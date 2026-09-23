namespace RescueSriLanka.Api.Features.ComponentC.Models;

public class HelpRequest
{
    public Guid Id { get; set; }

    public required string RequesterName { get; set; }

    public required string ContactNumber { get; set; }

    public required string NeedType { get; set; }

    public required string Description { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public string Status { get; set; } = "Pending";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
