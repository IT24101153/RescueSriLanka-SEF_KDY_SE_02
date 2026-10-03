using System.Data.Common;
using RescueSriLanka.Api.Features.ComponentB.Models;
using RescueSriLanka.Api.Features.ComponentB.Services;

namespace RescueSriLanka.Api.Tests;

// Incident lifecycle tests fail if they ever attempt Help Request synchronization.
internal sealed class RejectHelpRequestResponseStatusService : IHelpRequestResponseStatusService
{
    public Task ReturnToPendingForRecoordinationAsync(Guid requestId, Guid cancelledAssignmentId, DbTransaction? transaction)
        => throw new InvalidOperationException("Incident flow must not reset Help Requests.");

    public Task SynchronizeAsync(Guid requestId, HelpRequestStatus target, DbTransaction? transaction)
        => throw new InvalidOperationException("Incident flow must not update Help Requests.");
}
