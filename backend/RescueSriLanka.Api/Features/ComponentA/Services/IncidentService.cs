using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentA.DTOs;
using RescueSriLanka.Api.Features.ComponentA.Models;
using RescueSriLanka.Api.Features.ComponentA.Services.Notifications;
using RescueSriLanka.Api.Services;

namespace RescueSriLanka.Api.Features.ComponentA.Services;
public interface IIncidentService
{
    Task<IReadOnlyList<IncidentDto>> QueryAsync(
        IncidentStatus? status, IncidentSeverity? severity, IncidentType? type,
        string? district, bool activeOnly, CancellationToken ct = default);

    Task<IncidentDto?> GetAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<IncidentDto>> NearbyAsync(
        double latitude, double longitude, double radiusKm, CancellationToken ct = default);

    Task<IncidentDto> CreateAsync(
        CreateIncidentRequest request, Guid? reportedByUserId, CancellationToken ct = default);

    Task<IncidentDto?> UpdateStatusAsync(
        Guid id, IncidentStatus status, Guid actingUserId, CancellationToken ct = default);

    Task<IncidentDto?> OverrideSeverityAsync(
        Guid id, IncidentSeverity severity, Guid actingUserId, CancellationToken ct = default);

    Task<DashboardStatisticsDto> GetStatisticsAsync(CancellationToken ct = default);
}

public class IncidentService(
    AppDbContext db,
    ISafetyZoneService zoneService,
    IIncidentAnalysisQueue analysisQueue,
    INotificationQueue notificationQueue,
    ILogger<IncidentService> logger) : IIncidentService
{
    public async Task<IReadOnlyList<IncidentDto>> QueryAsync(
        IncidentStatus? status, IncidentSeverity? severity, IncidentType? type,
        string? district, bool activeOnly, CancellationToken ct = default)
    {
        var query = db.Incidents.AsNoTracking().Include(incident => incident.Images).AsQueryable();

        if (activeOnly) query = query.Where(incident => incident.IsActive);
        if (status is not null) query = query.Where(incident => incident.Status == status);
        if (severity is not null) query = query.Where(incident => incident.Severity == severity);
        if (type is not null) query = query.Where(incident => incident.Type == type);
        if (!string.IsNullOrWhiteSpace(district))
            query = query.Where(incident => incident.District == district);

        var incidents = await query
            .OrderByDescending(incident => incident.Severity)
            .ThenByDescending(incident => incident.ReportedAt)
            .ToListAsync(ct);

        return incidents.Select(incident => IncidentDto.FromIncident(incident)).ToList();
    }

    public async Task<IncidentDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var incident = await db.Incidents
            .AsNoTracking()
            .Include(entity => entity.Images)
            .FirstOrDefaultAsync(entity => entity.Id == id, ct);

        return incident is null ? null : IncidentDto.FromIncident(incident);
    }

    /// <summary>The "what's near me" query — bounding box in SQL, exact distance in memory.</summary>
    public async Task<IReadOnlyList<IncidentDto>> NearbyAsync(
        double latitude, double longitude, double radiusKm, CancellationToken ct = default)
    {
        var (minLat, maxLat, minLon, maxLon) = GeoService.BoundingBox(latitude, longitude, radiusKm);

        var candidates = await db.Incidents
            .AsNoTracking()
            .Include(incident => incident.Images)
            .Where(incident =>
                incident.IsActive &&
                incident.Latitude >= minLat && incident.Latitude <= maxLat &&
                incident.Longitude >= minLon && incident.Longitude <= maxLon)
            .ToListAsync(ct);

        return candidates
            .Select(incident => new
            {
                Incident = incident,
                Distance = GeoService.DistanceKm(
                    latitude, longitude, incident.Latitude, incident.Longitude)
            })
            .Where(row => row.Distance <= radiusKm)
            .OrderBy(row => row.Distance)
            .Select(row => IncidentDto.FromIncident(row.Incident, Math.Round(row.Distance, 2)))
            .ToList();
    }

    public async Task<IncidentDto> CreateAsync(
        CreateIncidentRequest request, Guid? reportedByUserId, CancellationToken ct = default)
    {
        var incident = new Incident
        {
            Title = request.Title.Trim(),
            Description = request.Description.Trim(),
            Type = request.Type,
            Severity = request.Severity ?? IncidentSeverity.Moderate,
            Status = IncidentStatus.Reported,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            AffectedRadiusMeters = request.AffectedRadiusMeters,
            District = request.District?.Trim(),
            AddressText = request.AddressText?.Trim(),
            EstimatedAffectedPeople = request.EstimatedAffectedPeople,
            ReportedByUserId = reportedByUserId
        };

        db.Incidents.Add(incident);
        await db.SaveChangesAsync(ct);

        // A new incident changes the map's zone layer immediately.
        await zoneService.RecomputeAsync(ct);

        // The agent scores it in the background so the coordinator finds a
        // proposal waiting rather than a button to press. Nothing it produces
        // is applied without approval.
        analysisQueue.Enqueue(incident.Id);

        // Tell the reporter we have it. No district warning yet — nobody has
        // confirmed this is real.
        notificationQueue.Enqueue(
            new NotificationJob(NotificationKind.ReportReceived, incident.Id));

        logger.LogInformation("Incident {Id} created ({Type}, {Severity})",
            incident.Id, incident.Type, incident.Severity);

        return IncidentDto.FromIncident(incident);
    }

    public async Task<IncidentDto?> UpdateStatusAsync(
        Guid id, IncidentStatus status, Guid actingUserId, CancellationToken ct = default)
    {
        var incident = await db.Incidents
            .Include(entity => entity.Images)
            .FirstOrDefaultAsync(entity => entity.Id == id, ct);

        if (incident is null) return null;

        incident.Status = status;
        incident.UpdatedAt = DateTime.UtcNow;

        switch (status)
        {
            case IncidentStatus.Verified:
                incident.VerifiedByUserId = actingUserId;
                incident.VerifiedAt = DateTime.UtcNow;
                break;

            // Closing an incident takes it off the live map and drops its zone.
            case IncidentStatus.Resolved or IncidentStatus.Rejected:
                incident.IsActive = false;
                incident.ResolvedAt = DateTime.UtcNow;
                break;
        }

        await db.SaveChangesAsync(ct);
        await zoneService.RecomputeAsync(ct);

        // Verification is a human vouching for the report, which is exactly the
        // point at which warning a whole district becomes defensible. The
        // notification service decides whether the severity clears the bar.
        if (status == IncidentStatus.Verified)
        {
            notificationQueue.Enqueue(
                new NotificationJob(NotificationKind.DistrictWarning, incident.Id));
        }

        return IncidentDto.FromIncident(incident);
    }

    public async Task<IncidentDto?> OverrideSeverityAsync(
        Guid id, IncidentSeverity severity, Guid actingUserId, CancellationToken ct = default)
    {
        var incident = await db.Incidents
            .Include(entity => entity.Images)
            .FirstOrDefaultAsync(entity => entity.Id == id, ct);

        if (incident is null) return null;

        incident.Severity = severity;
        incident.SeverityOverriddenBy = actingUserId;
        incident.SeverityOverriddenAt = DateTime.UtcNow;
        incident.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        await zoneService.RecomputeAsync(ct);

        // A coordinator setting the severity by hand is the same judgement as
        // approving a proposal, and without this an incident verified while it
        // looked Moderate would never warn its district once someone raised it
        // to Critical. Already-warned incidents are not warned twice.
        notificationQueue.Enqueue(
            new NotificationJob(NotificationKind.DistrictWarning, incident.Id));

        return IncidentDto.FromIncident(incident);
    }

    public async Task<DashboardStatisticsDto> GetStatisticsAsync(CancellationToken ct = default)
    {
        var active = await db.Incidents.AsNoTracking()
            .Where(incident => incident.IsActive)
            .ToListAsync(ct);

        var all = await db.Incidents.AsNoTracking().ToListAsync(ct);
        var since = DateTime.UtcNow.AddHours(-24);

        return new DashboardStatisticsDto
        {
            ActiveIncidents = active.Count,
            CriticalIncidents = active.Count(incident => incident.Severity == IncidentSeverity.Critical),
            AwaitingVerification = active.Count(incident => incident.Status == IncidentStatus.Reported),
            ReportedLast24Hours = all.Count(incident => incident.ReportedAt >= since),
            PeopleAffected = active.Sum(incident => incident.EstimatedAffectedPeople ?? 0),
            ActiveDangerZones = await db.SafetyZones.AsNoTracking()
                .CountAsync(zone => zone.IsActive && zone.Status == ZoneStatus.Danger, ct),
            AwaitingAiAnalysis = active.Count(incident => incident.AiAnalysedAt is null),
            BySeverity = active.GroupBy(incident => incident.Severity.ToString())
                .ToDictionary(group => group.Key, group => group.Count()),
            ByType = active.GroupBy(incident => incident.Type.ToString())
                .ToDictionary(group => group.Key, group => group.Count()),
            ByStatus = active.GroupBy(incident => incident.Status.ToString())
                .ToDictionary(group => group.Key, group => group.Count()),
            ByDistrict = active.Where(incident => incident.District is not null)
                .GroupBy(incident => incident.District!)
                .ToDictionary(group => group.Key, group => group.Count())
        };
    }
}
