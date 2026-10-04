using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentA.Agents.ZonePlanningAgent;
using RescueSriLanka.Api.Features.ComponentA.DTOs;
using RescueSriLanka.Api.Features.ComponentA.Models;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Services;

namespace RescueSriLanka.Api.Features.ComponentA.Services;
public interface ISafetyZoneService
{
    Task<IReadOnlyList<SafetyZoneDto>> GetActiveAsync(CancellationToken ct = default);
    Task<ZoneCheckResultDto> CheckPointAsync(double latitude, double longitude, CancellationToken ct = default);
    Task<int> RecomputeAsync(CancellationToken ct = default);
}

/// <summary>
/// Coordinator-declared zones: the console's create / edit / retire, and what an
/// approved Zone Planning Agent plan writes through.
/// </summary>
public interface IManualZoneService
{
    /// <summary>
    /// Declares a manual zone. Throws <see cref="ArgumentException"/> for a
    /// zone off the island or already expired.
    /// </summary>
    Task<SafetyZoneDto> CreateManualAsync(
        SafetyZoneRequest request, Guid userId, Guid? sourceAgentRunId = null, CancellationToken ct = default);

    /// <summary>
    /// Edits a manual zone; null when it does not exist. Throws
    /// <see cref="InvalidOperationException"/> for a derived zone, which
    /// follows its incident and is changed by editing the incident.
    /// </summary>
    Task<SafetyZoneDto?> UpdateManualAsync(
        Guid id, SafetyZoneRequest request, Guid userId, CancellationToken ct = default);

    /// <summary>Takes a manual zone off the map, kept on record. False when it does not exist.</summary>
    Task<bool> RetireManualAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
/// Keeps the safe/caution/danger layer in step with the active incidents.
/// Manually declared zones are never touched by the recompute.
/// </summary>
public class SafetyZoneService(AppDbContext db, ILogger<SafetyZoneService> logger)
    : ISafetyZoneService, IManualZoneService
{
    public async Task<IReadOnlyList<SafetyZoneDto>> GetActiveAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var zones = await db.SafetyZones
            .AsNoTracking()
            .Where(zone => zone.IsActive && (zone.ExpiresAt == null || zone.ExpiresAt > now))
            .OrderBy(zone => zone.Status)
            .ToListAsync(ct);

        return [.. zones.Select(SafetyZoneDto.FromZone)];
    }

    /// <summary>The worst zone containing the point wins — caution never masks danger.</summary>
    public async Task<ZoneCheckResultDto> CheckPointAsync(
        double latitude, double longitude, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var zones = await db.SafetyZones
            .AsNoTracking()
            .Where(zone => zone.IsActive && (zone.ExpiresAt == null || zone.ExpiresAt > now))
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
                incident.Status != IncidentStatus.Reported &&
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
            MatchingZones = [.. matching.Select(SafetyZoneDto.FromZone)],
            NearbyIncidentCount = nearbyIncidents,
            Message = message
        };
    }

    /// <summary>Rebuilds every incident-derived zone. Returns how many are active afterwards.</summary>
    public async Task<int> RecomputeAsync(CancellationToken ct = default)
    {
        // Only approved reports draw a zone — an unchecked report must not
        // put a danger area on the public map.
        var activeIncidents = await db.Incidents
            .Where(incident => incident.IsActive &&
                               incident.Status != IncidentStatus.Reported &&
                               incident.Status != IncidentStatus.Merged)
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

        var now = DateTime.UtcNow;
        var total = await db.SafetyZones.CountAsync(
            zone => zone.IsActive && (zone.ExpiresAt == null || zone.ExpiresAt > now), ct);
        logger.LogInformation("Safety zones recomputed — {Count} active.", total);
        return total;
    }

    public async Task<SafetyZoneDto> CreateManualAsync(
        SafetyZoneRequest request, Guid userId, Guid? sourceAgentRunId = null, CancellationToken ct = default)
    {
        var zone = BuildManual(request, userId, sourceAgentRunId);
        db.SafetyZones.Add(zone);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Manual zone {Id} ({Status}) declared by {UserId}.", zone.Id, zone.Status, userId);
        return SafetyZoneDto.FromZone(zone);
    }

    public async Task<SafetyZoneDto?> UpdateManualAsync(
        Guid id, SafetyZoneRequest request, Guid userId, CancellationToken ct = default)
    {
        var zone = await db.SafetyZones.FirstOrDefaultAsync(entity => entity.Id == id, ct);
        if (zone is null) return null;

        if (zone.Source != ZoneSource.ManualOverride)
        {
            throw new InvalidOperationException(
                "This zone is derived from an incident. Edit the incident's location, radius or severity instead.");
        }

        Validate(request);
        Apply(zone, request);
        zone.CreatedByUserId = userId;
        zone.IsActive = true;

        await db.SaveChangesAsync(ct);
        return SafetyZoneDto.FromZone(zone);
    }

    public async Task<bool> RetireManualAsync(Guid id, CancellationToken ct = default)
    {
        var zone = await db.SafetyZones.FirstOrDefaultAsync(entity => entity.Id == id, ct);
        if (zone is null) return false;

        if (zone.Source != ZoneSource.ManualOverride)
        {
            throw new InvalidOperationException(
                "This zone is derived from an incident and goes when the incident is resolved or rejected.");
        }

        zone.IsActive = false;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// A new manual zone, validated but not yet added — so an approved plan can
    /// check every zone before writing any of them.
    /// </summary>
    public static SafetyZone BuildManual(SafetyZoneRequest request, Guid userId, Guid? sourceAgentRunId)
    {
        Validate(request);

        var zone = new SafetyZone
        {
            Name = request.Name.Trim(),
            Source = ZoneSource.ManualOverride,
            CreatedByUserId = userId,
            SourceAgentRunId = sourceAgentRunId
        };
        Apply(zone, request);
        return zone;
    }

    /// <summary>
    /// The checks a data annotation cannot express. Throws
    /// <see cref="ArgumentException"/>, reported as 400.
    /// </summary>
    public static void Validate(SafetyZoneRequest request)
    {
        if (!ZonePlanValidator.OnIsland(request.CenterLatitude, request.CenterLongitude))
        {
            throw new ArgumentException("The zone's centre must be in Sri Lanka.");
        }

        if (request.ExpiresAt is DateTime expiry && expiry.ToUniversalTime() <= DateTime.UtcNow)
        {
            throw new ArgumentException("The expiry time must be in the future.");
        }
    }

    private static void Apply(SafetyZone zone, SafetyZoneRequest request)
    {
        zone.Name = request.Name.Trim();
        zone.Status = request.Status;
        zone.CenterLatitude = request.CenterLatitude;
        zone.CenterLongitude = request.CenterLongitude;
        zone.RadiusMeters = request.RadiusMeters;
        zone.District = SriLankaDistricts.Normalise(request.District);
        zone.Rationale = string.IsNullOrWhiteSpace(request.Rationale) ? null : request.Rationale.Trim();
        zone.ExpiresAt = request.ExpiresAt?.ToUniversalTime();
        zone.ComputedAt = DateTime.UtcNow;
    }

    private static ZoneStatus MapSeverityToZone(IncidentSeverity severity) => severity switch
    {
        IncidentSeverity.Critical or IncidentSeverity.High => ZoneStatus.Danger,
        IncidentSeverity.Moderate => ZoneStatus.Caution,
        _ => ZoneStatus.Caution
    };
}
