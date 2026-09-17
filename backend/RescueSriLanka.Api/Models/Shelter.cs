namespace RescueSriLanka.Api.Models;

public class Shelter
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public required string Address { get; set; }

    public decimal Latitude { get; set; }

    public decimal Longitude { get; set; }

    public int Capacity { get; set; }

    public int OccupiedCapacity { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public int AvailableCapacity => Math.Max(0, Capacity - OccupiedCapacity);
}
