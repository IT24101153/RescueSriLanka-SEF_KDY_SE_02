using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentB.Models;
using RescueSriLanka.Api.Features.ComponentD.DTOs;

namespace RescueSriLanka.Api.Features.ComponentD.Services;

public interface IHelpRequestReadService
{
    Task<List<RescueHelpRequestDto>> GetEligibleAsync(CancellationToken ct = default);
    Task<RescueHelpRequestDto?> GetEligibleAsync(Guid id, CancellationToken ct = default);
    Task<RescueHelpRequestDto?> GetForExistingPlanAsync(Guid id, CancellationToken ct = default);
}

public sealed class HelpRequestReadService(AppDbContext db) : IHelpRequestReadService
{
    private IQueryable<HelpRequest> Eligible() => db.HelpRequests.AsNoTracking()
        .Where(r => r.Status == HelpRequestStatus.Pending && r.VerificationStatus == VerificationStatus.Verified);

    public async Task<List<RescueHelpRequestDto>> GetEligibleAsync(CancellationToken ct = default) =>
        (await Eligible().OrderByDescending(r => r.UrgencyScore).ThenBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .ToListAsync(ct)).Select(ToDto).ToList();

    public async Task<RescueHelpRequestDto?> GetEligibleAsync(Guid id, CancellationToken ct = default)
    {
        var request = await Eligible().SingleOrDefaultAsync(r => r.Id == id, ct);
        return request is null ? null : ToDto(request);
    }

    public async Task<RescueHelpRequestDto?> GetForExistingPlanAsync(Guid id, CancellationToken ct = default)
    {
        var request = await db.HelpRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id
            && r.VerificationStatus == VerificationStatus.Verified
            && (r.Status == HelpRequestStatus.Pending || r.Status == HelpRequestStatus.Assigned), ct);
        return request is null ? null : ToDto(request);
    }

    private static RescueHelpRequestDto ToDto(HelpRequest r) => new(r.Id, r.Type.ToString(), r.Description,
        double.IsFinite(r.Latitude) ? r.Latitude : null, double.IsFinite(r.Longitude) ? r.Longitude : null, r.UrgencyScore, r.Status.ToString(), r.VerificationStatus.ToString(), r.CreatedAt, r.EstimatedPeopleCount);
}
