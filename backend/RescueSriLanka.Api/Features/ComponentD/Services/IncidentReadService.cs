using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentA.DTOs;
using RescueSriLanka.Api.Features.ComponentA.Models;

namespace RescueSriLanka.Api.Features.ComponentD.Services;
public interface IIncidentReadService
{
    Task<IReadOnlyList<IncidentDto>> QueryAsync(
        IncidentStatus? status = null, IncidentSeverity? severity = null,
        IncidentType? type = null, string? district = null,
        bool activeOnly = true, CancellationToken ct = default);
    Task<IncidentDto?> GetAsync(Guid id, CancellationToken ct = default);
}

public class IncidentReadService(AppDbContext db) : IIncidentReadService
{
    public async Task<IReadOnlyList<IncidentDto>> QueryAsync(
        IncidentStatus? status = null, IncidentSeverity? severity = null,
        IncidentType? type = null, string? district = null,
        bool activeOnly = true, CancellationToken ct = default)
    {
        var query = db.Incidents.AsNoTracking().Include(incident => incident.Images).AsQueryable();
        if (activeOnly) query = query.Where(incident => incident.IsActive);
        if (status is not null) query = query.Where(incident => incident.Status == status);
        if (severity is not null) query = query.Where(incident => incident.Severity == severity);
        if (type is not null) query = query.Where(incident => incident.Type == type);
        if (!string.IsNullOrWhiteSpace(district)) query = query.Where(incident => incident.District == district);

        // Enums are stored as text; explicit priority avoids alphabetical SQL ordering.
        var incidents = await query.OrderByDescending(incident =>
                incident.Severity == IncidentSeverity.Critical ? 3 :
                incident.Severity == IncidentSeverity.High ? 2 :
                incident.Severity == IncidentSeverity.Moderate ? 1 : 0)
            .ThenByDescending(incident => incident.ReportedAt).ToListAsync(ct);
        return incidents.Select(incident => IncidentDto.FromIncident(incident)).ToList();
    }

    public async Task<IncidentDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var incident = await db.Incidents.AsNoTracking().Include(entity => entity.Images)
            .FirstOrDefaultAsync(entity => entity.Id == id, ct);
        return incident is null ? null : IncidentDto.FromIncident(incident);
    }
}
