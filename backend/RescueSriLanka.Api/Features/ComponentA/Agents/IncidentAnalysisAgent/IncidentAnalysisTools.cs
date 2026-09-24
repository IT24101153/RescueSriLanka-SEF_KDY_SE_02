using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Services;

namespace RescueSriLanka.Api.Features.ComponentA.Agents.IncidentAnalysisAgent;
/// <summary>
/// The allow-list. These are the ONLY tools the Incident Analysis Agent may
/// call — every input is validated here, every output is a plain structured
/// value, and the agent cannot reach anything else in the system.
/// </summary>
public class IncidentAnalysisTools(
    AppDbContext db,
    HttpClient http,
    ILogger<IncidentAnalysisTools> logger)
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
    /// Rainfall over the last 48 hours, in mm, from Open-Meteo — free and
    /// keyless, so there is no secret to protect. Returns null when the service
    /// is down, slow or answers with something unexpected: the agent must cope
    /// with a tool being unavailable, never fail because of one.
    /// </summary>
    public async Task<double?> GetRainfallLast48hAsync(
        double latitude, double longitude, CancellationToken ct = default)
    {
        // Validated input — only real coordinates ever leave the system.
        if (latitude is < -90 or > 90 || longitude is < -180 or > 180) return null;

        var url = string.Create(CultureInfo.InvariantCulture,
            $"v1/forecast?latitude={latitude:F4}&longitude={longitude:F4}&hourly=precipitation&past_hours=48&forecast_hours=0");

        try
        {
            using var document = JsonDocument.Parse(await http.GetStringAsync(url, ct));

            var hours = document.RootElement.GetProperty("hourly").GetProperty("precipitation");

            // Missing hours arrive as null and are skipped, not counted as zero.
            return hours.EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.Number)
                .Sum(value => value.GetDouble());
        }
        catch (Exception ex) when (
            ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException ||
            (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Rainfall tool unavailable for {Lat},{Lon}.", latitude, longitude);
            return null;
        }
    }
}
