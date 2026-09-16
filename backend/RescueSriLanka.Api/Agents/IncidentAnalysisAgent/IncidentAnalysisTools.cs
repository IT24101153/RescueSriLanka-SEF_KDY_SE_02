using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Services;

namespace RescueSriLanka.Api.Agents.IncidentAnalysisAgent;

/// <summary>
/// The allow-list. These are the ONLY tools the Incident Analysis Agent may
/// call — every input is validated here, every output is a plain structured
/// value, and the agent cannot reach anything else in the system.
/// </summary>
public class IncidentAnalysisTools(AppDbContext db)
{
    /// <summary>
    /// How many other active incidents sit within the radius. Clustered reports
    /// indicate a larger event than any single report reveals.
    /// </summary>
    public async Task<int> CountNearbyActiveIncidentsAsync(
        double latitude, double longitude, double radiusKm, Guid excludeId,
        CancellationToken ct = default)
    {
        // Validated input — the agent cannot ask for an unbounded sweep.
        if (latitude is < -90 or > 90) throw new ArgumentOutOfRangeException(nameof(latitude));
        if (longitude is < -180 or > 180) throw new ArgumentOutOfRangeException(nameof(longitude));
        radiusKm = Math.Clamp(radiusKm, 0.5, 50);

        var (minLat, maxLat, minLon, maxLon) =
            GeoService.BoundingBox(latitude, longitude, radiusKm);

        var candidates = await db.Incidents
            .AsNoTracking()
            .Where(incident =>
                incident.IsActive &&
                incident.Id != excludeId &&
                incident.Latitude >= minLat && incident.Latitude <= maxLat &&
                incident.Longitude >= minLon && incident.Longitude <= maxLon)
            .Select(incident => new { incident.Latitude, incident.Longitude })
            .ToListAsync(ct);

        return candidates.Count(row =>
            GeoService.DistanceKm(latitude, longitude, row.Latitude, row.Longitude) <= radiusKm);
    }

    /// <summary>
    /// Rainfall over the last 48 hours. Returns null when no weather provider is
    /// configured — the agent must cope with a tool being unavailable.
    /// </summary>
    public Task<double?> GetRainfallLast48hAsync(
        double latitude, double longitude, CancellationToken ct = default)
    {
        // TODO: OpenWeatherMap free tier. Until a key is configured the agent
        // runs without this signal rather than failing.
        _ = latitude;
        _ = longitude;
        _ = ct;
        return Task.FromResult<double?>(null);
    }
}
