using System;

namespace RescueSriLanka.Api.Features.ComponentB.Models
{
    // Represents a computed safety advisory for an area or route, used by
    // the "route/area safety check" business operation before a tourist travels.
    public class TravelAdvisory
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string AreaName { get; set; } = string.Empty;

        // Centre point of the advisory area
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double RadiusMeters { get; set; }

        public SafetyLevel SafetyLevel { get; set; }

        // Human-readable explanation, e.g. "Active flood incident 1.2km away"
        public string Reason { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ExpiresAt { get; set; }
    }
}