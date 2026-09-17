namespace RescueSriLanka.Api.Models;

public class ResourceAllocation
{
    public Guid Id { get; set; }

    public required string ResourceType { get; set; }

    public Guid ResourceId { get; set; }

    public decimal Quantity { get; set; }

    public Guid? HelpRequestId { get; set; }

    public Guid? IncidentId { get; set; }

    public string Status { get; set; } = "Active";

    public DateTime AllocatedAtUtc { get; set; } = DateTime.UtcNow;
}
