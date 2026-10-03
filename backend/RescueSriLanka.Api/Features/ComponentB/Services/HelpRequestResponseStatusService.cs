using System.Data.Common;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentB.Models;

namespace RescueSriLanka.Api.Features.ComponentB.Services;

public interface IHelpRequestResponseStatusService
{
    Task SynchronizeAsync(Guid requestId, HelpRequestStatus target, DbTransaction? transaction);
    Task ReturnToPendingForRecoordinationAsync(Guid requestId, Guid cancelledAssignmentId, DbTransaction? transaction);
}

// Internal integration only: no additional HTTP permissions. Component B retains
// ownership of its model, transition rules and history. The caller commits the
// shared transaction only after both component writes have succeeded.
public sealed class HelpRequestResponseStatusService(AppDbContext db,
    IHttpContextAccessor? httpContext = null) : IHelpRequestResponseStatusService
{
    public async Task SynchronizeAsync(Guid requestId, HelpRequestStatus target, DbTransaction? transaction)
    {
        if (target is not (HelpRequestStatus.Assigned or HelpRequestStatus.InProgress or HelpRequestStatus.Resolved))
            throw new ArgumentOutOfRangeException(nameof(target));
        await InResponseTransactionAsync(transaction, context => SynchronizeCoreAsync(context, requestId, target));
    }

    // The response owner calls this only after saving cancellation of a plan
    // without a dispatch. Public/manual status updates never use this operation.
    public Task ReturnToPendingForRecoordinationAsync(Guid requestId, Guid cancelledAssignmentId, DbTransaction? transaction)
        => InResponseTransactionAsync(transaction, async context =>
        {
            var request = await context.HelpRequests.SingleOrDefaultAsync(r => r.Id == requestId)
                ?? throw new InvalidOperationException("The linked Help Request no longer exists.");
            await context.Entry(request).ReloadAsync();
            if (request.Status == HelpRequestStatus.Pending) return; // Legacy plan or already reset.
            context.RequestStatusHistories.Add(HelpRequestStatusTransition.ReturnToPendingForRecoordination(
                request, ActorId(), $"Response plan {cancelledAssignmentId} cancelled before dispatch; re-coordination required."));
            await context.SaveChangesAsync();
        });

    private async Task InResponseTransactionAsync(DbTransaction? transaction, Func<AppDbContext, Task> operation)
    {
        if (db.Database.IsRelational() && transaction is null)
            throw new InvalidOperationException("Response synchronization requires the response transaction.");

        if (transaction is null)
        {
            await operation(db);
            return;
        }

        // A short-lived B context borrows D's connection/transaction. It owns
        // neither, and cannot flush unrelated changes from the scoped B reader.
        await using var enlisted = new AppDbContext((DbContextOptions<AppDbContext>)db.GetService<IDbContextOptions>());
        enlisted.Database.SetDbConnection(transaction.Connection!, contextOwnsConnection: false);
        await enlisted.Database.UseTransactionAsync(transaction);
        await operation(enlisted);
    }

    private Guid ActorId() => Guid.TryParse(httpContext?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : Guid.Empty; // Empty identifies an internal system action.

    private async Task SynchronizeCoreAsync(AppDbContext context, Guid requestId, HelpRequestStatus target)
    {
        var request = await context.HelpRequests.SingleOrDefaultAsync(r => r.Id == requestId)
            ?? throw new InvalidOperationException("The linked Help Request no longer exists.");
        await context.Entry(request).ReloadAsync();
        if (request.Status == target) return;

        var actor = ActorId();
        const string notes = "Synchronized from rescue response lifecycle.";
        // Older response plans may still have Pending requests. Catch up using
        // every legal B transition, preserving the complete history.
        if (request.Status == HelpRequestStatus.Pending && target != HelpRequestStatus.Assigned)
            context.RequestStatusHistories.Add(HelpRequestStatusTransition.Apply(request, HelpRequestStatus.Assigned, actor, notes));
        if (request.Status == HelpRequestStatus.Assigned && target == HelpRequestStatus.Resolved)
            context.RequestStatusHistories.Add(HelpRequestStatusTransition.Apply(request, HelpRequestStatus.InProgress, actor, notes));
        context.RequestStatusHistories.Add(HelpRequestStatusTransition.Apply(request, target, actor, notes));
        await context.SaveChangesAsync();
    }
}
