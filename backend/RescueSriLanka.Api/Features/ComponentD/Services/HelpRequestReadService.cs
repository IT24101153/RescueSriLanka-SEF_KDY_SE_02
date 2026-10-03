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
    // Only requests a rescue team can actually act on reach the coordination
    // queue. Water/Food/Shelter/Other stay with Component B and Component C;
    // forwarding them here would just be noise the Rescue Coordinator can't do
    // anything about.
    private IQueryable<HelpRequest> Eligible() => db.HelpRequests.AsNoTracking()
        .Where(r => r.Status == HelpRequestStatus.Pending && r.VerificationStatus == VerificationStatus.Verified
            && (r.Type == HelpRequestType.Medical || r.Type == HelpRequestType.Rescue));

    public async Task<List<RescueHelpRequestDto>> GetEligibleAsync(CancellationToken ct = default)
    {
        var requests = await Eligible().OrderByDescending(r => r.UrgencyScore).ThenBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .ToListAsync(ct);
        var citizens = await CitizenLookupAsync(requests.Select(r => r.CitizenId), ct);
        return requests.Select(r => ToDto(r, citizens)).ToList();
    }

    public async Task<RescueHelpRequestDto?> GetEligibleAsync(Guid id, CancellationToken ct = default)
    {
        var request = await Eligible().SingleOrDefaultAsync(r => r.Id == id, ct);
        if (request is null) return null;
        var citizens = await CitizenLookupAsync([request.CitizenId], ct);
        return ToDto(request, citizens);
    }

    public async Task<RescueHelpRequestDto?> GetForExistingPlanAsync(Guid id, CancellationToken ct = default)
    {
        var request = await db.HelpRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id
            && r.VerificationStatus == VerificationStatus.Verified
            && (r.Status == HelpRequestStatus.Pending || r.Status == HelpRequestStatus.Assigned), ct);
        if (request is null) return null;
        var citizens = await CitizenLookupAsync([request.CitizenId], ct);
        return ToDto(request, citizens);
    }

    // The Rescue Coordinator needs a way to actually reach the person they're
    // dispatching a team to, so — unlike the rest of this DTO — citizen name
    // and phone number are deliberately included here. A missing user (should
    // not happen: CitizenId is a real FK with DeleteBehavior.Restrict) degrades
    // to null rather than failing the queue.
    private async Task<Dictionary<Guid, (string? Name, string? Phone)>> CitizenLookupAsync(
        IEnumerable<Guid> citizenIds, CancellationToken ct)
    {
        var ids = citizenIds.Distinct().ToArray();
        return await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => ((string? Name, string? Phone))(u.FullName, u.PhoneNumber), ct);
    }

    private static RescueHelpRequestDto ToDto(HelpRequest r, IReadOnlyDictionary<Guid, (string? Name, string? Phone)> citizens)
    {
        citizens.TryGetValue(r.CitizenId, out var citizen);
        return new(r.Id, r.Type.ToString(), r.Description,
            double.IsFinite(r.Latitude) ? r.Latitude : null, double.IsFinite(r.Longitude) ? r.Longitude : null,
            r.UrgencyScore, r.Status.ToString(), r.VerificationStatus.ToString(), r.CreatedAt, r.EstimatedPeopleCount,
            citizen.Name, citizen.Phone);
    }
}
