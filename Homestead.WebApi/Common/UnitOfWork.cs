using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Persistence;

namespace Homestead.WebApi.Common;

/// <inheritdoc cref="IUnitOfWork"/>
public class UnitOfWork(
    AppDbContext db,
    IAuditContext auditContext,
    IAuditLogger auditLogger,
    ICurrentUserAccessor currentUser,
    CommandTransactionCoordinator coordinator) : IUnitOfWork
{
    private Guid? _actorOverride;
    private List<Func<CancellationToken, Task>>? _afterCommitHooks;

    public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken)
    {
        // JOIN: a nested call inside an already-open ambient transaction joins it and returns.
        // The outermost frame owns commit + hook draining. No depth/scope mutation on the join path.
        if (coordinator.TryBeginScope(db.Database))
            return await work();

        try
        {
            var result = await work();
            // Lazy: SaveChangesAsync opens the tx on the first real flush (after its idempotent guard), and
            // the CommandTransactionInterceptor opens it before any change-tracker-bypassing write — so slow
            // non-DB work in the handler holds no pooled connection.
            await SaveChangesAsync(cancellationToken);

            if (coordinator.DecrementDepth() == 0)
            {
                // Commit releases ownership BEFORE draining hooks so a throwing after-commit hook cannot send
                // the catch into a rollback of the already-committed transaction.
                await coordinator.CommitAsync(cancellationToken);
                await RunAfterCommitHooksAsync(cancellationToken);
            }

            return result;
        }
        catch
        {
            coordinator.DecrementDepth();
            // Roll back at most once across nested catch frames; CancellationToken.None so cleanup runs
            // even when the request token is cancelled.
            await coordinator.RollbackOnceAsync();
            throw;
        }
        finally
        {
            await coordinator.DisposeAndResetIfTopAsync();

            if (coordinator.Depth == 0)
                // Discard any hooks a rolled-back command registered so they cannot leak into the next
                // command that reuses this scoped unit of work (the success path already drained them).
                _afterCommitHooks = null;
        }
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        // Idempotent: nothing tracked + no pending audit => true no-op, and (critically) opens no tx.
        // The audit flush below adds rows to the change tracker, so this guard is what makes a second
        // SaveChangesAsync (e.g. the final flush in ExecuteInTransactionAsync) a true no-op.
        if (!auditContext.HasEntries && !db.ChangeTracker.HasChanges())
            return 0;

        // Lazy-open the command transaction on the first real flush. Keeping this explicit call preserves
        // the "no-op command opens no tx" property for the common tracked path; the interceptor is the
        // safety net for the bypass paths and is idempotent, so it harmlessly no-ops when re-entered here.
        await coordinator.EnsureStartedAsync(db.Database, cancellationToken);

        // Flush batched audit through the existing actor-threading seam. The actor is the one set via
        // SetActor (job handlers), otherwise the signed-in user.
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
