using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentA.DTOs;
using RescueSriLanka.Api.Features.ComponentA.Models;
using RescueSriLanka.Api.Features.ComponentA.Services.Notifications;
using RescueSriLanka.Api.Services;

namespace RescueSriLanka.Api.Features.ComponentA.Services;
public interface IIncidentService
{
    /// <summary>
    /// <paramref name="sortBy"/> is one of severity (default), reportedat, status,
    /// type, district or affectedpeople; unrecognised values fall back to the
    /// default. <paramref name="sortDir"/> is "asc" or "desc" (default). Paging
    /// applies only when both <paramref name="page"/> and <paramref name="pageSize"/>
    /// are supplied — omitting them returns every matching incident, unchanged
    /// from before paging existed, which is what the map and other components
    /// that call this endpoint without paging still rely on.
    /// </summary>
    Task<IncidentQueryResult> QueryAsync(
        IncidentStatus? status, IncidentSeverity? severity, IncidentType? type,
        string? district, bool activeOnly,
        string? sortBy = null, string? sortDir = null,
        int? page = null, int? pageSize = null,
        bool approvedOnly = false,
        CancellationToken ct = default);

    Task<IncidentDto?> GetAsync(Guid id, CancellationToken ct = default);

    /// <param name="approvedOnly">True for the public: leave out reports a
    /// coordinator has not yet approved as true.</param>
    Task<IReadOnlyList<IncidentDto>> NearbyAsync(
        double latitude, double longitude, double radiusKm,
        bool approvedOnly = false, CancellationToken ct = default);

    Task<IncidentDto> CreateAsync(
        CreateIncidentRequest request, Guid? reportedByUserId, CancellationToken ct = default);

    /// <summary>
    /// Files a report and its photo together, storing the photo before the
    /// analysis agent is asked to look — so the agent actually sees it.
    /// Throws <see cref="ArgumentException"/>, before anything is created, for
    /// a photo that would be refused.
    /// </summary>
    Task<CreateIncidentResponse> CreateWithPhotoAsync(
        CreateIncidentRequest request, IFormFile photo, string? caption,
        Guid? reportedByUserId, CancellationToken ct = default);

    Task<IncidentDto?> UpdateStatusAsync(
        Guid id, IncidentStatus status, Guid actingUserId, CancellationToken ct = default);

    Task<IncidentDto?> OverrideSeverityAsync(
        Guid id, IncidentSeverity severity, Guid actingUserId, CancellationToken ct = default);

    Task<DashboardStatisticsDto> GetStatisticsAsync(CancellationToken ct = default);

    /// <returns>False when no such incident exists.</returns>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

public class IncidentService(
    AppDbContext db,
    ISafetyZoneService zoneService,
    IIncidentAnalysisQueue analysisQueue,
    INotificationQueue notificationQueue,
    IImageStorageService imageStorage,
    ILogger<IncidentService> logger) : IIncidentService
{
    public async Task<IncidentQueryResult> QueryAsync(
        IncidentStatus? status, IncidentSeverity? severity, IncidentType? type,
        string? district, bool activeOnly,
        string? sortBy = null, string? sortDir = null,
        int? page = null, int? pageSize = null,
        bool approvedOnly = false,
        CancellationToken ct = default)
    {
        var query = db.Incidents.AsNoTracking().Include(incident => incident.Images).AsQueryable();

        if (activeOnly) query = query.Where(incident => incident.IsActive);
        // A report is only public once a coordinator has approved it as true.
        if (approvedOnly) query = query.Where(incident => incident.Status != IncidentStatus.Reported);
        if (status is not null) query = query.Where(incident => incident.Status == status);
        if (severity is not null) query = query.Where(incident => incident.Severity == severity);
        if (type is not null) query = query.Where(incident => incident.Type == type);
        if (!string.IsNullOrWhiteSpace(district))
            query = query.Where(incident => incident.District == district);

        var totalCount = await query.CountAsync(ct);

        var descending = !string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);
        query = sortBy?.ToLowerInvariant() switch
        {
            "reportedat" => descending
                ? query.OrderByDescending(incident => incident.ReportedAt)
                : query.OrderBy(incident => incident.ReportedAt),
            "status" => descending
                ? query.OrderByDescending(incident => incident.Status)
                : query.OrderBy(incident => incident.Status),
            "type" => descending
                ? query.OrderByDescending(incident => incident.Type)
                : query.OrderBy(incident => incident.Type),
            "district" => descending
                ? query.OrderByDescending(incident => incident.District)
                : query.OrderBy(incident => incident.District),
            "affectedpeople" => descending
                ? query.OrderByDescending(incident => incident.EstimatedAffectedPeople)
                : query.OrderBy(incident => incident.EstimatedAffectedPeople),
            // Default: unchanged from before paging and sorting existed.
            _ => query
                .OrderByDescending(incident => incident.Severity)
                .ThenByDescending(incident => incident.ReportedAt)
        };

        // Omitting page/pageSize keeps every existing caller (the map, other
        // components) working exactly as before this was added.
        if (page is > 0 && pageSize is > 0)
        {
            query = query.Skip((page.Value - 1) * pageSize.Value).Take(Math.Min(pageSize.Value, 100));
        }

        var incidents = await query.ToListAsync(ct);

        return new IncidentQueryResult(
            [.. incidents.Select(incident => IncidentDto.FromIncident(incident))],
            totalCount);
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
        double latitude, double longitude, double radiusKm,
        bool approvedOnly = false, CancellationToken ct = default)
    {
        var (minLat, maxLat, minLon, maxLon) = GeoService.BoundingBox(latitude, longitude, radiusKm);

        var candidates = await db.Incidents
            .AsNoTracking()
            .Include(incident => incident.Images)
            .Where(incident =>
                incident.IsActive &&
                (!approvedOnly || incident.Status != IncidentStatus.Reported) &&
                incident.Latitude >= minLat && incident.Latitude <= maxLat &&
                incident.Longitude >= minLon && incident.Longitude <= maxLon)
            .ToListAsync(ct);

        return [.. candidates
            .Select(incident => new
            {
                Incident = incident,
                Distance = GeoService.DistanceKm(
                    latitude, longitude, incident.Latitude, incident.Longitude)
            })
            .Where(row => row.Distance <= radiusKm)
            .OrderBy(row => row.Distance)
            .Select(row => IncidentDto.FromIncident(row.Incident, Math.Round(row.Distance, 2)))];
    }

    public async Task<IncidentDto> CreateAsync(
        CreateIncidentRequest request, Guid? reportedByUserId, CancellationToken ct = default)
    {
        var incident = await SaveNewAsync(request, reportedByUserId, ct);
        QueueFollowUps(incident.Id);
        return IncidentDto.FromIncident(incident);
    }

    public async Task<CreateIncidentResponse> CreateWithPhotoAsync(
        CreateIncidentRequest request, IFormFile photo, string? caption,
        Guid? reportedByUserId, CancellationToken ct = default)
    {
        // Refuse a bad file up front: a report left behind without the photo
        // the citizen meant to send is worse than asking them to pick another.
        ImageStorageService.Validate(photo);

        var incident = await SaveNewAsync(request, reportedByUserId, ct);

        string? photoError = null;
        try
        {
            await imageStorage.SaveAsync(incident.Id, photo, caption, reportedByUserId, ct);
        }
        catch (InvalidOperationException ex)
        {
            // The storage backend failed (Cloudinary down, bad credentials).
            // The report itself is real and must stand; the citizen is told
            // the photo did not make it.
            photoError = ex.Message;
            logger.LogWarning(
                "Incident {Id} filed, but its photo was not stored: {Reason}", incident.Id, ex.Message);
        }

        // Only now, with the photo stored, does the agent get to look.
        QueueFollowUps(incident.Id);

        var saved = await GetAsync(incident.Id, ct) ?? IncidentDto.FromIncident(incident);
        return new CreateIncidentResponse { Incident = saved, PhotoError = photoError };
    }

    private async Task<Incident> SaveNewAsync(
        CreateIncidentRequest request, Guid? reportedByUserId, CancellationToken ct)
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

        logger.LogInformation("Incident {Id} created ({Type}, {Severity})",
            incident.Id, incident.Type, incident.Severity);

        return incident;
    }

    /// <summary>
    /// Background work for a new report. Queued last, after any photo is
    /// stored: the analysis worker starts at once, so queuing any earlier means
    /// the agent grades the report without ever seeing the picture.
    /// </summary>
    private void QueueFollowUps(Guid incidentId)
    {
        // The agent scores it in the background so the coordinator finds a
        // proposal waiting rather than a button to press. Nothing it produces
        // is applied without approval.
        analysisQueue.Enqueue(incidentId);

        // Tell the reporter we have it. No district warning yet — nobody has
        // confirmed this is real.
        notificationQueue.Enqueue(
            new NotificationJob(NotificationKind.ReportReceived, incidentId));
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

        // Resolved/Rejected close an incident: off the live map, zone dropped.
        // Every other status reopens it — a coordinator can revisit a Rejected
        // report and mark it Verified later, and that has to bring it back to
        // life rather than leave it stranded inactive with a stale ResolvedAt.
        var closing = status is IncidentStatus.Resolved or IncidentStatus.Rejected;
        incident.IsActive = !closing;
        incident.ResolvedAt = closing ? DateTime.UtcNow : null;

        if (status == IncidentStatus.Verified)
        {
            incident.VerifiedByUserId = actingUserId;
            incident.VerifiedAt = DateTime.UtcNow;
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

    /// <summary>
    /// Permanently removes a report — a coordinator's call for a duplicate,
    /// spam, or test report, distinct from Rejected (which keeps it on record
    /// as reviewed and false). Its images and any derived safety zone cascade
    /// at the database level; a help request that referenced it keeps existing
    /// with the link cleared.
    /// </summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var incident = await db.Incidents.FirstOrDefaultAsync(entity => entity.Id == id, ct);
        if (incident is null) return false;

        // AgentRun is a table shared across every component's agents and has no
        // foreign key to Incident (see its model comment), so nothing cascades
        // into it — left alone, its rows for this incident would dangle.
        var runs = await db.AgentRuns.Where(run => run.IncidentId == id).ToListAsync(ct);
        db.AgentRuns.RemoveRange(runs);

        db.Incidents.Remove(incident);
        await db.SaveChangesAsync(ct);

        // The incident behind a derived safety zone is gone; recompute so the
        // map stops showing a zone with nothing underneath it.
        await zoneService.RecomputeAsync(ct);

        logger.LogInformation("Incident {Id} deleted", id);
        return true;
    }
}
