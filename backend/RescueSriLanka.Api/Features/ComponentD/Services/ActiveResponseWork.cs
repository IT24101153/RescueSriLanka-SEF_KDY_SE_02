using RescueSriLanka.Api.Features.ComponentD.Models;

namespace RescueSriLanka.Api.Features.ComponentD.Services;

public static class ActiveResponseWork
{
    // Shared existing reservation semantics: rejected plans and terminal dispatches
    // do not occupy resources. Keep candidate search and assignment validation aligned.
    public static IQueryable<Assignment> Active(this IQueryable<Assignment> assignments) =>
        assignments.Where(a => a.Status != AssignmentStatus.Rejected && a.Status != AssignmentStatus.Cancelled &&
            (a.Dispatch == null || (a.Dispatch.Status != DispatchStatus.Resolved && a.Dispatch.Status != DispatchStatus.Cancelled)));
}
