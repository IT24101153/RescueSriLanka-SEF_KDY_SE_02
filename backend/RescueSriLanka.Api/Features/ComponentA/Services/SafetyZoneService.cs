using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentA.DTOs;
using RescueSriLanka.Api.Features.ComponentA.Models;
using RescueSriLanka.Api.Services;

namespace RescueSriLanka.Api.Features.ComponentA.Services;
public interface ISafetyZoneService
{
    Task<IReadOnlyList<SafetyZoneDto>> GetActiveAsync(CancellationToken ct = default);
    Task<ZoneCheckResultDto> CheckPointAsync(double latitude, double longitude, CancellationToken ct = default);
    Task<int> RecomputeAsync(CancellationToken ct = default);
}

/// <summary>
/// Keeps the safe/caution/danger layer in step with the active incidents.
/// Manually declared zones are never touched by the recompute.
/// </summary>
public class SafetyZoneService(AppDbContext db, ILogger<SafetyZoneService> logger) : ISafetyZoneService
{
    public async Task<IReadOnlyList<SafetyZoneDto>> GetActiveAsync(CancellationToken ct = default)
    {
        var zones = await db.SafetyZones
            .AsNoTracking()
            .Where(zone => zone.IsActive)
            .OrderBy(zone => zone.Status)
            .ToListAsync(ct);

        return zones.Select(SafetyZoneDto.FromZone).ToList();
    }

    /// <summary>The worst zone containing the point wins — caution never masks danger.</summary>
    public async Task<ZoneCheckResultDto> CheckPointAsync(
        double latitude, double longitude, CancellationToken ct = default)
    {
        var zones = await db.SafetyZones
            .AsNoTracking()
            .Where(zone => zone.IsActive)
            .ToListAsync(ct);

        var matching = zones
            .Where(zone => GeoService.DistanceKm(
                latitude, longitude, zone.CenterLatitude, zone.CenterLongitude)
                    * 1000 <= zone.RadiusMeters)
            .OrderByDescending(zone => zone.Status)
            .ToList();

        var status = matching.Count == 0 ? ZoneStatus.Safe : matching[0].Status;

        var (minLat, maxLat, minLon, maxLon) = GeoService.BoundingBox(latitude, longitude, 10);
        var nearbyIncidents = await db.Incidents
            .AsNoTracking()
            .CountAsync(incident =>
                incident.IsActive &&
                incident.Latitude >= minLat && incident.Latitude <= maxLat &&
                incident.Longitude >= minLon && incident.Longitude <= maxLon, ct);

        var message = status switch
        {
            ZoneStatus.Danger => "You are inside an active danger zone. Follow official evacuation guidance.",
            ZoneStatus.Caution => "Caution: an active incident is affecting this area.",
            _ => "No active hazard recorded at this location."
        };

        return new ZoneCheckResultDto
        {
            Status = status.ToString(),
            Latitude = latitude,
            Longitude = longitude,
            MatchingZones = matching.Select(SafetyZoneDto.FromZone).ToList(),
            NearbyIncidentCount = nearbyIncidents,
            Message = message
        };
    }

    /// <summary>Rebuilds every incident-derived zone. Returns how many are active afterwards.</summary>
    public async Task<int> RecomputeAsync(CancellationToken ct = default)
    {
        var activeIncidents = await db.Incidents
            .Where(incident => incident.IsActive)
            .ToListAsync(ct);

        var derivedZones = await db.SafetyZones
            .Where(zone => zone.Source == ZoneSource.DerivedFromIncident)
            .ToListAsync(ct);

        var activeIds = activeIncidents.Select(incident => incident.Id).ToHashSet();

        // Zones whose incident closed are no longer relevant.
        foreach (var stale in derivedZones.Where(zone =>
                     zone.SourceIncidentId is null || !activeIds.Contains(zone.SourceIncidentId.Value)))
        {
            db.SafetyZones.Remove(stale);
        }

        foreach (var incident in activeIncidents)
        {
            var status = MapSeverityToZone(incident.Severity);
            var existing = derivedZones.FirstOrDefault(zone => zone.SourceIncidentId == incident.Id);

            if (existing is null)
            {
                db.SafetyZones.Add(new SafetyZone
                {
                    Name = $"{incident.Type} — {incident.District ?? "Unknown area"}",
                    Status = status,
                    Source = ZoneSource.DerivedFromIncident,
                    CenterLatitude = incident.Latitude,
                    CenterLongitude = incident.Longitude,
                    RadiusMeters = incident.AffectedRadiusMeters,
                    District = incident.District,
                    Rationale = $"Derived from {incident.Severity} {incident.Type} incident.",
                    SourceIncidentId = incident.Id
                });
                continue;
            }

            existing.Status = status;
            existing.CenterLatitude = incident.Latitude;
            existing.CenterLongitude = incident.Longitude;
            existing.RadiusMeters = incident.AffectedRadiusMeters;
            existing.District = incident.District;
            existing.Rationale = $"Derived from {incident.Severity} {incident.Type} incident.";
            existing.ComputedAt = DateTime.UtcNow;
            existing.IsActive = true;
        }

        await db.SaveChangesAsync(ct);

        var total = await db.SafetyZones.CountAsync(zone => zone.IsActive, ct);
        logger.LogInformation("Safety zones recomputed — {Count} active.", total);
        return total;
    }

    private static ZoneStatus MapSeverityToZone(IncidentSeverity severity) => severity switch
    {
        IncidentSeverity.Critical or IncidentSeverity.High => ZoneStatus.Danger,
        IncidentSeverity.Moderate => ZoneStatus.Caution,
        _ => ZoneStatus.Caution
    };
}
