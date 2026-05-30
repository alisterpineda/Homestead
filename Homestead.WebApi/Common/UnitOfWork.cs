using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Common;

/// <inheritdoc cref="IUnitOfWork"/>
public class UnitOfWork(
    AppDbContext db,
    IAuditContext auditContext,
    IAuditLogger auditLogger,
    ICurrentUserAccessor currentUser) : IUnitOfWork
{
    private Guid? _actorOverride;
    private List<Func<CancellationToken, Task>>? _afterCommitHooks;

    public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken)
    {
        // Insurance + correctness for any nested command (ISender dispatched within an already-open
        // scope): join the ambient transaction and let the outermost command own the commit + hook
        // draining. Re-opening here would either throw (relational) or split the work across transactions.
        if (db.Database.CurrentTransaction is not null)
            return await work();

        T result;
        await using (var tx = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            result = await work();
            // Flush batched audit + persist into the open transaction; idempotent guard preserved so a
            // handler that already flushed mid-handler is not double-committed or double-audited.
            await SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }

        // Post-commit side effects run OUTSIDE the transaction: they must observe durably-committed
        // state (SignalR, file deletes, job-processor signals read from a separate connection).
        await RunAfterCommitHooksAsync(cancellationToken);
        return result;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        // Idempotent: when a handler already committed (or there is simply nothing to do), neither
        // re-commit nor re-flush audit. The audit flush below adds rows to the change tracker, so this
        // guard is what makes a second SaveChangesAsync (e.g. from the behavior) a true no-op.
        if (!auditContext.HasEntries && !db.ChangeTracker.HasChanges())
            return 0;

        // Flush batched audit through the existing actor-threading seam. The actor is the request's
        // explicit ActorUserId when supplied (internal jobs), otherwise the signed-in user.
        var actor = _actorOverride ?? currentUser.UserId;
        auditContext.FlushTo(auditLogger.ForUser(actor));

        return await db.SaveChangesAsync(cancellationToken);
    }

    public void OnAfterCommit(Func<CancellationToken, Task> hook)
    {
        (_afterCommitHooks ??= []).Add(hook);
    }

    public void SetActor(Guid? actorUserId) => _actorOverride = actorUserId;

    private async Task RunAfterCommitHooksAsync(CancellationToken cancellationToken)
    {
        if (_afterCommitHooks is null)
            return;

        var hooks = _afterCommitHooks;
        _afterCommitHooks = null;
        foreach (var hook in hooks)
            await hook(cancellationToken);
    }
}
