using RescueSriLanka.Api.Features.ComponentB.Models;

namespace RescueSriLanka.Api.Features.ComponentB.Services;

// Shared by manual management and internal response synchronization.
internal static class HelpRequestStatusTransition
{
    public static RequestStatusHistory Apply(HelpRequest request, HelpRequestStatus next,
        Guid actor, string? notes)
    {
        var allowed = (request.Status, next) switch
        {
            (HelpRequestStatus.Pending, HelpRequestStatus.Assigned or HelpRequestStatus.Cancelled) => true,
            (HelpRequestStatus.Assigned, HelpRequestStatus.InProgress or HelpRequestStatus.Cancelled) => true,
            (HelpRequestStatus.InProgress, HelpRequestStatus.Resolved or HelpRequestStatus.Cancelled) => true,
            _ => false
        };
        if (!allowed)
            throw new InvalidOperationException($"Cannot change a help request from {request.Status} to {next}.");

        return Record(request, next, actor, notes);
    }

    internal static RequestStatusHistory ReturnToPendingForRecoordination(HelpRequest request, Guid actor, string notes)
    {
        if (request.Status != HelpRequestStatus.Assigned)
            throw new InvalidOperationException($"Cannot return a {request.Status} help request to Pending after plan cancellation.");
        return Record(request, HelpRequestStatus.Pending, actor, notes);
    }

    private static RequestStatusHistory Record(HelpRequest request, HelpRequestStatus next, Guid actor, string? notes)
    {
        var history = new RequestStatusHistory
        {
            HelpRequestId = request.Id, OldStatus = request.Status, NewStatus = next,
            ChangedByUserId = actor, Notes = notes
        };
        request.Status = next;
        request.UpdatedAt = history.ChangedAt;
        return history;
    }
}
